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
        public void Export(string tempDataPath, string errorPath, string outputPath)
        {
            var ioOutputPath = PathResolver.ToExtendedPath(outputPath);
            var outputDir = Path.GetDirectoryName(ioOutputPath);
            if (!string.IsNullOrWhiteSpace(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var userPredicate = new Func<ExportRecord, bool>(record =>
                string.Equals(record.PrincipalType, "User", StringComparison.OrdinalIgnoreCase));
            var groupPredicate = new Func<ExportRecord, bool>(record =>
                string.Equals(record.PrincipalType, "Group", StringComparison.OrdinalIgnoreCase));
            var allPredicate = new Func<ExportRecord, bool>(record => true);

            var headers = new[]
            {
                "FolderPath",
                "PrincipalName",
                "PrincipalSid",
                "PrincipalType",
                "PermissionLayer",
                "AllowDeny",
                "RightsSummary",
                "RightsMask",
                "EffectiveRightsSummary",
                "EffectiveRightsMask",
                "ShareRightsMask",
                "NtfsRightsMask",
                "IsInherited",
                "AppliesToThisFolder",
                "AppliesToSubfolders",
                "AppliesToFiles",
                "InheritanceFlags",
                "PropagationFlags",
                "Source",
                "Depth",
                "ResourceType",
                "TargetPath",
                "Owner",
                "ShareName",
                "ShareServer",
                "AuditSummary",
                "RiskLevel",
                "Disabilitato",
                "IsServiceAccount",
                "IsAdminAccount",
                "HasExplicitPermissions",
                "IsInheritanceDisabled",
                "IncludeInherited",
                "ResolveIdentities",
                "ExcludeServiceAccounts",
                "ExcludeAdminAccounts",
                "EnableAdvancedAudit",
                "ComputeEffectiveAccess",
                "IncludeSharePermissions",
                "IncludeFiles",
                "ReadOwnerAndSacl",
                "CompareBaseline",
                "ScanAllDepths",
                "MaxDepth",
                "ExpandGroups",
                "UsePowerShell"
            };

            var userScan = ScanFolderPermissions(tempDataPath, userPredicate, headers);
            var groupScan = ScanFolderPermissions(tempDataPath, groupPredicate, headers);
            var allScan = ScanFolderPermissions(tempDataPath, allPredicate, headers);
            var errorHeaders = new[]
            {
                "Path",
                "ErrorType",
                "Message"
            };
            var errorScan = ScanErrors(errorPath, errorHeaders);

            using (var document = SpreadsheetDocument.Create(ioOutputPath, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();
                var sheets = workbookPart.Workbook.AppendChild(new Sheets());

                var userSheet = workbookPart.AddNewPart<WorksheetPart>();
                WriteFolderPermissionsSheet(userSheet, tempDataPath, userPredicate, headers, "UsersTable", 1, userScan);
                sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(userSheet), SheetId = 1, Name = "Users" });

                var groupSheet = workbookPart.AddNewPart<WorksheetPart>();
                WriteFolderPermissionsSheet(groupSheet, tempDataPath, groupPredicate, headers, "GroupsTable", 2, groupScan);
                sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(groupSheet), SheetId = 2, Name = "Groups" });

                var aclSheet = workbookPart.AddNewPart<WorksheetPart>();
                WriteFolderPermissionsSheet(aclSheet, tempDataPath, allPredicate, headers, "AclTable", 3, allScan);
                sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(aclSheet), SheetId = 3, Name = "Acl" });

                var errorSheet = workbookPart.AddNewPart<WorksheetPart>();
                WriteErrorsSheet(errorSheet, errorPath, errorHeaders, "ErrorsTable", 4, errorScan);
                sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(errorSheet), SheetId = 4, Name = "Errors" });

                workbookPart.Workbook.Save();
            }
        }

        private ScanResult ScanFolderPermissions(string tempDataPath, Func<ExportRecord, bool> predicate, string[] headers)
        {
            var widths = InitializeColumnWidths(headers);
            var rowCount = 1;
            var ioPath = PathResolver.ToExtendedPath(tempDataPath);
            if (!File.Exists(ioPath))
            {
                return new ScanResult(rowCount, widths);
            }

            foreach (var line in File.ReadLines(ioPath))
            {
                if (!TryParseRecord(line, out var record)) continue;
                if (!predicate(record)) continue;
                var values = BuildRecordValues(record);
                UpdateColumnWidths(widths, values);
                rowCount += 1;
            }

            return new ScanResult(rowCount, widths);
        }

        private ScanResult ScanErrors(string errorPath, string[] headers)
        {
            var widths = InitializeColumnWidths(headers);
            var rowCount = 1;
            if (string.IsNullOrWhiteSpace(errorPath))
            {
                return new ScanResult(rowCount, widths);
            }

            var ioPath = PathResolver.ToExtendedPath(errorPath);
            if (!File.Exists(ioPath))
            {
                return new ScanResult(rowCount, widths);
            }

            foreach (var line in File.ReadLines(ioPath))
            {
                if (!TryParseError(line, out var entry)) continue;
                UpdateColumnWidths(widths, entry.Path, entry.ErrorType, entry.Message);
                rowCount += 1;
            }

            return new ScanResult(rowCount, widths);
        }

        private void WriteFolderPermissionsSheet(WorksheetPart sheetPart, string tempDataPath, Func<ExportRecord, bool> predicate, string[] headers, string tableName, uint tableId, ScanResult scanResult)
        {
            using (var writer = OpenXmlWriter.Create(sheetPart))
            {
                writer.WriteStartElement(new Worksheet());
                WriteColumns(writer, scanResult.ColumnWidths);
                writer.WriteStartElement(new SheetData());
                WriteRow(writer, headers);

                var ioPath = PathResolver.ToExtendedPath(tempDataPath);
                if (File.Exists(ioPath))
                {
                    foreach (var line in File.ReadLines(ioPath))
                    {
                        if (!TryParseRecord(line, out var record)) continue;
                        if (!predicate(record)) continue;
                        var values = BuildRecordValues(record);
                        WriteRow(writer, values);
                    }
                }

                writer.WriteEndElement();
                WriteTableParts(writer, sheetPart, tableName, tableId, headers, scanResult.RowCount);
                writer.WriteEndElement();
            }
        }

        private void WriteErrorsSheet(WorksheetPart sheetPart, string errorPath, string[] headers, string tableName, uint tableId, ScanResult scanResult)
        {
            using (var writer = OpenXmlWriter.Create(sheetPart))
            {
                writer.WriteStartElement(new Worksheet());
                WriteColumns(writer, scanResult.ColumnWidths);
                writer.WriteStartElement(new SheetData());
                WriteRow(writer, headers);

                var ioPath = PathResolver.ToExtendedPath(errorPath);
                if (File.Exists(ioPath))
                {
                    foreach (var line in File.ReadLines(ioPath))
                    {
                        if (!TryParseError(line, out var error)) continue;
                        WriteRow(writer, error.Path, error.ErrorType, error.Message);
                    }
                }

                writer.WriteEndElement();
                WriteTableParts(writer, sheetPart, tableName, tableId, headers, scanResult.RowCount);
                writer.WriteEndElement();
            }
        }

        private bool TryParseRecord(string line, out ExportRecord record)
        {
            record = null;
            if (string.IsNullOrWhiteSpace(line)) return false;
            try
            {
                record = JsonConvert.DeserializeObject<ExportRecord>(line);
            }
            catch
            {
                return false;
            }
            if (record == null) return false;
            if (string.Equals(record.PrincipalType, "Meta", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(record.PrincipalName, "SCAN_OPTIONS", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return true;
        }

        private bool TryParseError(string line, out ErrorEntry entry)
        {
            entry = null;
            if (string.IsNullOrWhiteSpace(line)) return false;
            try
            {
                entry = JsonConvert.DeserializeObject<ErrorEntry>(line);
            }
            catch
            {
                return false;
            }
            return entry != null;
        }

        private string[] BuildRecordValues(ExportRecord record)
        {
            return new[]
            {
                record.FolderPath,
                record.PrincipalName,
                record.PrincipalSid,
                record.PrincipalType,
                record.PermissionLayer.ToString(),
                record.AllowDeny,
                record.RightsSummary,
                record.RightsMask.ToString(CultureInfo.InvariantCulture),
                record.EffectiveRightsSummary,
                record.EffectiveRightsMask.ToString(CultureInfo.InvariantCulture),
                record.ShareRightsMask.ToString(CultureInfo.InvariantCulture),
                record.NtfsRightsMask.ToString(CultureInfo.InvariantCulture),
                record.IsInherited.ToString(),
                record.AppliesToThisFolder.ToString(),
                record.AppliesToSubfolders.ToString(),
                record.AppliesToFiles.ToString(),
                record.InheritanceFlags,
                record.PropagationFlags,
                record.Source,
                record.Depth.ToString(CultureInfo.InvariantCulture),
                record.ResourceType,
                record.TargetPath,
                record.Owner,
                record.ShareName,
                record.ShareServer,
                record.AuditSummary,
                record.RiskLevel,
                record.IsDisabled.ToString(),
                record.IsServiceAccount.ToString(),
                record.IsAdminAccount.ToString(),
                record.HasExplicitPermissions.ToString(),
                record.IsInheritanceDisabled.ToString(),
                record.IncludeInherited.ToString(),
                record.ResolveIdentities.ToString(),
                record.ExcludeServiceAccounts.ToString(),
                record.ExcludeAdminAccounts.ToString(),
                record.EnableAdvancedAudit.ToString(),
                record.ComputeEffectiveAccess.ToString(),
                record.IncludeSharePermissions.ToString(),
                record.IncludeFiles.ToString(),
                record.ReadOwnerAndSacl.ToString(),
                record.CompareBaseline.ToString(),
                record.ScanAllDepths.ToString(),
                record.MaxDepth.ToString(CultureInfo.InvariantCulture),
                record.ExpandGroups.ToString(),
                record.UsePowerShell.ToString()
            };
        }

        private sealed class ScanResult
        {
            public ScanResult(int rowCount, int[] columnWidths)
            {
                RowCount = rowCount;
                ColumnWidths = columnWidths;
            }

            public int RowCount { get; private set; }
            public int[] ColumnWidths { get; private set; }
        }

        private int[] InitializeColumnWidths(string[] headers)
        {
            var widths = new int[headers.Length];
            for (var i = 0; i < headers.Length; i++)
            {
                widths[i] = headers[i] == null ? 0 : headers[i].Length;
            }
            return widths;
        }

        private void UpdateColumnWidths(int[] widths, params string[] values)
        {
            for (var i = 0; i < widths.Length && i < values.Length; i++)
            {
                var valueLength = values[i] == null ? 0 : values[i].Length;
                if (valueLength > widths[i])
                {
                    widths[i] = valueLength;
                }
            }
        }

        private void WriteColumns(OpenXmlWriter writer, int[] widths)
        {
            writer.WriteStartElement(new Columns());
            for (var i = 0; i < widths.Length; i++)
            {
                var width = CalculateColumnWidth(widths[i]);
                writer.WriteElement(new Column
                {
                    Min = (uint)(i + 1),
                    Max = (uint)(i + 1),
                    Width = width,
                    CustomWidth = true,
                    BestFit = true
                });
            }
            writer.WriteEndElement();
        }

        private double CalculateColumnWidth(int maxLength)
        {
            var width = maxLength + 2;
            if (width < 10) width = 10;
            if (width > 60) width = 60;
            return width;
        }

        private void WriteTableParts(OpenXmlWriter writer, WorksheetPart sheetPart, string tableName, uint tableId, string[] headers, int rowCount)
        {
            if (rowCount < 2)
            {
                return;
            }

            var columnCount = headers.Length;
            var range = string.Format("A1:{0}{1}", ColumnName(columnCount), rowCount);
            var tablePart = sheetPart.AddNewPart<TableDefinitionPart>();
            var table = new Table
            {
                Id = tableId,
                Name = tableName,
                DisplayName = tableName,
                Reference = range,
                TotalsRowShown = false
            };
            var columns = new TableColumns { Count = (uint)columnCount };
            for (var i = 0; i < columnCount; i++)
            {
                columns.Append(new TableColumn { Id = (uint)(i + 1), Name = headers[i] });
            }
            table.Append(new AutoFilter { Reference = range });
            table.Append(columns);
            table.Append(new TableStyleInfo
            {
                Name = "TableStyleMedium2",
                ShowFirstColumn = false,
                ShowLastColumn = false,
                ShowRowStripes = true,
                ShowColumnStripes = false
            });
            tablePart.Table = table;

            writer.WriteStartElement(new TableParts { Count = 1 });
            writer.WriteElement(new TablePart { Id = sheetPart.GetIdOfPart(tablePart) });
            writer.WriteEndElement();
        }

        private string ColumnName(int index)
        {
            var dividend = index;
            var columnName = string.Empty;
            while (dividend > 0)
            {
                var modulo = (dividend - 1) % 26;
                columnName = Convert.ToChar('A' + modulo) + columnName;
                dividend = (dividend - 1) / 26;
            }
            return columnName;
        }

        private void WriteRow(OpenXmlWriter writer, params string[] values)
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

    }
}
