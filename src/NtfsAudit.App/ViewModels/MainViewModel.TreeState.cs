using NtfsAudit.Core.Logging;
using NtfsAudit.Core.Export;
using NtfsAudit.Core.Cache;
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
using System.Diagnostics;
using System.IO;
using System.Linq;
using NtfsAudit.Core.Models;
using NtfsAudit.App.Services;
using NtfsAudit.Core.Services;

namespace NtfsAudit.App.ViewModels
{
    public partial class MainViewModel
    {
        private void UpdateProgress(ScanProgress progress)
        {
            CurrentPathText = string.IsNullOrWhiteSpace(progress.CurrentPath) ? string.Empty : progress.CurrentPath;
            ProcessedCount = progress.Processed;
            ProcessedFilesCount = progress.FilesProcessed;
            ElapsedText = FormatElapsed(progress.Elapsed);
            ErrorCount = progress.Errors;
            if (string.Equals(progress.Stage, "Errore", StringComparison.OrdinalIgnoreCase)
                || string.Equals(progress.Stage, "Error", StringComparison.OrdinalIgnoreCase))
            {
                CurrentPathBackground = "#FFFFCDD2";
            }
            else if (!string.IsNullOrWhiteSpace(progress.CurrentPath))
            {
                CurrentPathBackground = "#FFC8E6C9";
            }
            else
            {
                CurrentPathBackground = "Transparent";
            }
        }

        private void LoadTree(ScanResult result)
        {
            FolderTree.Clear();
            _currentFilteredTreeMap = null;
            if (result == null)
            {
                return;
            }

            if (_fullTreeMap == null || _fullTreeMap.Count == 0)
            {
                _fullTreeMap = ResolveFullTreeMap(result);
            }

            var treeMap = _fullTreeMap;
            if (treeMap == null || treeMap.Count == 0)
            {
                return;
            }

            var preferredRoot = ResolveTreeRoot(treeMap, RootPath);
            var filteredTreeMap = ApplyTreeFilters(treeMap, result.Details, preferredRoot);
            _currentFilteredTreeMap = filteredTreeMap;
            var provider = new FolderTreeProvider(filteredTreeMap, result.Details);
            var roots = ResolveTreeRoots(filteredTreeMap, preferredRoot);
            if (roots.Count == 0) return;

            foreach (var rootPath in roots)
            {
                var rootName = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(rootName)) rootName = rootPath;
                var rootDetail = result.Details != null && result.Details.TryGetValue(rootPath, out var detail) ? detail : null;
                var rootSummary = rootDetail == null ? null : rootDetail.DiffSummary;
                var rootNode = new FolderNodeViewModel(
                    rootPath,
                    rootName,
                    rootPath,
                    provider,
                    false,
                    rootDetail != null && rootDetail.HasFileEntries,
                    rootDetail != null && rootDetail.HasExplicitPermissions,
                    rootDetail != null && rootDetail.IsInheritanceDisabled,
                    rootSummary == null ? 0 : rootSummary.Added.Count(key => !key.IsInherited),
                    rootSummary == null ? 0 : rootSummary.Removed.Count,
                    rootSummary == null ? 0 : rootSummary.DenyExplicitCount,
                    rootSummary != null && rootSummary.IsProtected,
                    rootDetail == null || rootDetail.BaselineSummary == null ? 0 : rootDetail.BaselineSummary.Added.Count,
                    rootDetail == null || rootDetail.BaselineSummary == null ? 0 : rootDetail.BaselineSummary.Removed.Count,
                    rootDetail != null && rootDetail.HasExplicitNtfs,
                    rootDetail != null && rootDetail.HasExplicitShare,
                    rootDetail != null && rootDetail.HasHighRiskEntries,
                    rootDetail != null && rootDetail.HasMediumRiskEntries,
                    rootDetail != null && rootDetail.HasLowRiskEntries);
                rootNode.IsExpanded = true;
                if (FolderTree.Count == 0)
                {
                    rootNode.IsSelected = true;
                }
                FolderTree.Add(rootNode);
            }
        }

        private void ReloadTreeWithFilters()
        {
            if (_scanResult == null || _fullTreeMap == null || _fullTreeMap.Count == 0) return;
            var preferredPath = SelectedFolderPath;
            var preferredRoot = ResolveTreeRoot(_fullTreeMap, _scanResult.RootPath);
            if (!string.IsNullOrWhiteSpace(preferredRoot)
                && !string.Equals(RootPath, preferredRoot, StringComparison.OrdinalIgnoreCase))
            {
                RootPath = preferredRoot;
            }
            LoadTree(_scanResult);
            if (_currentFilteredTreeMap == null || _currentFilteredTreeMap.Count == 0)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(preferredPath) && _currentFilteredTreeMap.ContainsKey(preferredPath))
            {
                SelectFolder(preferredPath);
                return;
            }

