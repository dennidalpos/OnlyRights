using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var records = LoadFolderPermissions(tempDataPath);
            var errors = LoadErrors(errorPath);
            var groupRecords = records
                .Where(record => string.Equals(record.PrincipalType, "Group", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var userRecords = records
                .Where(record => string.Equals(record.PrincipalType, "User", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var headers = new[]
            {
                "FolderPath",
                "PrincipalName",
                "PrincipalSid",
                "PrincipalType",
                "AllowDeny",
                "RightsSummary",
                "RightsMask",
                "IsInherited",
                "InheritanceFlags",
                "PropagationFlags",
                "Source",
                "Depth",
                "Disabilitato",
                "IsServiceAccount",
                "IsAdminAccount",
                "HasExplicitPermissions",
                "IsInheritanceDisabled",
                "IncludeInherited",
                "ResolveIdentities",
                "ExcludeServiceAccounts",
                "ExcludeAdminAccounts"
            };

            using (var document = SpreadsheetDocument.Create(outputPath, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();
                var sheets = workbookPart.Workbook.AppendChild(new Sheets());

                var userSheet = workbookPart.AddNewPart<WorksheetPart>();
                WriteFolderPermissionsSheet(userSheet, userRecords, headers, "UsersTable", 1);
                sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(userSheet), SheetId = 1, Name = "Users" });

                var groupSheet = workbookPart.AddNewPart<WorksheetPart>();
                WriteFolderPermissionsSheet(groupSheet, groupRecords, headers, "GroupsTable", 2);
                sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(groupSheet), SheetId = 2, Name = "Groups" });

                var aclSheet = workbookPart.AddNewPart<WorksheetPart>();
                WriteFolderPermissionsSheet(aclSheet, records, headers, "AclTable", 3);
                sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(aclSheet), SheetId = 3, Name = "Acl" });

                var errorHeaders = new[]
                {
                    "Path",
                    "ErrorType",
                    "Message"
                };
                var errorSheet = workbookPart.AddNewPart<WorksheetPart>();
                WriteErrorsSheet(errorSheet, errors, errorHeaders, "ErrorsTable", 4);
                sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(errorSheet), SheetId = 4, Name = "Errors" });

                workbookPart.Workbook.Save();
            }
        }

        private List<ExportRecord> LoadFolderPermissions(string tempDataPath)
        {
            var records = new List<ExportRecord>();
            var ioPath = PathResolver.ToExtendedPath(tempDataPath);
            if (!File.Exists(ioPath))
            {
                return records;
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
                records.Add(record);
            }

            return records;
        }

        private List<ErrorEntry> LoadErrors(string errorPath)
        {
            var errors = new List<ErrorEntry>();
            if (string.IsNullOrWhiteSpace(errorPath))
            {
                return errors;
            }

            var ioPath = PathResolver.ToExtendedPath(errorPath);
            if (!File.Exists(ioPath))
            {
                return errors;
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
                errors.Add(entry);
            }

            return errors;
        }

        private void WriteFolderPermissionsSheet(WorksheetPart sheetPart, List<ExportRecord> records, string[] headers, string tableName, uint tableId)
        {
            var columnWidths = InitializeColumnWidths(headers);
            foreach (var record in records)
            {
                UpdateColumnWidths(columnWidths, record.FolderPath, record.PrincipalName, record.PrincipalSid, record.PrincipalType,
                    record.AllowDeny, record.RightsSummary, record.RightsMask.ToString(CultureInfo.InvariantCulture), record.IsInherited.ToString(), record.InheritanceFlags,
                    record.PropagationFlags, record.Source, record.Depth.ToString(CultureInfo.InvariantCulture),
                    record.IsDisabled.ToString(), record.IsServiceAccount.ToString(), record.IsAdminAccount.ToString(),
                    record.HasExplicitPermissions.ToString(), record.IsInheritanceDisabled.ToString(),
                    record.IncludeInherited.ToString(), record.ResolveIdentities.ToString(),
                    record.ExcludeServiceAccounts.ToString(), record.ExcludeAdminAccounts.ToString());
            }
            var rowCount = records.Count + 1;

            using (var writer = OpenXmlWriter.Create(sheetPart))
            {
                writer.WriteStartElement(new Worksheet());
                WriteColumns(writer, columnWidths);
                writer.WriteStartElement(new SheetData());
                WriteRow(writer, headers);

                foreach (var record in records)
                {
                    WriteRow(writer,
                        record.FolderPath,
                        record.PrincipalName,
                        record.PrincipalSid,
                        record.PrincipalType,
                        record.AllowDeny,
                        record.RightsSummary,
                        record.RightsMask.ToString(CultureInfo.InvariantCulture),
                        record.IsInherited.ToString(),
                        record.InheritanceFlags,
                        record.PropagationFlags,
                        record.Source,
                        record.Depth.ToString(CultureInfo.InvariantCulture),
                        record.IsDisabled.ToString(),
                        record.IsServiceAccount.ToString(),
                        record.IsAdminAccount.ToString(),
                        record.HasExplicitPermissions.ToString(),
                        record.IsInheritanceDisabled.ToString(),
                        record.IncludeInherited.ToString(),
                        record.ResolveIdentities.ToString(),
                        record.ExcludeServiceAccounts.ToString(),
                        record.ExcludeAdminAccounts.ToString());
                }

                writer.WriteEndElement();
                WriteTableParts(writer, sheetPart, tableName, tableId, headers, rowCount);
                writer.WriteEndElement();
            }
        }

        private void WriteErrorsSheet(WorksheetPart sheetPart, List<ErrorEntry> errors, string[] headers, string tableName, uint tableId)
        {
            var columnWidths = InitializeColumnWidths(headers);
            foreach (var error in errors)
            {
                UpdateColumnWidths(columnWidths, error.Path, error.ErrorType, error.Message);
            }
            var rowCount = errors.Count + 1;

            using (var writer = OpenXmlWriter.Create(sheetPart))
            {
                writer.WriteStartElement(new Worksheet());
                WriteColumns(writer, columnWidths);
                writer.WriteStartElement(new SheetData());
                WriteRow(writer, headers);

                foreach (var error in errors)
                {
                    WriteRow(writer,
                        error.Path,
                        error.ErrorType,
                        error.Message);
                }

                writer.WriteEndElement();
                WriteTableParts(writer, sheetPart, tableName, tableId, headers, rowCount);
                writer.WriteEndElement();
            }
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
