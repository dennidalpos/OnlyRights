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
        private const int CurrentArchiveVersion = 2;
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

            var ioOutputPath = PathResolver.ToExtendedPath(outputPath);
            var outputDirectory = Path.GetDirectoryName(ioOutputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }
            if (File.Exists(ioOutputPath))
            {
                File.Delete(ioOutputPath);
            }

            using (var archive = ZipFile.Open(ioOutputPath, ZipArchiveMode.Create))
            {
                AddFileEntry(archive, DataEntryName, result.TempDataPath);
                if (!AddFileEntry(archive, ErrorsEntryName, result.ErrorPath))
                {
                    AddEmptyEntry(archive, ErrorsEntryName);
                }
                AddJsonEntry(archive, TreeEntryName, result.TreeMap);
                AddJsonEntry(archive, FolderFlagsEntryName, BuildFolderFlags(result.Details));
                AddJsonEntry(archive, MetaEntryName, new ArchiveMeta
                {
                    RootPath = rootPath,
                    CreatedAt = DateTime.UtcNow,
                    Version = CurrentArchiveVersion
                });
            }
        }

        public AnalysisArchiveResult Import(string archivePath)
        {
            if (string.IsNullOrWhiteSpace(archivePath)) throw new ArgumentException("Archive path required", "archivePath");
            var ioArchivePath = PathResolver.ToExtendedPath(archivePath);
            var tempDir = Path.Combine(Path.GetTempPath(), "NtfsAudit", "imports", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            using (var archive = ZipFile.OpenRead(ioArchivePath))
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

            var meta = LoadMeta(metaPath);
            var treeMap = LoadTreeMap(treePath, dataPath, meta.RootPath);

            var details = BuildDetailsFromExport(dataPath);
            ApplyFolderFlags(details, LoadFolderFlags(folderFlagsPath));
            var scanOptions = LoadScanOptions(dataPath);

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
                RootPath = meta.RootPath,
                ScanOptions = scanOptions
            };
        }

        private Dictionary<string, List<string>> LoadTreeMap(string treePath, string dataPath, string rootPath)
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

            return BuildTreeFromExport(dataPath, rootPath);
        }

        private ArchiveMeta LoadMeta(string metaPath)
        {
            if (!File.Exists(metaPath))
            {
                return new ArchiveMeta { Version = 1 };
            }

            try
            {
                var meta = JsonConvert.DeserializeObject<ArchiveMeta>(File.ReadAllText(metaPath)) ?? new ArchiveMeta();
                if (meta.Version <= 0)
                {
                    meta.Version = 1;
                }
                return meta;
            }
            catch
            {
                return new ArchiveMeta { Version = 1 };
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
                    PermissionLayer = record.PermissionLayer,
                    AllowDeny = record.AllowDeny,
                    RightsSummary = record.RightsSummary,
                    RightsMask = record.RightsMask,
                    EffectiveRightsSummary = record.EffectiveRightsSummary,
                    EffectiveRightsMask = record.EffectiveRightsMask,
                    ShareRightsMask = record.ShareRightsMask,
                    NtfsRightsMask = record.NtfsRightsMask,
                    IsInherited = record.IsInherited,
                    AppliesToThisFolder = record.AppliesToThisFolder,
                    AppliesToSubfolders = record.AppliesToSubfolders,
                    AppliesToFiles = record.AppliesToFiles,
                    InheritanceFlags = record.InheritanceFlags,
                    PropagationFlags = record.PropagationFlags,
                    Source = record.Source,
                    Depth = record.Depth,
                    ResourceType = record.ResourceType,
                    TargetPath = record.TargetPath,
                    Owner = record.Owner,
                    ShareName = record.ShareName,
                    ShareServer = record.ShareServer,
                    AuditSummary = record.AuditSummary,
                    RiskLevel = record.RiskLevel,
                    IsDisabled = record.IsDisabled,
                    IsServiceAccount = SidClassifier.IsServiceAccountSid(record.PrincipalSid),
                    IsAdminAccount = SidClassifier.IsPrivilegedGroupSid(record.PrincipalSid),
                    HasExplicitPermissions = record.HasExplicitPermissions,
                    IsInheritanceDisabled = record.IsInheritanceDisabled,
                    MemberNames = record.MemberNames == null ? null : new List<string>(record.MemberNames)
                };

                if (entry.PermissionLayer == PermissionLayer.Share)
                {
                    detail.ShareEntries.Add(entry);
                    detail.HasExplicitShare = true;
                }
                else if (entry.PermissionLayer == PermissionLayer.Effective)
                {
                    detail.EffectiveEntries.Add(entry);
                }
                else
                {
                    detail.AllEntries.Add(entry);
                    detail.HasExplicitNtfs = detail.HasExplicitNtfs || entry.HasExplicitPermissions || !entry.IsInherited;
                    if (entry.HasExplicitPermissions || !entry.IsInherited)
                    {
                        detail.HasExplicitPermissions = true;
                    }
                }
                if (entry.IsInheritanceDisabled)
                {
                    detail.IsInheritanceDisabled = true;
                }
                if (entry.PermissionLayer == PermissionLayer.Ntfs
                    && string.Equals(record.PrincipalType, "Group", StringComparison.OrdinalIgnoreCase))
                {
                    detail.GroupEntries.Add(entry);
                }
                else if (entry.PermissionLayer == PermissionLayer.Ntfs)
                {
                    detail.UserEntries.Add(entry);
                }
            }

            return details;
        }

        private ScanOptions LoadScanOptions(string dataPath)
        {
            if (!File.Exists(dataPath)) return null;
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
                if (!string.Equals(record.PrincipalType, "Meta", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(record.PrincipalName, "SCAN_OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return new ScanOptions
                {
                    RootPath = record.FolderPath,
                    IncludeInherited = record.IncludeInherited,
                    ResolveIdentities = record.ResolveIdentities,
                    ExcludeServiceAccounts = record.ExcludeServiceAccounts,
                    ExcludeAdminAccounts = record.ExcludeAdminAccounts,
                    EnableAdvancedAudit = record.EnableAdvancedAudit,
                    ComputeEffectiveAccess = record.ComputeEffectiveAccess,
                    IncludeSharePermissions = record.IncludeSharePermissions,
                    IncludeFiles = record.IncludeFiles,
                    ReadOwnerAndSacl = record.ReadOwnerAndSacl,
                    CompareBaseline = record.CompareBaseline,
                    ScanAllDepths = record.ScanAllDepths,
                    MaxDepth = record.MaxDepth,
                    ExpandGroups = record.ExpandGroups,
                    UsePowerShell = record.UsePowerShell
                };
            }

            return null;
        }

        private Dictionary<string, FolderFlagsPayload> BuildFolderFlags(Dictionary<string, FolderDetail> details)
        {
            var payload = new Dictionary<string, FolderFlagsPayload>(StringComparer.OrdinalIgnoreCase);
            if (details == null) return payload;
            foreach (var entry in details)
            {
                var baselineAdded = entry.Value.BaselineSummary == null ? new List<AclDiffKey>() : entry.Value.BaselineSummary.Added;
                var baselineRemoved = entry.Value.BaselineSummary == null ? new List<AclDiffKey>() : entry.Value.BaselineSummary.Removed;
                payload[entry.Key] = new FolderFlagsPayload
                {
                    HasExplicitPermissions = entry.Value.HasExplicitPermissions,
                    HasExplicitNtfs = entry.Value.HasExplicitNtfs,
                    HasExplicitShare = entry.Value.HasExplicitShare,
                    IsInheritanceDisabled = entry.Value.IsInheritanceDisabled,
                    BaselineAdded = baselineAdded,
                    BaselineRemoved = baselineRemoved
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
                detail.HasExplicitNtfs = detail.HasExplicitNtfs || entry.Value.HasExplicitNtfs;
                detail.HasExplicitShare = detail.HasExplicitShare || entry.Value.HasExplicitShare;
                detail.IsInheritanceDisabled = detail.IsInheritanceDisabled || entry.Value.IsInheritanceDisabled;
                if ((entry.Value.BaselineAdded != null && entry.Value.BaselineAdded.Count > 0) ||
                    (entry.Value.BaselineRemoved != null && entry.Value.BaselineRemoved.Count > 0))
                {
                    detail.BaselineSummary = new AclDiffSummary();
                    detail.BaselineSummary.Added.AddRange(entry.Value.BaselineAdded ?? new List<AclDiffKey>());
                    detail.BaselineSummary.Removed.AddRange(entry.Value.BaselineRemoved ?? new List<AclDiffKey>());
                }
            }
        }

        private string BuildEntryKey(ExportRecord record)
        {
            var principalKey = string.IsNullOrWhiteSpace(record.PrincipalSid) ? record.PrincipalName : record.PrincipalSid;
            var membersKey = record.MemberNames == null ? string.Empty : string.Join(",", record.MemberNames);
            return string.Format("{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9}|{10}|{11}|{12}|{13}|{14}|{15}|{16}|{17}|{18}|{19}|{20}|{21}|{22}|{23}|{24}|{25}|{26}|{27}|{28}",
                principalKey ?? string.Empty,
                record.PrincipalType ?? string.Empty,
                record.PermissionLayer.ToString(),
                record.AllowDeny ?? string.Empty,
                record.RightsSummary ?? string.Empty,
                record.RightsMask,
                record.EffectiveRightsSummary ?? string.Empty,
                record.EffectiveRightsMask,
                record.ShareRightsMask,
                record.NtfsRightsMask,
                record.IsInherited,
                record.AppliesToThisFolder,
                record.AppliesToSubfolders,
                record.AppliesToFiles,
                record.InheritanceFlags ?? string.Empty,
                record.PropagationFlags ?? string.Empty,
                record.Source ?? string.Empty,
                record.Depth,
                record.ResourceType ?? string.Empty,
                record.TargetPath ?? string.Empty,
                record.Owner ?? string.Empty,
                record.ShareName ?? string.Empty,
                record.ShareServer ?? string.Empty,
                record.AuditSummary ?? string.Empty,
                record.RiskLevel ?? string.Empty,
                record.IsDisabled,
                record.IsServiceAccount,
                record.IsAdminAccount,
                membersKey,
                record.HasExplicitPermissions,
                record.IsInheritanceDisabled);
        }

        private Dictionary<string, List<string>> BuildTreeFromExport(string dataPath, string rootPath)
        {
            var treeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(dataPath)) return treeMap;

            var normalizedRoot = NormalizeTreePath(rootPath);
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
                if (!IsWithinRoot(NormalizeTreePath(record.FolderPath), normalizedRoot))
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
                    var normalizedParent = NormalizeTreePath(parent);
                    if (!IsWithinRoot(normalizedParent, normalizedRoot))
                    {
                        break;
                    }
                    if (!folders.Add(parent))
                    {
                        break;
                    }
                    if (!string.IsNullOrWhiteSpace(normalizedRoot)
                        && string.Equals(normalizedParent, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                    parent = SafeGetParent(parent, parentCache);
                }
            }

            if (!string.IsNullOrWhiteSpace(rootPath))
            {
                folders.Add(rootPath);
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
                    var normalizedParent = NormalizeTreePath(parent);
                    if (!IsWithinRoot(normalizedParent, normalizedRoot))
                    {
                        continue;
                    }
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

        private static string NormalizeTreePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            var normalized = PathResolver.FromExtendedPath(path);
            return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsWithinRoot(string candidate, string root)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }
            if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;
            return candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
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
            var ioDestinationPath = PathResolver.ToExtendedPath(destinationPath);
            entry.ExtractToFile(ioDestinationPath, true);
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

        private class ArchiveMeta
        {
            public string RootPath { get; set; }
            public DateTime CreatedAt { get; set; }
            public int Version { get; set; }
        }

        private class FolderFlagsPayload
        {
            public bool HasExplicitPermissions { get; set; }
            public bool HasExplicitNtfs { get; set; }
            public bool HasExplicitShare { get; set; }
            public bool IsInheritanceDisabled { get; set; }
            public List<AclDiffKey> BaselineAdded { get; set; }
            public List<AclDiffKey> BaselineRemoved { get; set; }
        }
    }

    public class AnalysisArchiveResult
    {
        public ScanResult ScanResult { get; set; }
        public string RootPath { get; set; }
        public ScanOptions ScanOptions { get; set; }
    }
}
