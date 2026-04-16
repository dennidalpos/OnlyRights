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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NtfsAudit.App.Export;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public partial class AnalysisArchive
    {
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
                var folderPath = NormalizeExportPath(record.FolderPath);
                if (string.IsNullOrWhiteSpace(folderPath)) continue;
                if (string.Equals(record.PrincipalType, "Meta", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(record.PrincipalName, "SCAN_OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                FolderDetail detail;
                if (!details.TryGetValue(folderPath, out detail))
                {
                    detail = new FolderDetail();
                    details[folderPath] = detail;
                }

                var entryKey = BuildEntryKey(record);
                HashSet<string> folderKeys;
                if (!dedupe.TryGetValue(folderPath, out folderKeys))
                {
                    folderKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    dedupe[folderPath] = folderKeys;
                }
                if (!folderKeys.Add(entryKey))
                {
                    continue;
                }

                var entry = new AceEntry
                {
                    FolderPath = folderPath,
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
                    PathKind = record.PathKind,
                    Depth = record.Depth,
                    ResourceType = record.ResourceType,
                    TargetPath = NormalizeExportPath(record.TargetPath),
                    Owner = record.Owner,
                    ShareName = record.ShareName,
                    ShareServer = record.ShareServer,
                    AuditSummary = record.AuditSummary,
                    RiskLevel = record.RiskLevel,
                    IsDisabled = record.IsDisabled,
                    IsServiceAccount = record.IsServiceAccount || SidClassifier.IsServiceAccountSid(record.PrincipalSid),
                    IsAdminAccount = record.IsAdminAccount || SidClassifier.IsPrivilegedGroupSid(record.PrincipalSid),
                    HasExplicitPermissions = record.HasExplicitPermissions,
                    IsInheritanceDisabled = record.IsInheritanceDisabled,
                    MemberNames = record.MemberNames == null ? null : new List<string>(record.MemberNames)
                };

                if (entry.PermissionLayer == PermissionLayer.Share)
                {
                    detail.ShareEntries.Add(entry);
                    detail.HasExplicitShare = true;
                    detail.HasShareEntries = true;
                }
                else if (entry.PermissionLayer == PermissionLayer.Effective)
                {
                    detail.EffectiveEntries.Add(entry);
                    detail.HasEffectiveEntries = true;
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

                detail.HasFileEntries = detail.HasFileEntries || string.Equals(entry.ResourceType, "File", StringComparison.OrdinalIgnoreCase);
                detail.HasFolderEntries = detail.HasFolderEntries || !string.Equals(entry.ResourceType, "File", StringComparison.OrdinalIgnoreCase);
                detail.HasHighRiskEntries = detail.HasHighRiskEntries || string.Equals(entry.RiskLevel, "Alto", StringComparison.OrdinalIgnoreCase);
                detail.HasMediumRiskEntries = detail.HasMediumRiskEntries || string.Equals(entry.RiskLevel, "Medio", StringComparison.OrdinalIgnoreCase);
                detail.HasLowRiskEntries = detail.HasLowRiskEntries || string.Equals(entry.RiskLevel, "Basso", StringComparison.OrdinalIgnoreCase);
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
                    HasFileEntries = entry.Value.HasFileEntries,
                    HasFolderEntries = entry.Value.HasFolderEntries,
                    HasHighRiskEntries = entry.Value.HasHighRiskEntries,
                    HasMediumRiskEntries = entry.Value.HasMediumRiskEntries,
                    HasLowRiskEntries = entry.Value.HasLowRiskEntries,
                    HasShareEntries = entry.Value.HasShareEntries,
                    HasEffectiveEntries = entry.Value.HasEffectiveEntries,
                    DiffSummary = entry.Value.DiffSummary,
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
                detail.HasFileEntries = detail.HasFileEntries || entry.Value.HasFileEntries;
                detail.HasFolderEntries = detail.HasFolderEntries || entry.Value.HasFolderEntries;
                detail.HasHighRiskEntries = detail.HasHighRiskEntries || entry.Value.HasHighRiskEntries;
                detail.HasMediumRiskEntries = detail.HasMediumRiskEntries || entry.Value.HasMediumRiskEntries;
                detail.HasLowRiskEntries = detail.HasLowRiskEntries || entry.Value.HasLowRiskEntries;
                detail.HasShareEntries = detail.HasShareEntries || entry.Value.HasShareEntries;
                detail.HasEffectiveEntries = detail.HasEffectiveEntries || entry.Value.HasEffectiveEntries;
                if (entry.Value.DiffSummary != null)
                {
                    detail.DiffSummary = entry.Value.DiffSummary;
                }
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
            return string.Format("{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9}|{10}|{11}|{12}|{13}|{14}|{15}|{16}|{17}|{18}|{19}|{20}|{21}|{22}|{23}|{24}|{25}|{26}|{27}|{28}|{29}|{30}|{31}",
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
                NormalizeExportPath(record.TargetPath) ?? string.Empty,
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
                record.IsInheritanceDisabled,
                record.PathKind);
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
                var folderPath = NormalizeExportPath(record.FolderPath);
                if (string.IsNullOrWhiteSpace(folderPath)) continue;
                if (string.Equals(record.PrincipalType, "Meta", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(record.PrincipalName, "SCAN_OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!IsWithinRoot(NormalizeTreePath(folderPath), normalizedRoot))
                {
                    continue;
                }
                folders.Add(folderPath);
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

        private static DateTime NormalizeImportedTimestamp(DateTime importedTimestamp, string archivePath)
        {
            if (importedTimestamp != default(DateTime))
            {
                return importedTimestamp.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(importedTimestamp, DateTimeKind.Utc)
                    : importedTimestamp.ToUniversalTime();
            }

            try
            {
                var fallback = File.GetLastWriteTimeUtc(archivePath);
                return fallback == default(DateTime) ? DateTime.UtcNow : fallback;
            }
            catch
            {
                return DateTime.UtcNow;
            }
        }

        private static string NormalizeExportPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            return PathResolver.FromExtendedPath(path).Replace('/', '\\').Trim();
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

        private Dictionary<string, FolderDetail> BuildDetailsFromFolderFlags(Dictionary<string, List<string>> treeMap, Dictionary<string, FolderFlagsPayload> flags)
        {
            var details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase);
            if (treeMap != null)
            {
                foreach (var path in treeMap.Keys.Where(key => !string.IsNullOrWhiteSpace(key)))
                {
                    details[path] = new FolderDetail { EntriesLoaded = false };
                }
            }

            ApplyFolderFlags(details, flags);
            return details;
        }

        private class ArchiveMeta
        {
            public string RootPath { get; set; }
            public string RootPathKind { get; set; }
            public DateTime CreatedAt { get; set; }
            public int Version { get; set; }
            public ScanOptions ScanOptions { get; set; }
            public int DataRecordCount { get; set; }
            public int ErrorRecordCount { get; set; }
        }

        private class FolderFlagsPayload
        {
            public bool HasExplicitPermissions { get; set; }
            public bool HasExplicitNtfs { get; set; }
            public bool HasExplicitShare { get; set; }
            public bool IsInheritanceDisabled { get; set; }
            public bool HasFileEntries { get; set; }
            public bool HasFolderEntries { get; set; }
            public bool HasHighRiskEntries { get; set; }
            public bool HasMediumRiskEntries { get; set; }
            public bool HasLowRiskEntries { get; set; }
            public bool HasShareEntries { get; set; }
            public bool HasEffectiveEntries { get; set; }
            public AclDiffSummary DiffSummary { get; set; }
            public List<AclDiffKey> BaselineAdded { get; set; }
            public List<AclDiffKey> BaselineRemoved { get; set; }
        }
    }
}