            var visibleRoot = ResolveTreeRoot(_currentFilteredTreeMap, RootPath);
            if (!string.IsNullOrWhiteSpace(visibleRoot))
            {
                SelectFolder(visibleRoot);
            }
        }

        private Dictionary<string, List<string>> ApplyTreeFilters(Dictionary<string, List<string>> treeMap, Dictionary<string, FolderDetail> details, string rootPath)
        {
            if (treeMap == null || treeMap.Count == 0) return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var filtered = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var roots = ResolveTreeRoots(treeMap, rootPath);
            if (roots.Count == 0)
            {
                return filtered;
            }

            bool IncludeNode(string node, string root)
            {
                if (string.IsNullOrWhiteSpace(node)) return false;
                var direct = NodeMatchesTreeFilters(node, details);
                if (!treeMap.TryGetValue(node, out var children) || children == null || children.Count == 0)
                {
                    if (direct) filtered[node] = new List<string>();
                    return direct;
                }

                var includedChildren = new List<string>();
                foreach (var child in children)
                {
                    if (IncludeNode(child, root)) includedChildren.Add(child);
                }
                if (direct || includedChildren.Count > 0 || string.Equals(node, root, StringComparison.OrdinalIgnoreCase))
                {
                    filtered[node] = includedChildren;
                    return true;
                }
                return false;
            }

            foreach (var root in roots)
            {
                IncludeNode(root, root);
            }

            AddVisibleFileLeaves(filtered, details);
            return filtered;
        }

        private void AddVisibleFileLeaves(Dictionary<string, List<string>> treeMap, Dictionary<string, FolderDetail> details)
        {
            if (!TreeFilterFilesOnly || treeMap == null || treeMap.Count == 0 || details == null || details.Count == 0)
            {
                return;
            }

            foreach (var folderPath in treeMap.Keys.ToList())
            {
                FolderDetail detail;
                if (!details.TryGetValue(folderPath, out detail) || detail == null || detail.AllEntries == null)
                {
                    continue;
                }

                var filePaths = detail.AllEntries
                    .Where(entry => IsFileResourceType(entry.ResourceType) && !string.IsNullOrWhiteSpace(entry.TargetPath))
                    .Select(entry => NormalizeTreePath(entry.TargetPath))
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (filePaths.Count == 0)
                {
                    continue;
                }

                List<string> children;
                if (!treeMap.TryGetValue(folderPath, out children) || children == null)
                {
                    children = new List<string>();
                    treeMap[folderPath] = children;
                }

                foreach (var filePath in filePaths)
                {
                    if (!children.Contains(filePath, StringComparer.OrdinalIgnoreCase))
                    {
                        children.Add(filePath);
                    }

                    if (!treeMap.ContainsKey(filePath))
                    {
                        treeMap[filePath] = new List<string>();
                    }
                }
            }
        }

        private List<string> ResolveTreeRoots(Dictionary<string, List<string>> treeMap, string preferredRoot)
        {
            var roots = new List<string>();
            if (treeMap == null || treeMap.Count == 0)
            {
                return roots;
            }

            var childSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in treeMap)
            {
                if (entry.Value == null) continue;
                foreach (var child in entry.Value)
                {
                    if (!string.IsNullOrWhiteSpace(child))
                    {
                        childSet.Add(NormalizeTreePath(child));
                    }
                }
            }

            roots = treeMap.Keys
                .Where(key => !childSet.Contains(NormalizeTreePath(key)))
                .OrderBy(key => NormalizeTreePath(key))
                .ToList();

            if (roots.Count == 0)
            {
                roots.AddRange(treeMap.Keys.OrderBy(key => NormalizeTreePath(key)));
            }

            if (!string.IsNullOrWhiteSpace(preferredRoot))
            {
                var normalizedPreferred = NormalizeTreePath(preferredRoot);
                var preferred = roots.FirstOrDefault(root => string.Equals(NormalizeTreePath(root), normalizedPreferred, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(preferred))
                {
                    roots.Remove(preferred);
                    roots.Insert(0, preferred);
                }
            }

            return roots;
        }

        private Dictionary<string, List<string>> ResolveFullTreeMap(ScanResult result)
        {
            if (result == null)
            {
                return null;
            }

            var treeMap = result.TreeMap;
            var preferredRoot = !string.IsNullOrWhiteSpace(result.RootPath) ? result.RootPath : RootPath;
            var detailsTreeMap = BuildTreeMapFromDetails(result.Details, preferredRoot);
            var treeMapCount = treeMap == null ? 0 : treeMap.Count;
            var detailsTreeMapCount = detailsTreeMap == null ? 0 : detailsTreeMap.Count;

            Debug.WriteLine(string.Format(
                "[TreeMap] import source counts => treeMap:{0}, detailsTreeMap:{1}, root:{2}",
                treeMapCount,
                detailsTreeMapCount,
                preferredRoot));

            if ((treeMap == null || treeMap.Count == 0) && detailsTreeMap != null && detailsTreeMap.Count > 0)
            {
                Debug.WriteLine("[TreeMap] using detailsTreeMap (treeMap missing or empty)");
                return detailsTreeMap;
            }

            if (treeMap != null && treeMap.Count > 0)
            {
                Debug.WriteLine("[TreeMap] using persisted treeMap");
                return treeMap;
            }

            Debug.WriteLine("[TreeMap] fallback to export records treeMap reconstruction");
            return BuildTreeMapFromExportRecords(result.TempDataPath, preferredRoot);
        }

        private bool NodeMatchesTreeFilters(string path, Dictionary<string, FolderDetail> details)
        {
            if (details == null || !details.TryGetValue(path, out var detail) || detail == null)
            {
                return !AnyTreeFilterEnabled();
            }

            var diff = detail.DiffSummary;
            var hasDiff = diff != null && (diff.Added.Count > 0 || diff.Removed.Count > 0);
            var hasExplicitDeny = diff != null && diff.DenyExplicitCount > 0;
            var hasBaselineMismatch = detail.BaselineSummary != null && (detail.BaselineSummary.Added.Count > 0 || detail.BaselineSummary.Removed.Count > 0);

            var hasTypeMatch = true;
            var hasFiles = detail.HasFileEntries;
            var hasFolders = detail.HasFolderEntries;
            if (TreeFilterFilesOnly && !TreeFilterFoldersOnly)
            {
                hasTypeMatch = hasFiles;
            }
            else if (TreeFilterFoldersOnly && !TreeFilterFilesOnly)
            {
                hasTypeMatch = hasFolders;
            }

            if (!hasTypeMatch)
            {
                return false;
            }

            var includeByCategory = false;
            var categoryFilterSelected = false;

            if (TreeFilterExplicitOnly)
            {
                categoryFilterSelected = true;
                includeByCategory = includeByCategory || detail.HasExplicitPermissions;
            }

            if (TreeFilterInheritanceDisabledOnly)
            {
                categoryFilterSelected = true;
                includeByCategory = includeByCategory || detail.IsInheritanceDisabled;
            }

            if (TreeFilterDiffOnly)
            {
                categoryFilterSelected = true;
                includeByCategory = includeByCategory || hasDiff;
            }

            if (TreeFilterExplicitDenyOnly)
            {
                categoryFilterSelected = true;
                includeByCategory = includeByCategory || hasExplicitDeny;
            }

            if (TreeFilterBaselineMismatchOnly)
            {
                categoryFilterSelected = true;
                includeByCategory = includeByCategory || hasBaselineMismatch;
            }

            return !categoryFilterSelected || includeByCategory;
        }

        private static bool IsFileResourceType(string resourceType)
        {
            return string.Equals(resourceType, "File", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFolderResourceType(string resourceType)
        {
            return string.Equals(resourceType, "Folder", StringComparison.OrdinalIgnoreCase)
                || string.Equals(resourceType, "Cartella", StringComparison.OrdinalIgnoreCase)
                || string.Equals(resourceType, "Directory", StringComparison.OrdinalIgnoreCase);
        }

        private bool AnyTreeFilterEnabled()
        {
            return TreeFilterExplicitOnly
                || TreeFilterInheritanceDisabledOnly
                || TreeFilterDiffOnly
                || TreeFilterExplicitDenyOnly
                || TreeFilterBaselineMismatchOnly
                || (TreeFilterFilesOnly ^ TreeFilterFoldersOnly);
        }

        private void UpdateSelectedFolderInfo(string path, FolderDetail detail)
        {
            var entries = detail == null ? new List<AceEntry>() : detail.AllEntries;
            var detectedPathKind = PathResolver.DetectPathKind(path);
            SelectedPathKind = FormatPathKind(detectedPathKind);
            SelectedOwnerSummary = entries.Select(e => e.Owner).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "-";
            SelectedInheritanceDisabled = detail != null && detail.IsInheritanceDisabled;
            SelectedInheritanceSummary = LocalizationManager.Text(SelectedInheritanceDisabled
                ? "Results.InheritanceDisabled"
                : "Results.InheritanceActive");
            SelectedTotalAceCount = entries.Count;
            SelectedExplicitAceCount = entries.Count(e => !e.IsInherited);
            SelectedInheritedAceCount = entries.Count(e => e.IsInherited);
            SelectedDenyAceCount = entries.Count(e => string.Equals(e.AllowDeny, "Deny", StringComparison.OrdinalIgnoreCase));
            var layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (entries.Count > 0) layers.Add(LocalizationManager.Text("Results.PermissionLayer.Ntfs"));
            if (detail != null && detail.HasShareEntries) layers.Add(LocalizationManager.Text("Results.PermissionLayer.Share"));
            if (detail != null && detail.HasEffectiveEntries) layers.Add(LocalizationManager.Text("Results.PermissionLayer.Effective"));
            if (detectedPathKind == PathKind.Unsupported) layers.Add(LocalizationManager.Text("Results.PermissionLayer.Unsupported"));
            SelectedPermissionLayers = layers.Count == 0 ? "-" : string.Join(", ", layers);
            SelectedRiskSummary = LocalizationManager.Format(
                "Results.RiskSummaryFormat",
                entries.Count(e => string.Equals(NormalizeRiskLevel(e.RiskLevel), "high", StringComparison.Ordinal)),
                entries.Count(e => string.Equals(NormalizeRiskLevel(e.RiskLevel), "medium", StringComparison.Ordinal)),
                entries.Count(e => string.Equals(NormalizeRiskLevel(e.RiskLevel), "low", StringComparison.Ordinal)));
            var warning = entries
                .Select(e => e.AuditSummary)
                .FirstOrDefault(IsAcquisitionWarningSummary);
            if (string.IsNullOrWhiteSpace(warning) && detectedPathKind == PathKind.Unsupported)
            {
                warning = LocalizationManager.Text("Results.NfsWarning");
            }
            SelectedAcquisitionWarnings = string.IsNullOrWhiteSpace(warning) ? "-" : warning;
            SelectedScannedAtText = _scanResult != null && _scanResult.ScannedAtUtc != default(DateTime)
                ? _scanResult.ScannedAtUtc.ToLocalTime().ToString("dd-MM-yyyy HH:mm:ss")
                : "-";
        }

        private string ResolveTreeRoot(Dictionary<string, List<string>> treeMap, string preferredRoot)
        {
            if (treeMap == null || treeMap.Count == 0)
            {
                return preferredRoot;
            }
            if (!string.IsNullOrWhiteSpace(preferredRoot))
            {
                var normalizedPreferred = NormalizeTreePath(preferredRoot);
                var matchingRoot = treeMap.Keys.FirstOrDefault(
                    key => string.Equals(NormalizeTreePath(key), normalizedPreferred, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(matchingRoot))
                {
                    return matchingRoot;
                }
            }

            var childSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in treeMap)
            {
                foreach (var child in entry.Value)
                {
                    if (!string.IsNullOrWhiteSpace(child))
                    {
                        childSet.Add(NormalizeTreePath(child));
                    }
                }
            }

            var roots = treeMap.Keys
                .Where(key => !childSet.Contains(NormalizeTreePath(key)))
                .OrderBy(key => NormalizeTreePath(key).Length)
                .ToList();
            if (roots.Count > 0)
            {
                return roots[0];
            }

            return treeMap.Keys.First();
        }

        private static string FormatPathKind(PathKind pathKind)
        {
            switch (pathKind)
            {
                case PathKind.Local:
                    return LocalizationManager.Text("PathKind.Local");
                case PathKind.UncSmb:
                    return LocalizationManager.Text("PathKind.UncSmb");
                case PathKind.Dfs:
                    return LocalizationManager.Text("PathKind.Dfs");
                case PathKind.WslUnc:
                    return LocalizationManager.Text("PathKind.WslUnc");
                case PathKind.Unsupported:
                    return LocalizationManager.Text("PathKind.Unsupported");
                default:
                    return LocalizationManager.Text("PathKind.Unknown");
            }
        }

        private static bool IsAcquisitionWarningSummary(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            if (string.Equals(value, "No SACL entries", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "Nessuna voce SACL", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return value.IndexOf("unavailable", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("missing", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("not read", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("non disponibile", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("accesso negato", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("mancante", StringComparison.OrdinalIgnoreCase) >= 0
                || value.IndexOf("non letti", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
