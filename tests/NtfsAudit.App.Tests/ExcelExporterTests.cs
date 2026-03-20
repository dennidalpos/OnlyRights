/*
 * OnlyRights
 * Copyright (c) 2026 Danny Perondi
 * All rights reserved.
 *
 * Proprietary and confidential.
 * Viewing is permitted only for reference, evaluation, or internal review.
 * Unauthorized copying, modification, distribution, sublicensing,
 * commercial use, or reuse of this file is prohibited without prior
 * written permission from Danny Perondi.
 */
using System;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml.Packaging;
using NtfsAudit.App.Export;
using NtfsAudit.App.Models;
using Newtonsoft.Json;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class ExcelExporterTests
    {
        [Fact]
        public void Export_SplitsSheets_SkipsMetaAndInvalidRows_WritesErrorsAndReplacesOutput()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "NtfsAudit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var dataPath = Path.Combine(tempRoot, "data.jsonl");
                var errorPath = Path.Combine(tempRoot, "errors.jsonl");
                var outputPath = Path.Combine(tempRoot, "report.xlsx");
                File.WriteAllText(outputPath, "legacy-content");

                File.WriteAllLines(dataPath, new[]
                {
                    "{ invalid json",
                    JsonConvert.SerializeObject(new ExportRecord { PrincipalType = "Meta", PrincipalName = "Metadata" }),
                    JsonConvert.SerializeObject(new ExportRecord { PrincipalType = "User", PrincipalName = "SCAN_OPTIONS" }),
                    JsonConvert.SerializeObject(BuildRecord(@"C:\data\users", "user-a", "User")),
                    JsonConvert.SerializeObject(BuildRecord(@"C:\data\users", "user-b", "User")),
                    JsonConvert.SerializeObject(BuildRecord(@"C:\data\groups", "group-a", "Group")),
                    JsonConvert.SerializeObject(BuildRecord(@"C:\data\groups", "group-b", "Group"))
                });

                File.WriteAllLines(errorPath, new[]
                {
                    "{ invalid json",
                    JsonConvert.SerializeObject(new ErrorEntry
                    {
                        Path = @"C:\data\broken",
                        ErrorType = "AccessDenied",
                        Message = "Denied"
                    })
                });

                var exporter = new ExcelExporter(1);

                var result = exporter.Export(dataPath, errorPath, outputPath);

                Assert.Equal(2, result.UserRowCount);
                Assert.Equal(2, result.GroupRowCount);
                Assert.Equal(2, result.UserSheetCount);
                Assert.Equal(2, result.GroupSheetCount);
                Assert.True(result.WasSplit);
                Assert.True(File.Exists(outputPath));
                Assert.DoesNotContain(Directory.GetFiles(tempRoot), path => Path.GetFileName(path).Contains(".tmp_", StringComparison.OrdinalIgnoreCase));

                using (var document = SpreadsheetDocument.Open(outputPath, false))
                {
                    var workbookPart = document.WorkbookPart;
                    var sheetNames = workbookPart.Workbook.Sheets.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>()
                        .Select(sheet => sheet.Name.Value)
                        .ToArray();

                    Assert.Contains("Users_1", sheetNames);
                    Assert.Contains("Users_2", sheetNames);
                    Assert.Contains("Groups_1", sheetNames);
                    Assert.Contains("Groups_2", sheetNames);
                    Assert.Contains("Errors", sheetNames);

                    Assert.Equal(2u, GetSheetRowCount(workbookPart, "Users_1"));
                    Assert.Equal(2u, GetSheetRowCount(workbookPart, "Users_2"));
                    Assert.Equal(2u, GetSheetRowCount(workbookPart, "Groups_1"));
                    Assert.Equal(2u, GetSheetRowCount(workbookPart, "Groups_2"));
                    Assert.Equal(2u, GetSheetRowCount(workbookPart, "Errors"));
                }
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        private static ExportRecord BuildRecord(string folderPath, string principalName, string principalType)
        {
            return new ExportRecord
            {
                FolderPath = folderPath,
                PrincipalName = principalName,
                PrincipalSid = "S-1-5-21-" + principalName,
                PrincipalType = principalType,
                PermissionLayer = PermissionLayer.Ntfs,
                AllowDeny = "Allow",
                RightsSummary = "Read",
                EffectiveRightsSummary = "Read",
                HasExplicitPermissions = true,
                PathKind = PathKind.Local,
                TargetPath = folderPath
            };
        }

        private static uint GetSheetRowCount(WorkbookPart workbookPart, string sheetName)
        {
            var sheet = workbookPart.Workbook.Sheets.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>()
                .Single(candidate => string.Equals(candidate.Name, sheetName, StringComparison.Ordinal));
            var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id);
            return (uint)worksheetPart.Worksheet.Descendants<DocumentFormat.OpenXml.Spreadsheet.Row>().Count();
        }
    }
}
