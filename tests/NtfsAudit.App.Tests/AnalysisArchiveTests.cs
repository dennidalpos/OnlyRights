using System;
using System.Collections.Generic;
using System.IO;
using NtfsAudit.App.Export;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class AnalysisArchiveTests
    {
        [Fact]
        public void Import_NormalizesForwardSlashPathsInData()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "NtfsAudit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var dataPath = Path.Combine(tempRoot, "scan.jsonl");
                var errorPath = Path.Combine(tempRoot, "errors.jsonl");
                var archivePath = Path.Combine(tempRoot, "analysis.ntaudit");

                var record = new ExportRecord
                {
                    FolderPath = @"\\server/share/folder",
                    PrincipalName = "Everyone",
                    PrincipalSid = "S-1-1-0",
                    PrincipalType = "Group",
                    PermissionLayer = PermissionLayer.Ntfs,
                    AllowDeny = "Allow",
                    RightsSummary = "Read",
                    EffectiveRightsSummary = "Read",
                    HasExplicitPermissions = true
                };

                File.WriteAllText(dataPath, Newtonsoft.Json.JsonConvert.SerializeObject(record) + Environment.NewLine);
                File.WriteAllText(errorPath, string.Empty);

                var archive = new AnalysisArchive();
                archive.Export(new ScanResult
                {
                    TempDataPath = dataPath,
                    ErrorPath = errorPath,
                    RootPath = @"\\server\share",
                    RootPathKind = PathKind.Unc,
                    Details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase),
                    TreeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                    ScanOptions = new ScanOptions { RootPath = @"\\server\share" },
                    ScannedAtUtc = DateTime.UtcNow
                }, @"\\server\share", archivePath);

                var imported = archive.Import(archivePath);

                Assert.NotNull(imported.ScanResult);
                Assert.Contains(imported.ScanResult.Details.Keys, key => key.Contains("\\server\\share\\folder", StringComparison.OrdinalIgnoreCase));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Fact]
        public void Import_PreservesServiceAndAdminFlagsWhenSidIsMissing()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "NtfsAudit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var dataPath = Path.Combine(tempRoot, "scan.jsonl");
                var errorPath = Path.Combine(tempRoot, "errors.jsonl");
                var archivePath = Path.Combine(tempRoot, "analysis.ntaudit");

                var serviceRecord = new ExportRecord
                {
                    FolderPath = @"C:\data",
                    PrincipalName = "svc_custom",
                    PrincipalType = "User",
                    PermissionLayer = PermissionLayer.Ntfs,
                    AllowDeny = "Allow",
                    RightsSummary = "Read",
                    EffectiveRightsSummary = "Read",
                    IsServiceAccount = true,
                    IsAdminAccount = true,
                    HasExplicitPermissions = true
                };

                File.WriteAllText(dataPath, Newtonsoft.Json.JsonConvert.SerializeObject(serviceRecord) + Environment.NewLine);
                File.WriteAllText(errorPath, string.Empty);

                var archive = new AnalysisArchive();
                archive.Export(new ScanResult
                {
                    TempDataPath = dataPath,
                    ErrorPath = errorPath,
                    RootPath = @"C:\data",
                    RootPathKind = PathKind.Local,
                    Details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase),
                    TreeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                    ScanOptions = new ScanOptions { RootPath = @"C:\data" },
                    ScannedAtUtc = DateTime.UtcNow
                }, @"C:\data", archivePath);

                var imported = archive.Import(archivePath);
                var first = Assert.Single(imported.ScanResult.Details[@"C:\data"].AllEntries);

                Assert.True(first.IsServiceAccount);
                Assert.True(first.IsAdminAccount);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Fact]
        public void Import_UsesDedicatedAnalysisImportWorkspace()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "NtfsAudit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var dataPath = Path.Combine(tempRoot, "scan.jsonl");
                var errorPath = Path.Combine(tempRoot, "errors.jsonl");
                var archivePath = Path.Combine(tempRoot, "analysis.ntaudit");

                var record = new ExportRecord
                {
                    FolderPath = @"C:\data",
                    PrincipalName = "Everyone",
                    PrincipalSid = "S-1-1-0",
                    PrincipalType = "Group",
                    PermissionLayer = PermissionLayer.Ntfs,
                    AllowDeny = "Allow",
                    RightsSummary = "Read",
                    EffectiveRightsSummary = "Read",
                    HasExplicitPermissions = true
                };

                File.WriteAllText(dataPath, Newtonsoft.Json.JsonConvert.SerializeObject(record) + Environment.NewLine);
                File.WriteAllText(errorPath, string.Empty);

                var archive = new AnalysisArchive();
                archive.Export(new ScanResult
                {
                    TempDataPath = dataPath,
                    ErrorPath = errorPath,
                    RootPath = @"C:\data",
                    RootPathKind = PathKind.Local,
                    Details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase),
                    TreeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                    ScanOptions = new ScanOptions { RootPath = @"C:\data" },
                    ScannedAtUtc = DateTime.UtcNow
                }, @"C:\data", archivePath);

                var imported = archive.Import(archivePath);

                Assert.NotNull(imported.ScanResult);
                Assert.Contains(Path.Combine("NtfsAudit", "imports"), imported.ScanResult.TempDataPath, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Fact]
        public void Export_CleansObsoleteAnalysisExportWorkspaceEntries()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "NtfsAudit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var workspace = Path.Combine(Path.GetTempPath(), "NtfsAudit", "exports");
                Directory.CreateDirectory(workspace);
                var obsoleteFile = Path.Combine(workspace, string.Format("stale_{0}.tmp", Guid.NewGuid().ToString("N")));
                File.WriteAllText(obsoleteFile, "stale");
                File.SetLastWriteTimeUtc(obsoleteFile, DateTime.UtcNow.AddDays(-10));

                var dataPath = Path.Combine(tempRoot, "scan.jsonl");
                var errorPath = Path.Combine(tempRoot, "errors.jsonl");
                var archivePath = Path.Combine(tempRoot, "analysis.ntaudit");

                var record = new ExportRecord
                {
                    FolderPath = @"C:\data",
                    PrincipalName = "Everyone",
                    PrincipalSid = "S-1-1-0",
                    PrincipalType = "Group",
                    PermissionLayer = PermissionLayer.Ntfs,
                    AllowDeny = "Allow",
                    RightsSummary = "Read",
                    EffectiveRightsSummary = "Read",
                    HasExplicitPermissions = true
                };

                File.WriteAllText(dataPath, Newtonsoft.Json.JsonConvert.SerializeObject(record) + Environment.NewLine);
                File.WriteAllText(errorPath, string.Empty);

                var archive = new AnalysisArchive();
                archive.Export(new ScanResult
                {
                    TempDataPath = dataPath,
                    ErrorPath = errorPath,
                    RootPath = @"C:\data",
                    RootPathKind = PathKind.Local,
                    Details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase),
                    TreeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                    ScanOptions = new ScanOptions { RootPath = @"C:\data" },
                    ScannedAtUtc = DateTime.UtcNow
                }, @"C:\data", archivePath);

                Assert.False(File.Exists(obsoleteFile));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Fact]
        public void Import_CleansObsoleteAnalysisImportWorkspaceEntries()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "NtfsAudit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var dataPath = Path.Combine(tempRoot, "scan.jsonl");
                var errorPath = Path.Combine(tempRoot, "errors.jsonl");
                var archivePath = Path.Combine(tempRoot, "analysis.ntaudit");

                var record = new ExportRecord
                {
                    FolderPath = @"C:\data",
                    PrincipalName = "Everyone",
                    PrincipalSid = "S-1-1-0",
                    PrincipalType = "Group",
                    PermissionLayer = PermissionLayer.Ntfs,
                    AllowDeny = "Allow",
                    RightsSummary = "Read",
                    EffectiveRightsSummary = "Read",
                    HasExplicitPermissions = true
                };

                File.WriteAllText(dataPath, Newtonsoft.Json.JsonConvert.SerializeObject(record) + Environment.NewLine);
                File.WriteAllText(errorPath, string.Empty);

                var archive = new AnalysisArchive();
                archive.Export(new ScanResult
                {
                    TempDataPath = dataPath,
                    ErrorPath = errorPath,
                    RootPath = @"C:\data",
                    RootPathKind = PathKind.Local,
                    Details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase),
                    TreeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                    ScanOptions = new ScanOptions { RootPath = @"C:\data" },
                    ScannedAtUtc = DateTime.UtcNow
                }, @"C:\data", archivePath);

                var workspace = Path.Combine(Path.GetTempPath(), "NtfsAudit", "imports");
                var obsoleteDir = Path.Combine(workspace, string.Format("stale_{0}", Guid.NewGuid().ToString("N")));
                Directory.CreateDirectory(obsoleteDir);
                Directory.SetLastWriteTimeUtc(obsoleteDir, DateTime.UtcNow.AddDays(-10));

                archive.Import(archivePath);

                Assert.False(Directory.Exists(obsoleteDir));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }
        [Fact]
        public void Export_WritesRequiredEntriesAndMetaCounts()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "NtfsAudit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var dataPath = Path.Combine(tempRoot, "scan.jsonl");
                var errorPath = Path.Combine(tempRoot, "errors.jsonl");
                var archivePath = Path.Combine(tempRoot, "analysis.ntaudit");

                var line = Newtonsoft.Json.JsonConvert.SerializeObject(new ExportRecord
                {
                    FolderPath = @"C:\data",
                    PrincipalName = "Everyone",
                    PrincipalSid = "S-1-1-0",
                    PrincipalType = "Group",
                    PermissionLayer = PermissionLayer.Ntfs,
                    AllowDeny = "Allow",
                    RightsSummary = "Read",
                    EffectiveRightsSummary = "Read",
                    HasExplicitPermissions = true
                });
                File.WriteAllText(dataPath, line + Environment.NewLine);
                File.WriteAllText(errorPath, "err" + Environment.NewLine);

                var archive = new AnalysisArchive();
                archive.Export(new ScanResult
                {
                    TempDataPath = dataPath,
                    ErrorPath = errorPath,
                    RootPath = @"C:\data",
                    RootPathKind = PathKind.Local,
                    Details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase),
                    TreeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                    ScanOptions = new ScanOptions { RootPath = @"C:\data" },
                    ScannedAtUtc = DateTime.UtcNow
                }, @"C:\data", archivePath);

                using (var zip = System.IO.Compression.ZipFile.OpenRead(archivePath))
                {
                    Assert.NotNull(zip.GetEntry("data.jsonl"));
                    Assert.NotNull(zip.GetEntry("errors.jsonl"));
                    Assert.NotNull(zip.GetEntry("tree.json"));
                    Assert.NotNull(zip.GetEntry("folderflags.json"));
                    Assert.NotNull(zip.GetEntry("analysis.sqlite"));
                    var meta = zip.GetEntry("meta.json");
                    Assert.NotNull(meta);
                    using (var stream = meta.Open())
                    using (var reader = new StreamReader(stream))
                    {
                        var text = reader.ReadToEnd();
                        Assert.Contains("\"DataRecordCount\":1", text);
                        Assert.Contains("\"ErrorRecordCount\":1", text);
                    }
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

        [Fact]
        public void Import_UsesSqliteBackendForLargeDatasets()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "NtfsAudit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var dataPath = Path.Combine(tempRoot, "scan.jsonl");
                var errorPath = Path.Combine(tempRoot, "errors.jsonl");
                var archivePath = Path.Combine(tempRoot, "analysis_large.ntaudit");
                using (var writer = new StreamWriter(dataPath))
                {
                    for (var i = 0; i < 5000; i++)
                    {
                        writer.WriteLine(Newtonsoft.Json.JsonConvert.SerializeObject(new ExportRecord
                        {
                            FolderPath = @"C:\data",
                            PrincipalName = "User" + i,
                            PrincipalSid = "S-1-5-21-" + i,
                            PrincipalType = "User",
                            PermissionLayer = PermissionLayer.Ntfs,
                            AllowDeny = "Allow",
                            RightsSummary = "Read",
                            EffectiveRightsSummary = "Read",
                            HasExplicitPermissions = true
                        }));
                    }
                }

                File.WriteAllText(errorPath, string.Empty);

                var archive = new AnalysisArchive();
                archive.Export(new ScanResult
                {
                    TempDataPath = dataPath,
                    ErrorPath = errorPath,
                    RootPath = @"C:\data",
                    RootPathKind = PathKind.Local,
                    Details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase)
                    {
                        [@"C:\data"] = new FolderDetail { HasExplicitPermissions = true, HasExplicitNtfs = true, HasFolderEntries = true }
                    },
                    TreeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                    {
                        [@"C:\data"] = new List<string>()
                    },
                    ScanOptions = new ScanOptions { RootPath = @"C:\data" },
                    ScannedAtUtc = DateTime.UtcNow
                }, @"C:\data", archivePath);

                var imported = archive.Import(archivePath);

                Assert.True(imported.UsesSqliteBackend);
                Assert.True(imported.ScanResult.UsesSqliteBackend);
                Assert.False(imported.ScanResult.Details[@"C:\data"].EntriesLoaded);
                var loadedDetail = new AnalysisSqliteStore().LoadFolderDetail(imported.ScanResult.SqliteDatabasePath, @"C:\data");
                Assert.Equal(5000, loadedDetail.AllEntries.Count);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Fact]
        public void Import_ThrowsWhenMetaCountsDoNotMatchData()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "NtfsAudit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var dataPath = Path.Combine(tempRoot, "scan.jsonl");
                var errorPath = Path.Combine(tempRoot, "errors.jsonl");
                var archivePath = Path.Combine(tempRoot, "analysis.ntaudit");

                var line = Newtonsoft.Json.JsonConvert.SerializeObject(new ExportRecord
                {
                    FolderPath = @"C:\data",
                    PrincipalName = "Everyone",
                    PrincipalSid = "S-1-1-0",
                    PrincipalType = "Group",
                    PermissionLayer = PermissionLayer.Ntfs,
                    AllowDeny = "Allow",
                    RightsSummary = "Read",
                    EffectiveRightsSummary = "Read",
                    HasExplicitPermissions = true
                });
                File.WriteAllText(dataPath, line + Environment.NewLine);
                File.WriteAllText(errorPath, string.Empty);

                var archive = new AnalysisArchive();
                archive.Export(new ScanResult
                {
                    TempDataPath = dataPath,
                    ErrorPath = errorPath,
                    RootPath = @"C:\data",
                    RootPathKind = PathKind.Local,
                    Details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase),
                    TreeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                    ScanOptions = new ScanOptions { RootPath = @"C:\data" },
                    ScannedAtUtc = DateTime.UtcNow
                }, @"C:\data", archivePath);

                using (var zip = System.IO.Compression.ZipFile.Open(archivePath, System.IO.Compression.ZipArchiveMode.Update))
                {
                    var metaEntry = zip.GetEntry("meta.json");
                    Assert.NotNull(metaEntry);
                    string metaText;
                    using (var stream = metaEntry.Open())
                    using (var reader = new StreamReader(stream))
                    {
                        metaText = reader.ReadToEnd();
                    }

                    metaText = metaText.Replace("\"DataRecordCount\":1", "\"DataRecordCount\":2");
                    metaEntry.Delete();
                    var rewritten = zip.CreateEntry("meta.json");
                    using (var stream = rewritten.Open())
                    using (var writer = new StreamWriter(stream))
                    {
                        writer.Write(metaText);
                    }
                }

                Assert.Throws<InvalidDataException>(() => archive.Import(archivePath));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Fact]
        public void Export_AppendsNtauditExtensionWhenMissing()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "NtfsAudit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var dataPath = Path.Combine(tempRoot, "scan.jsonl");
                var errorPath = Path.Combine(tempRoot, "errors.jsonl");
                var archivePathWithoutExtension = Path.Combine(tempRoot, "analysis_report");

                var line = Newtonsoft.Json.JsonConvert.SerializeObject(new ExportRecord
                {
                    FolderPath = @"C:\data",
                    PrincipalName = "Everyone",
                    PrincipalSid = "S-1-1-0",
                    PrincipalType = "Group",
                    PermissionLayer = PermissionLayer.Ntfs,
                    AllowDeny = "Allow",
                    RightsSummary = "Read",
                    EffectiveRightsSummary = "Read",
                    HasExplicitPermissions = true
                });
                File.WriteAllText(dataPath, line + Environment.NewLine);
                File.WriteAllText(errorPath, string.Empty);

                var archive = new AnalysisArchive();
                archive.Export(new ScanResult
                {
                    TempDataPath = dataPath,
                    ErrorPath = errorPath,
                    RootPath = @"C:\data",
                    RootPathKind = PathKind.Local,
                    Details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase),
                    TreeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                    ScanOptions = new ScanOptions { RootPath = @"C:\data" },
                    ScannedAtUtc = DateTime.UtcNow
                }, @"C:\data", archivePathWithoutExtension);

                Assert.True(File.Exists(archivePathWithoutExtension + ".ntaudit"));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }


    }
}
