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
                "PrincipalSid",
                "PrincipalType",
                "PermissionLayer",
                "AllowDeny",
                "RightsSummary",
                "EffectiveRightsSummary",
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
                "IsAdminAccount"
            };

            var result = new ExcelExportResult();

            using (var document = SpreadsheetDocument.Create(ioOutputPath, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();
                var sheets = workbookPart.Workbook.AppendChild(new Sheets());
                uint sheetId = 1;

                var userSheetIndex = 1;
                var groupSheetIndex = 1;
                var userSheetWriter = CreateSheetWriter(workbookPart, sheets, BuildSheetName("Users", userSheetIndex), sheetId++, headers);
                var groupSheetWriter = CreateSheetWriter(workbookPart, sheets, BuildSheetName("Groups", groupSheetIndex), sheetId++, headers);

                foreach (var record in ReadFolderPermissions(tempDataPath))
                {
                    if (record == null) continue;
                    if (string.Equals(record.PrincipalType, "Group", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteRecord(ref groupSheetWriter, workbookPart, sheets, "Groups", ref groupSheetIndex, ref sheetId, headers, record);
                        result.GroupRowCount++;
                    }
                    else if (string.Equals(record.PrincipalType, "User", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteRecord(ref userSheetWriter, workbookPart, sheets, "Users", ref userSheetIndex, ref sheetId, headers, record);
                        result.UserRowCount++;
                    }
                }

                result.UserSheetCount = userSheetIndex;
                result.GroupSheetCount = groupSheetIndex;
                userSheetWriter.Dispose();
                groupSheetWriter.Dispose();

                var errorHeaders = new[]
                {
                    "Path",
                    "ErrorType",
                    "Message"
                };
                var errorSheet = workbookPart.AddNewPart<WorksheetPart>();
                WriteErrorsSheet(errorSheet, ReadErrors(errorPath), errorHeaders);
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

        private void WriteErrorsSheet(WorksheetPart sheetPart, IEnumerable<ErrorEntry> errors, string[] headers)
        {
            using (var writer = OpenXmlWriter.Create(sheetPart))
            {
                writer.WriteStartElement(new Worksheet());
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
                writer.WriteEndElement();
            }
        }

        private SheetWriter CreateSheetWriter(WorkbookPart workbookPart, Sheets sheets, string sheetName, uint sheetId, string[] headers)
        {
            var sheetPart = workbookPart.AddNewPart<WorksheetPart>();
            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(sheetPart), SheetId = sheetId, Name = sheetName });
            return new SheetWriter(sheetPart, headers);
        }

        private void WriteRecord(ref SheetWriter writer, WorkbookPart workbookPart, Sheets sheets, string prefix, ref int sheetIndex, ref uint sheetId, string[] headers, ExportRecord record)
        {
            if (writer.RowCount >= MaxDataRowsPerSheet)
            {
                writer.Dispose();
                sheetIndex++;
                writer = CreateSheetWriter(workbookPart, sheets, BuildSheetName(prefix, sheetIndex), sheetId++, headers);
            }

            writer.WriteRow(BuildRowValues(record));
        }

        private string[] BuildRowValues(ExportRecord record)
        {
            return new[]
            {
                GetFolderName(record.FolderPath),
                record.PrincipalName,
                record.PrincipalSid,
                record.PrincipalType,
                record.PermissionLayer.ToString(),
                record.AllowDeny,
                record.RightsSummary,
                record.EffectiveRightsSummary,
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
                record.IsAdminAccount.ToString()
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

        private class SheetWriter : IDisposable
        {
            private readonly OpenXmlWriter _writer;
            public int RowCount { get; private set; }

            public SheetWriter(WorksheetPart sheetPart, string[] headers)
            {
                _writer = OpenXmlWriter.Create(sheetPart);
                _writer.WriteStartElement(new Worksheet());
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
                _writer.WriteEndElement();
                _writer.Dispose();
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
