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
            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            var summaries = new Dictionary<string, SummaryEntry>();
            var folderHeaders = new[]
            {
                "FolderPath",
                "PrincipalName",
                "PrincipalSid",
                "PrincipalType",
                "AllowDeny",
                "RightsSummary",
                "IsInherited",
                "InheritanceFlags",
                "PropagationFlags",
                "Source",
                "Depth"
            };
            var folderColumnWidths = InitializeColumnWidths(folderHeaders);
            var folderRowCount = AnalyzeFolderPermissions(tempDataPath, summaries, folderColumnWidths);

            var summaryHeaders = new[] { "PrincipalName", "PrincipalSid", "FoldersCount", "HighestRightsSummary", "Notes" };
            var summaryColumnWidths = InitializeColumnWidths(summaryHeaders);
            var summaryRowCount = AnalyzeSummary(summaries, summaryColumnWidths);

            var errorHeaders = new[] { "Path", "ErrorType", "Message" };
            var errorColumnWidths = InitializeColumnWidths(errorHeaders);
            var errors = AnalyzeErrors(errorPath, errorColumnWidths);
            var errorRowCount = errors.Count + 1;

            using (var document = SpreadsheetDocument.Create(outputPath, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();
                var sheets = workbookPart.Workbook.AppendChild(new Sheets());

                var folderSheet = workbookPart.AddNewPart<WorksheetPart>();
                WriteFolderPermissionsSheet(folderSheet, tempDataPath, folderHeaders, folderColumnWidths, folderRowCount);
                sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(folderSheet), SheetId = 1, Name = "FolderPermissions" });

                var summarySheet = workbookPart.AddNewPart<WorksheetPart>();
                WriteSummarySheet(summarySheet, summaries, summaryHeaders, summaryColumnWidths, summaryRowCount);
                sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(summarySheet), SheetId = 2, Name = "SummaryByPrincipal" });

                var errorSheet = workbookPart.AddNewPart<WorksheetPart>();
                WriteErrorSheet(errorSheet, errors, errorHeaders, errorColumnWidths, errorRowCount);
                sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(errorSheet), SheetId = 3, Name = "Errors" });

                workbookPart.Workbook.Save();
            }
        }

        private int AnalyzeFolderPermissions(string tempDataPath, Dictionary<string, SummaryEntry> summaries, int[] columnWidths)
        {
            var rowCount = 1;
            if (!File.Exists(tempDataPath))
            {
                return rowCount;
            }

            foreach (var line in File.ReadLines(tempDataPath))
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
                UpdateColumnWidths(columnWidths, record.FolderPath, record.PrincipalName, record.PrincipalSid, record.PrincipalType,
                    record.AllowDeny, record.RightsSummary, record.IsInherited.ToString(), record.InheritanceFlags,
                    record.PropagationFlags, record.Source, record.Depth.ToString(CultureInfo.InvariantCulture));
                rowCount++;

                SummaryEntry summary;
                if (!summaries.TryGetValue(record.PrincipalSid ?? record.PrincipalName, out summary))
                {
                    summary = new SummaryEntry
                    {
                        PrincipalName = record.PrincipalName,
                        PrincipalSid = record.PrincipalSid
                    };
                    summaries[record.PrincipalSid ?? record.PrincipalName] = summary;
                }

                summary.Folders.Add(record.FolderPath);
                var rank = RightsNormalizer.Rank(record.RightsSummary ?? string.Empty);
                if (rank > summary.HighestRank)
                {
                    summary.HighestRank = rank;
                    summary.HighestRights = record.RightsSummary;
                }
            }

            return rowCount;
        }

        private int AnalyzeSummary(Dictionary<string, SummaryEntry> summaries, int[] columnWidths)
        {
            var rowCount = 1;
            foreach (var summary in summaries.Values)
            {
                UpdateColumnWidths(columnWidths, summary.PrincipalName, summary.PrincipalSid,
                    summary.Folders.Count.ToString(CultureInfo.InvariantCulture), summary.HighestRights, string.Empty);
                rowCount++;
            }
            return rowCount;
        }

        private List<ErrorEntry> AnalyzeErrors(string errorPath, int[] columnWidths)
        {
            var errors = new List<ErrorEntry>();
            if (!File.Exists(errorPath))
            {
                return errors;
            }

            foreach (var line in File.ReadLines(errorPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                ErrorEntry error;
                try
                {
                    error = JsonConvert.DeserializeObject<ErrorEntry>(line);
                }
                catch
                {
                    continue;
                }
                if (error == null) continue;
                UpdateColumnWidths(columnWidths, error.Path, error.ErrorType, error.Message);
                errors.Add(error);
            }

            return errors;
        }

        private void WriteFolderPermissionsSheet(WorksheetPart sheetPart, string tempDataPath, string[] headers, int[] columnWidths, int rowCount)
        {
            using (var writer = OpenXmlWriter.Create(sheetPart))
            {
                writer.WriteStartElement(new Worksheet());
                WriteColumns(writer, columnWidths);
                writer.WriteStartElement(new SheetData());
                WriteRow(writer, headers);

                if (File.Exists(tempDataPath))
                {
                    foreach (var line in File.ReadLines(tempDataPath))
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
                        WriteRow(writer,
                            record.FolderPath,
                            record.PrincipalName,
                            record.PrincipalSid,
                            record.PrincipalType,
                            record.AllowDeny,
                            record.RightsSummary,
                            record.IsInherited.ToString(),
                            record.InheritanceFlags,
                            record.PropagationFlags,
                            record.Source,
                            record.Depth.ToString(CultureInfo.InvariantCulture));
                    }
                }

                writer.WriteEndElement();
                WriteTableParts(writer, sheetPart, "FolderPermissionsTable", 1, headers, rowCount);
                writer.WriteEndElement();
            }
        }

        private void WriteSummarySheet(WorksheetPart sheetPart, Dictionary<string, SummaryEntry> summaries, string[] headers, int[] columnWidths, int rowCount)
        {
            using (var writer = OpenXmlWriter.Create(sheetPart))
            {
                writer.WriteStartElement(new Worksheet());
                WriteColumns(writer, columnWidths);
                writer.WriteStartElement(new SheetData());
                WriteRow(writer, headers);

                foreach (var summary in summaries.Values)
                {
                    WriteRow(writer,
                        summary.PrincipalName,
                        summary.PrincipalSid,
                        summary.Folders.Count.ToString(CultureInfo.InvariantCulture),
                        summary.HighestRights,
                        string.Empty);
                }

                writer.WriteEndElement();
                WriteTableParts(writer, sheetPart, "SummaryByPrincipalTable", 2, headers, rowCount);
                writer.WriteEndElement();
            }
        }

        private void WriteErrorSheet(WorksheetPart sheetPart, List<ErrorEntry> errors, string[] headers, int[] columnWidths, int rowCount)
        {
            using (var writer = OpenXmlWriter.Create(sheetPart))
            {
                writer.WriteStartElement(new Worksheet());
                WriteColumns(writer, columnWidths);
                writer.WriteStartElement(new SheetData());
                WriteRow(writer, headers);
                foreach (var error in errors)
                {
                    WriteRow(writer, error.Path, error.ErrorType, error.Message);
                }

                writer.WriteEndElement();
                WriteTableParts(writer, sheetPart, "ErrorsTable", 3, headers, rowCount);
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

        private class SummaryEntry
        {
            public string PrincipalName { get; set; }
            public string PrincipalSid { get; set; }
            public HashSet<string> Folders { get; private set; }
            public int HighestRank { get; set; }
            public string HighestRights { get; set; }

            public SummaryEntry()
            {
                Folders = new HashSet<string>();
            }
        }
    }
}
