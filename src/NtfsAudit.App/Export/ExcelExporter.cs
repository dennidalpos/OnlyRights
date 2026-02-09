using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Newtonsoft.Json;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;

namespace NtfsAudit.App.Export
{
    public class ExcelExporter
    {
        private const int ExcelMaxRows = 1048576;
        private const int MaxDataRowsPerSheet = ExcelMaxRows - 1;
        private const int MinColumnWidth = 8;
        private const int MaxColumnWidth = 60;

        public ExcelExportResult Export(string tempDataPath, string errorPath, string outputPath)
        {
            var ioOutputPath = PathResolver.ToExtendedPath(outputPath);
            var outputDir = Path.GetDirectoryName(ioOutputPath);
            if (!string.IsNullOrWhiteSpace(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var headers = new[]
            {
                "FolderName",
                "PrincipalName",
                "TargetPath",
                "EffectiveRightsSummary",
                "Source",
                "Owner",
                "PrincipalType",
                "PermissionLayer",
                "AllowDeny",
                "RightsSummary",
                "IsInherited",
                "AppliesToThisFolder",
                "AppliesToSubfolders",
                "AppliesToFiles",
                "InheritanceFlags",
                "PropagationFlags",
                "Depth",
                "ResourceType",
                "ShareName",
                "ShareServer",
                "AuditSummary",
                "RiskLevel",
                "Disabilitato",
                "IsServiceAccount",
                "IsAdminAccount",
                "PrincipalSid"
            };

            var metrics = CollectSheetMetrics(tempDataPath, headers);
            var errorHeaders = new[]
            {
                "Path",
                "ErrorType",
                "Message"
            };
            var errors = new List<ErrorEntry>(ReadErrors(errorPath));
            var errorMetrics = BuildSheetMetrics(errorHeaders);
            foreach (var error in errors)
            {
                errorMetrics.UpdateWidths(new[]
                {
                    error == null ? string.Empty : error.Path,
                    error == null ? string.Empty : error.ErrorType,
                    error == null ? string.Empty : error.Message
                });
                errorMetrics.RowCount++;
            }

            var result = new ExcelExportResult();

            using (var document = SpreadsheetDocument.Create(ioOutputPath, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();
                var sheets = workbookPart.Workbook.AppendChild(new Sheets());
                uint sheetId = 1;
                uint tableId = 1;

                var userSheetIndex = 1;
                var groupSheetIndex = 1;
                var userSheetWriter = CreateSheetWriter(workbookPart, sheets, BuildSheetName("Users", userSheetIndex), sheetId++, headers, metrics.UserSheets[userSheetIndex - 1], ref tableId);
                var groupSheetWriter = CreateSheetWriter(workbookPart, sheets, BuildSheetName("Groups", groupSheetIndex), sheetId++, headers, metrics.GroupSheets[groupSheetIndex - 1], ref tableId);

                foreach (var record in ReadFolderPermissions(tempDataPath))
                {
                    if (record == null) continue;
                    if (string.Equals(record.PrincipalType, "Group", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteRecord(ref groupSheetWriter, workbookPart, sheets, "Groups", ref groupSheetIndex, ref sheetId, headers, record, metrics.GroupSheets, ref tableId);
                        result.GroupRowCount++;
                    }
                    else if (string.Equals(record.PrincipalType, "User", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteRecord(ref userSheetWriter, workbookPart, sheets, "Users", ref userSheetIndex, ref sheetId, headers, record, metrics.UserSheets, ref tableId);
                        result.UserRowCount++;
                    }
                }

                result.UserSheetCount = userSheetIndex;
                result.GroupSheetCount = groupSheetIndex;
                userSheetWriter.Dispose();
                groupSheetWriter.Dispose();

                var errorSheet = workbookPart.AddNewPart<WorksheetPart>();
                WriteErrorsSheet(errorSheet, errors, errorHeaders, errorMetrics, ref tableId);
                sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(errorSheet), SheetId = sheetId, Name = "Errors" });

                workbookPart.Workbook.Save();
            }

            return result;
        }

        private IEnumerable<ExportRecord> ReadFolderPermissions(string tempDataPath)
        {
            var ioPath = PathResolver.ToExtendedPath(tempDataPath);
            if (!File.Exists(ioPath))
            {
                yield break;
            }

            foreach (var line in File.ReadLines(ioPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                ExportRecord record;
                try
                {
                    record = JsonConvert.DeserializeObject<ExportRecord>(line);
                }
                catch
                {
                    continue;
                }
                if (record == null) continue;
                if (string.Equals(record.PrincipalType, "Meta", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(record.PrincipalName, "SCAN_OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                yield return record;
            }
        }

        private IEnumerable<ErrorEntry> ReadErrors(string errorPath)
        {
            if (string.IsNullOrWhiteSpace(errorPath))
            {
                yield break;
            }

            var ioPath = PathResolver.ToExtendedPath(errorPath);
            if (!File.Exists(ioPath))
            {
                yield break;
            }

            foreach (var line in File.ReadLines(ioPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                ErrorEntry entry;
                try
                {
                    entry = JsonConvert.DeserializeObject<ErrorEntry>(line);
                }
                catch
                {
                    continue;
                }
                if (entry == null) continue;
                yield return entry;
            }
        }

        private void WriteErrorsSheet(WorksheetPart sheetPart, IEnumerable<ErrorEntry> errors, string[] headers, SheetMetrics metrics, ref uint tableId)
        {
            var sheetName = "Errors";
            using (var writer = new SheetWriter(sheetPart, headers, metrics, sheetName, ref tableId))
            {
                foreach (var error in errors)
                {
                    writer.WriteRow(new[]
                    {
                        error == null ? string.Empty : error.Path,
                        error == null ? string.Empty : error.ErrorType,
                        error == null ? string.Empty : error.Message
                    });
                }
            }
        }

        private SheetWriter CreateSheetWriter(WorkbookPart workbookPart, Sheets sheets, string sheetName, uint sheetId, string[] headers, SheetMetrics metrics, ref uint tableId)
        {
            var sheetPart = workbookPart.AddNewPart<WorksheetPart>();
            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(sheetPart), SheetId = sheetId, Name = sheetName });
            return new SheetWriter(sheetPart, headers, metrics, sheetName, ref tableId);
        }

        private void WriteRecord(ref SheetWriter writer, WorkbookPart workbookPart, Sheets sheets, string prefix, ref int sheetIndex, ref uint sheetId, string[] headers, ExportRecord record, List<SheetMetrics> metricsPool, ref uint tableId)
        {
            if (writer.RowCount >= MaxDataRowsPerSheet)
            {
                writer.Dispose();
                sheetIndex++;
                writer = CreateSheetWriter(workbookPart, sheets, BuildSheetName(prefix, sheetIndex), sheetId++, headers, metricsPool[sheetIndex - 1], ref tableId);
            }

            writer.WriteRow(BuildRowValues(record));
        }

        private string[] BuildRowValues(ExportRecord record)
        {
            return new[]
            {
                GetFolderName(record.FolderPath),
                record.PrincipalName,
                record.TargetPath,
                record.EffectiveRightsSummary,
                record.Source,
                record.Owner,
                record.PrincipalType,
                record.PermissionLayer.ToString(),
                record.AllowDeny,
                record.RightsSummary,
                record.IsInherited.ToString(),
                record.AppliesToThisFolder.ToString(),
                record.AppliesToSubfolders.ToString(),
                record.AppliesToFiles.ToString(),
                record.InheritanceFlags,
                record.PropagationFlags,
                record.Depth.ToString(CultureInfo.InvariantCulture),
                record.ResourceType,
                record.ShareName,
                record.ShareServer,
                record.AuditSummary,
                record.RiskLevel,
                record.IsDisabled.ToString(),
                record.IsServiceAccount.ToString(),
                record.IsAdminAccount.ToString(),
                record.PrincipalSid
            };
        }

        private string BuildSheetName(string prefix, int index)
        {
            return string.Format("{0}_{1}", prefix, index);
        }

        private string GetFolderName(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return string.Empty;
            }

            var trimmed = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? folderPath : name;
        }

        private static void WriteRow(OpenXmlWriter writer, params string[] values)
        {
            writer.WriteStartElement(new Row());
            foreach (var value in values)
            {
                writer.WriteElement(new Cell
                {
                    DataType = CellValues.InlineString,
                    InlineString = new InlineString(new Text(value ?? string.Empty))
                });
            }
            writer.WriteEndElement();
        }

        private SheetMetricsCollection CollectSheetMetrics(string tempDataPath, string[] headers)
        {
            var userSheets = new List<SheetMetrics> { BuildSheetMetrics(headers) };
            var groupSheets = new List<SheetMetrics> { BuildSheetMetrics(headers) };
            var userIndex = 0;
            var groupIndex = 0;

            foreach (var record in ReadFolderPermissions(tempDataPath))
            {
                if (record == null) continue;
                var rowValues = BuildRowValues(record);
                if (string.Equals(record.PrincipalType, "Group", StringComparison.OrdinalIgnoreCase))
                {
                    if (groupSheets[groupIndex].RowCount >= MaxDataRowsPerSheet)
                    {
                        groupIndex++;
                        groupSheets.Add(BuildSheetMetrics(headers));
                    }
                    groupSheets[groupIndex].UpdateWidths(rowValues);
                    groupSheets[groupIndex].RowCount++;
                }
                else if (string.Equals(record.PrincipalType, "User", StringComparison.OrdinalIgnoreCase))
                {
                    if (userSheets[userIndex].RowCount >= MaxDataRowsPerSheet)
                    {
                        userIndex++;
                        userSheets.Add(BuildSheetMetrics(headers));
                    }
                    userSheets[userIndex].UpdateWidths(rowValues);
                    userSheets[userIndex].RowCount++;
                }
            }

            return new SheetMetricsCollection(userSheets, groupSheets);
        }

        private static SheetMetrics BuildSheetMetrics(string[] headers)
        {
            var metrics = new SheetMetrics(headers.Length);
            metrics.UpdateWidths(headers);
            return metrics;
        }

        private static double[] BuildColumnWidths(SheetMetrics metrics)
        {
            var widths = new double[metrics.MaxWidths.Length];
            for (var i = 0; i < metrics.MaxWidths.Length; i++)
            {
                widths[i] = Math.Min(MaxColumnWidth, Math.Max(MinColumnWidth, metrics.MaxWidths[i] + 2));
            }
            return widths;
        }

        private static string GetColumnName(int index)
        {
            var dividend = index;
            var columnName = string.Empty;
            while (dividend > 0)
            {
                var modulo = (dividend - 1) % 26;
                columnName = Convert.ToChar(65 + modulo) + columnName;
                dividend = (dividend - modulo) / 26;
            }
            return columnName;
        }

        private class SheetMetrics
        {
            public int RowCount { get; set; }
            public int[] MaxWidths { get; }

            public SheetMetrics(int columnCount)
            {
                MaxWidths = new int[columnCount];
            }

            public void UpdateWidths(string[] values)
            {
                if (values == null) return;
                var count = Math.Min(values.Length, MaxWidths.Length);
                for (var i = 0; i < count; i++)
                {
                    var length = string.IsNullOrEmpty(values[i]) ? 0 : values[i].Length;
                    if (length > MaxWidths[i])
                    {
                        MaxWidths[i] = length;
                    }
                }
            }
        }

        private class SheetMetricsCollection
        {
            public List<SheetMetrics> UserSheets { get; }
            public List<SheetMetrics> GroupSheets { get; }

            public SheetMetricsCollection(List<SheetMetrics> userSheets, List<SheetMetrics> groupSheets)
            {
                UserSheets = userSheets;
                GroupSheets = groupSheets;
            }
        }

        private class SheetWriter : IDisposable
        {
            private readonly OpenXmlWriter _writer;
            private readonly WorksheetPart _sheetPart;
            private readonly string[] _headers;
            private readonly string _sheetName;
            private readonly string _tableName;
            private readonly uint _tableId;
            public int RowCount { get; private set; }

            public SheetWriter(WorksheetPart sheetPart, string[] headers, SheetMetrics metrics, string sheetName, ref uint tableId)
            {
                _sheetPart = sheetPart;
                _headers = headers;
                _sheetName = sheetName;
                _tableId = tableId;
                _tableName = string.Format("Table{0}", _tableId);
                tableId++;
                _writer = OpenXmlWriter.Create(sheetPart);
                _writer.WriteStartElement(new Worksheet());
                WriteColumns(metrics);
                _writer.WriteStartElement(new SheetData());
                ExcelExporter.WriteRow(_writer, headers);
            }

            public void WriteRow(string[] values)
            {
                ExcelExporter.WriteRow(_writer, values);
                RowCount++;
            }

            public void Dispose()
            {
                _writer.WriteEndElement();
                WriteTableParts();
                _writer.WriteEndElement();
                _writer.Dispose();
            }

            private void WriteColumns(SheetMetrics metrics)
            {
                var widths = BuildColumnWidths(metrics);
                _writer.WriteStartElement(new Columns());
                for (var i = 0; i < widths.Length; i++)
                {
                    var columnIndex = (uint)(i + 1);
                    _writer.WriteElement(new Column
                    {
                        Min = columnIndex,
                        Max = columnIndex,
                        Width = widths[i],
                        CustomWidth = true,
                        BestFit = true
                    });
                }
                _writer.WriteEndElement();
            }

            private void WriteTableParts()
            {
                if (RowCount == 0)
                {
                    return;
                }
                var tableDefinitionPart = _sheetPart.AddNewPart<TableDefinitionPart>();
                var relId = _sheetPart.GetIdOfPart(tableDefinitionPart);
                var totalRows = Math.Max(1, RowCount + 1);
                var totalColumns = _headers.Length;
                var reference = string.Format("{0}1:{1}{2}", GetColumnName(1), GetColumnName(totalColumns), totalRows);
                tableDefinitionPart.Table = new Table(
                    new AutoFilter { Reference = reference },
                    new TableColumns(BuildTableColumns())
                    {
                        Count = (uint)totalColumns
                    },
                    new TableStyleInfo
                    {
                        Name = "TableStyleMedium9",
                        ShowFirstColumn = false,
                        ShowLastColumn = false,
                        ShowRowStripes = true,
                        ShowColumnStripes = false
                    })
                {
                    Id = _tableId,
                    Name = _tableName,
                    DisplayName = _tableName,
                    Reference = reference,
                    TotalsRowShown = false
                };
                tableDefinitionPart.Table.Save();

                _writer.WriteStartElement(new TableParts { Count = 1 });
                _writer.WriteElement(new TablePart { Id = relId });
                _writer.WriteEndElement();
            }

            private IEnumerable<TableColumn> BuildTableColumns()
            {
                for (var i = 0; i < _headers.Length; i++)
                {
                    yield return new TableColumn { Id = (uint)(i + 1), Name = _headers[i] };
                }
            }
        }
    }

    public class ExcelExportResult
    {
        public int UserRowCount { get; set; }
        public int GroupRowCount { get; set; }
        public int UserSheetCount { get; set; }
        public int GroupSheetCount { get; set; }

        public bool WasSplit
        {
            get { return UserSheetCount > 1 || GroupSheetCount > 1; }
        }
    }
}
