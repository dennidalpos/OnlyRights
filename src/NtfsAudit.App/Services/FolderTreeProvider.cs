using NtfsAudit.Core.Logging;
using NtfsAudit.Core.Export;
using NtfsAudit.Core.Cache;
using NtfsAudit.Core.Services;
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NtfsAudit.Core.Models;
using NtfsAudit.App.ViewModels;

namespace NtfsAudit.App.Services
{
    public class FolderTreeProvider
    {
        private readonly Dictionary<string, List<string>> _childrenMap;
        private readonly Dictionary<string, FolderDetail> _details;
        private readonly HashSet<string> _filePaths;

        public FolderTreeProvider(Dictionary<string, List<string>> childrenMap, Dictionary<string, FolderDetail> details)
        {
            _childrenMap = childrenMap;
            _details = details;
            _filePaths = BuildFilePathIndex(details);
        }

        public IEnumerable<FolderNodeViewModel> GetChildren(string parentPath)
        {
            List<string> children;
            if (!_childrenMap.TryGetValue(parentPath, out children))
            {
                yield break;
            }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(name)) name = child;
                var isFileNode = _filePaths.Contains(NormalizePath(child));
                var selectionPath = isFileNode ? parentPath : child;
                var flags = GetFlags(child, parentPath, isFileNode);
                yield return new FolderNodeViewModel(
                    child,
                    name,
                    selectionPath,
                    this,
                    isFileNode,
                    flags.hasFileEntries,
                    flags.hasExplicitPermissions,
                    flags.isInheritanceDisabled,
                    flags.explicitAddedCount,
                    flags.explicitRemovedCount,
                    flags.denyExplicitCount,
                    flags.isProtected,
                    flags.baselineAddedCount,
                    flags.baselineRemovedCount,
                    flags.hasExplicitNtfs,
                    flags.hasExplicitShare,
                    flags.hasHighRisk,
                    flags.hasMediumRisk,
                    flags.hasLowRisk);
            }
        }

        public bool HasChildren(string parentPath)
        {
            List<string> children;
            return _childrenMap.TryGetValue(parentPath, out children) && children.Count > 0;
        }

        private (bool hasFileEntries, bool hasExplicitPermissions, bool isInheritanceDisabled, int explicitAddedCount, int explicitRemovedCount, int denyExplicitCount, bool isProtected, int baselineAddedCount, int baselineRemovedCount, bool hasExplicitNtfs, bool hasExplicitShare, bool hasHighRisk, bool hasMediumRisk, bool hasLowRisk) GetFlags(string path, string parentPath, bool isFileNode)
        {
            if (_details == null || string.IsNullOrWhiteSpace(path))
            {
                return (false, false, false, 0, 0, 0, false, 0, 0, false, false, false, false, false);
            }

            if (isFileNode)
            {
                return GetFileFlags(path, parentPath);
            }

            FolderDetail detail;
            if (!_details.TryGetValue(path, out detail) || detail == null)
            {
                return (false, false, false, 0, 0, 0, false, 0, 0, false, false, false, false, false);
            }

            var summary = detail.DiffSummary;
            var added = summary == null ? 0 : summary.Added.Count(key => !key.IsInherited);
            var removed = summary == null ? 0 : summary.Removed.Count;
            var deny = summary == null ? 0 : summary.DenyExplicitCount;
            var isProtected = summary != null ? summary.IsProtected : detail.IsInheritanceDisabled;
            var baselineAdded = detail.BaselineSummary == null ? 0 : detail.BaselineSummary.Added.Count;
            var baselineRemoved = detail.BaselineSummary == null ? 0 : detail.BaselineSummary.Removed.Count;
            return (detail.HasFileEntries, detail.HasExplicitPermissions, detail.IsInheritanceDisabled, added, removed, deny, isProtected, baselineAdded, baselineRemoved, detail.HasExplicitNtfs, detail.HasExplicitShare, detail.HasHighRiskEntries, detail.HasMediumRiskEntries, detail.HasLowRiskEntries);
        }

        private (bool hasFileEntries, bool hasExplicitPermissions, bool isInheritanceDisabled, int explicitAddedCount, int explicitRemovedCount, int denyExplicitCount, bool isProtected, int baselineAddedCount, int baselineRemovedCount, bool hasExplicitNtfs, bool hasExplicitShare, bool hasHighRisk, bool hasMediumRisk, bool hasLowRisk) GetFileFlags(string path, string parentPath)
        {
            FolderDetail detail;
            if (string.IsNullOrWhiteSpace(parentPath)
                || !_details.TryGetValue(parentPath, out detail)
                || detail == null
                || detail.AllEntries == null)
            {
                return (false, false, false, 0, 0, 0, false, 0, 0, false, false, false, false, false);
            }

            var normalizedPath = NormalizePath(path);
            var entries = detail.AllEntries
                .Where(entry => string.Equals(entry.ResourceType, "File", System.StringComparison.OrdinalIgnoreCase)
                    && string.Equals(NormalizePath(entry.TargetPath), normalizedPath, System.StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (entries.Count == 0)
            {
                return (false, false, false, 0, 0, 0, false, 0, 0, false, false, false, false, false);
            }

            return (
                false,
                entries.Any(entry => entry.HasExplicitPermissions || !entry.IsInherited),
                false,
                0,
                0,
                entries.Count(entry => entry.IsExplicitDeny),
                false,
                0,
                0,
                entries.Any(entry => entry.PermissionLayer == PermissionLayer.Ntfs && (entry.HasExplicitPermissions || !entry.IsInherited)),
                entries.Any(entry => entry.PermissionLayer == PermissionLayer.Share && (entry.HasExplicitPermissions || !entry.IsInherited)),
                entries.Any(entry => string.Equals(entry.RiskLevel, "High", System.StringComparison.OrdinalIgnoreCase)),
                entries.Any(entry => string.Equals(entry.RiskLevel, "Medium", System.StringComparison.OrdinalIgnoreCase)),
                entries.Any(entry => string.Equals(entry.RiskLevel, "Low", System.StringComparison.OrdinalIgnoreCase)));
        }

        private static HashSet<string> BuildFilePathIndex(Dictionary<string, FolderDetail> details)
        {
            var filePaths = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            if (details == null)
            {
                return filePaths;
            }

            foreach (var detail in details.Values)
            {
                if (detail == null || detail.AllEntries == null)
                {
                    continue;
                }

                foreach (var targetPath in detail.AllEntries
                    .Where(entry => string.Equals(entry.ResourceType, "File", System.StringComparison.OrdinalIgnoreCase))
                    .Select(entry => NormalizePath(entry.TargetPath))
                    .Where(path => !string.IsNullOrWhiteSpace(path)))
                {
                    filePaths.Add(targetPath);
                }
            }

            return filePaths;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return PathResolver.FromExtendedPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
