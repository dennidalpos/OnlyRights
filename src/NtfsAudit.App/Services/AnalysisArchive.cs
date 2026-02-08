using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json;
using NtfsAudit.App.Export;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public class AnalysisArchive
    {
        private const string DataEntryName = "data.jsonl";
        private const string ErrorsEntryName = "errors.jsonl";
        private const string TreeEntryName = "tree.json";
        private const string MetaEntryName = "meta.json";
        private const string FolderFlagsEntryName = "folderflags.json";

        public void Export(ScanResult result, string rootPath, string outputPath)
        {
            if (result == null) throw new ArgumentNullException("result");
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path required", "outputPath");
            var dataPath = PathResolver.ToExtendedPath(result.TempDataPath);
            if (string.IsNullOrWhiteSpace(result.TempDataPath) || !File.Exists(dataPath))
            {
                throw new FileNotFoundException("Scan data file not found.", result.TempDataPath);
            }

            var outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }
            if (File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            using (var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create))
            {
                AddFileEntry(archive, DataEntryName, result.TempDataPath);
                if (!AddFileEntry(archive, ErrorsEntryName, result.ErrorPath))
                {
                    AddEmptyEntry(archive, ErrorsEntryName);
                }
                AddJsonEntry(archive, TreeEntryName, result.TreeMap ?? new Dictionary<string, List<string>>());
                AddJsonEntry(archive, FolderFlagsEntryName, BuildFolderFlags(result.Details ?? new Dictionary<string, FolderDetail>()));
                AddJsonEntry(archive, MetaEntryName, new ArchiveMeta
                {
                    RootPath = rootPath,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        public AnalysisArchiveResult Import(string archivePath)
        {
            if (string.IsNullOrWhiteSpace(archivePath)) throw new ArgumentException("Archive path required", "archivePath");
            var tempDir = Path.Combine(Path.GetTempPath(), "NtfsAudit", "imports", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                using (var archive = ZipFile.OpenRead(archivePath))
                {
                    ExtractEntry(archive, DataEntryName, tempDir);
                    ExtractEntry(archive, ErrorsEntryName, tempDir);
                    ExtractEntry(archive, TreeEntryName, tempDir);
                    ExtractEntry(archive, FolderFlagsEntryName, tempDir);
                    ExtractEntry(archive, MetaEntryName, tempDir);
                }

                var dataPath = Path.Combine(tempDir, DataEntryName);
                var errorPath = Path.Combine(tempDir, ErrorsEntryName);
                var treePath = Path.Combine(tempDir, TreeEntryName);
                var folderFlagsPath = Path.Combine(tempDir, FolderFlagsEntryName);
                var metaPath = Path.Combine(tempDir, MetaEntryName);
                if (!File.Exists(dataPath))
                {
                    throw new InvalidDataException("Archivio analisi non valido: dati mancanti.");
                }
                if (!File.Exists(errorPath))
                {
                    File.WriteAllText(errorPath, string.Empty);
                }

                var treeMap = LoadTreeMap(treePath, dataPath);
                var meta = LoadMeta(metaPath);

                var details = BuildDetailsFromExport(dataPath);
                ApplyFolderFlags(details, LoadFolderFlags(folderFlagsPath));

                var result = new ScanResult
                {
                    TempDataPath = dataPath,
                    ErrorPath = errorPath,
                    Details = details,
                    TreeMap = treeMap
                };

                return new AnalysisArchiveResult
                {
                    ScanResult = result,
                    RootPath = meta.RootPath
                };
            }
            catch
            {
                SafeDeleteDirectory(tempDir);
                throw;
            }
        }

        private Dictionary<string, List<string>> LoadTreeMap(string treePath, string dataPath)
        {
            if (File.Exists(treePath))
            {
                try
                {
                    var tree = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(File.ReadAllText(treePath));
                    if (tree != null && tree.Count > 0)
                    {
                        return new Dictionary<string, List<string>>(tree, StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch
                {
                }
            }

            return BuildTreeFromExport(dataPath);
        }

        private ArchiveMeta LoadMeta(string metaPath)
        {
            if (!File.Exists(metaPath))
            {
                return new ArchiveMeta();
            }

            try
            {
                return JsonConvert.DeserializeObject<ArchiveMeta>(File.ReadAllText(metaPath)) ?? new ArchiveMeta();
            }
            catch
            {
                return new ArchiveMeta();
            }
        }

        private Dictionary<string, FolderDetail> BuildDetailsFromExport(string dataPath)
        {
            var details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(dataPath)) return details;

            var dedupe = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(dataPath))
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
                if (string.IsNullOrWhiteSpace(record.FolderPath)) continue;
                if (string.Equals(record.PrincipalType, "Meta", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(record.PrincipalName, "SCAN_OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                FolderDetail detail;
                if (!details.TryGetValue(record.FolderPath, out detail))
                {
                    detail = new FolderDetail();
                    details[record.FolderPath] = detail;
                }

                var entryKey = BuildEntryKey(record);
                HashSet<string> folderKeys;
                if (!dedupe.TryGetValue(record.FolderPath, out folderKeys))
                {
                    folderKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    dedupe[record.FolderPath] = folderKeys;
                }
                if (!folderKeys.Add(entryKey))
                {
                    continue;
                }

                var entry = new AceEntry
                {
                    FolderPath = record.FolderPath,
                    PrincipalName = record.PrincipalName,
                    PrincipalSid = record.PrincipalSid,
                    PrincipalType = record.PrincipalType,
                    AllowDeny = record.AllowDeny,
                    RightsSummary = record.RightsSummary,
                    RightsMask = record.RightsMask,
                    EffectiveRightsSummary = record.EffectiveRightsSummary,
                    EffectiveRightsMask = record.EffectiveRightsMask,
                    IsInherited = record.IsInherited,
                    InheritanceFlags = record.InheritanceFlags,
                    PropagationFlags = record.PropagationFlags,
                    Source = record.Source,
                    Depth = record.Depth,
                    ResourceType = record.ResourceType,
                    TargetPath = record.TargetPath,
                    Owner = record.Owner,
                    AuditSummary = record.AuditSummary,
                    RiskLevel = record.RiskLevel,
                    IsDisabled = record.IsDisabled,
                    IsServiceAccount = SidClassifier.IsServiceAccountSid(record.PrincipalSid),
                    IsAdminAccount = SidClassifier.IsPrivilegedGroupSid(record.PrincipalSid),
                    HasExplicitPermissions = record.HasExplicitPermissions,
                    IsInheritanceDisabled = record.IsInheritanceDisabled,
                    MemberNames = record.MemberNames == null ? null : new List<string>(record.MemberNames)
                };

                detail.AllEntries.Add(entry);
                if (entry.HasExplicitPermissions || !entry.IsInherited)
                {
                    detail.HasExplicitPermissions = true;
                }
                if (entry.IsInheritanceDisabled)
                {
                    detail.IsInheritanceDisabled = true;
                }
                if (string.Equals(record.PrincipalType, "Group", StringComparison.OrdinalIgnoreCase))
                {
                    detail.GroupEntries.Add(entry);
                }
                else
                {
                    detail.UserEntries.Add(entry);
                }
            }

            return details;
        }

        private Dictionary<string, FolderFlagsPayload> BuildFolderFlags(Dictionary<string, FolderDetail> details)
        {
            var payload = new Dictionary<string, FolderFlagsPayload>(StringComparer.OrdinalIgnoreCase);
            if (details == null) return payload;
            foreach (var entry in details)
            {
                payload[entry.Key] = new FolderFlagsPayload
                {
                    HasExplicitPermissions = entry.Value.HasExplicitPermissions,
                    IsInheritanceDisabled = entry.Value.IsInheritanceDisabled
                };
            }
            return payload;
        }

        private Dictionary<string, FolderFlagsPayload> LoadFolderFlags(string flagsPath)
        {
            if (!File.Exists(flagsPath)) return new Dictionary<string, FolderFlagsPayload>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var flags = JsonConvert.DeserializeObject<Dictionary<string, FolderFlagsPayload>>(File.ReadAllText(flagsPath));
                return flags == null
                    ? new Dictionary<string, FolderFlagsPayload>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, FolderFlagsPayload>(flags, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, FolderFlagsPayload>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void ApplyFolderFlags(Dictionary<string, FolderDetail> details, Dictionary<string, FolderFlagsPayload> flags)
        {
            if (flags == null || flags.Count == 0) return;
            foreach (var entry in flags)
            {
                if (string.IsNullOrWhiteSpace(entry.Key)) continue;
                FolderDetail detail;
                if (!details.TryGetValue(entry.Key, out detail))
                {
                    detail = new FolderDetail();
                    details[entry.Key] = detail;
                }
                detail.HasExplicitPermissions = detail.HasExplicitPermissions || entry.Value.HasExplicitPermissions;
                detail.IsInheritanceDisabled = detail.IsInheritanceDisabled || entry.Value.IsInheritanceDisabled;
            }
        }

        private string BuildEntryKey(ExportRecord record)
        {
            var principalKey = string.IsNullOrWhiteSpace(record.PrincipalSid) ? record.PrincipalName : record.PrincipalSid;
            var membersKey = record.MemberNames == null ? string.Empty : string.Join(",", record.MemberNames);
            return string.Format("{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9}|{10}|{11}|{12}|{13}|{14}|{15}|{16}|{17}|{18}|{19}|{20}|{21}|{22}",
                principalKey ?? string.Empty,
                record.PrincipalType ?? string.Empty,
                record.AllowDeny ?? string.Empty,
                record.RightsSummary ?? string.Empty,
                record.RightsMask,
                record.EffectiveRightsSummary ?? string.Empty,
                record.EffectiveRightsMask,
                record.IsInherited,
                record.InheritanceFlags ?? string.Empty,
                record.PropagationFlags ?? string.Empty,
                record.Source ?? string.Empty,
                record.Depth,
                record.ResourceType ?? string.Empty,
                record.TargetPath ?? string.Empty,
                record.Owner ?? string.Empty,
                record.AuditSummary ?? string.Empty,
                record.RiskLevel ?? string.Empty,
                record.IsDisabled,
                record.IsServiceAccount,
                record.IsAdminAccount,
                membersKey,
                record.HasExplicitPermissions,
                record.IsInheritanceDisabled);
        }

        private Dictionary<string, List<string>> BuildTreeFromExport(string dataPath)
        {
            var treeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(dataPath)) return treeMap;

            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(dataPath))
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
                if (record == null || string.IsNullOrWhiteSpace(record.FolderPath)) continue;
                if (string.Equals(record.PrincipalType, "Meta", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(record.PrincipalName, "SCAN_OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                folders.Add(record.FolderPath);
            }

            var parentCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var toProcess = folders.ToList();
            foreach (var folder in toProcess)
            {
                var parent = SafeGetParent(folder, parentCache);
                while (!string.IsNullOrWhiteSpace(parent))
                {
                    if (!folders.Add(parent))
                    {
                        break;
                    }
                    parent = SafeGetParent(parent, parentCache);
                }
            }

            var treeSets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in folders)
            {
                if (!treeSets.ContainsKey(folder))
                {
                    treeSets[folder] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
            }

            foreach (var folder in folders)
            {
                var parent = SafeGetParent(folder, parentCache);
                if (parent != null)
                {
                    HashSet<string> children;
                    if (!treeSets.TryGetValue(parent, out children))
                    {
                        children = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        treeSets[parent] = children;
                    }
                    children.Add(folder);
                }
            }

            foreach (var entry in treeSets)
            {
                treeMap[entry.Key] = entry.Value.ToList();
            }

            return treeMap;
        }

        private string SafeGetParent(string path, Dictionary<string, string> cache)
        {
            if (cache != null && cache.TryGetValue(path, out var cachedParent))
            {
                return cachedParent;
            }

            string parentValue;
            try
            {
                var parent = Directory.GetParent(path);
                parentValue = parent == null ? null : parent.FullName;
            }
            catch
            {
                parentValue = null;
            }

            if (cache != null)
            {
                cache[path] = parentValue;
            }

            return parentValue;
        }

        private bool AddFileEntry(ZipArchive archive, string entryName, string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) return false;
            var ioPath = PathResolver.ToExtendedPath(sourcePath);
            if (!File.Exists(ioPath)) return false;
            archive.CreateEntryFromFile(ioPath, entryName);
            return true;
        }

        private void AddJsonEntry(ZipArchive archive, string entryName, object data)
        {
            var entry = archive.CreateEntry(entryName);
            using (var stream = entry.Open())
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(JsonConvert.SerializeObject(data));
            }
        }

        private void ExtractEntry(ZipArchive archive, string entryName, string destinationDir)
        {
            var entry = archive.GetEntry(entryName);
            if (entry == null) return;
            var destinationPath = Path.Combine(destinationDir, entryName);
            entry.ExtractToFile(destinationPath, true);
        }

        private void AddEmptyEntry(ZipArchive archive, string entryName)
        {
            var entry = archive.CreateEntry(entryName);
            using (var stream = entry.Open())
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(string.Empty);
            }
        }

        private void SafeDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
            }
        }

        private class ArchiveMeta
        {
            public string RootPath { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        private class FolderFlagsPayload
        {
            public bool HasExplicitPermissions { get; set; }
            public bool IsInheritanceDisabled { get; set; }
        }
    }

    public class AnalysisArchiveResult
    {
        public ScanResult ScanResult { get; set; }
        public string RootPath { get; set; }
    }
}
