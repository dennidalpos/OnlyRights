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
using System.Linq;
using NtfsAudit.Core.Models;

namespace NtfsAudit.Core.Services
{
    public static class ScanPathCompatibilityPolicy
    {
        public static bool IsSupportedPathKind(PathKind kind)
        {
            return kind == PathKind.Local
                || kind == PathKind.UncSmb
                || kind == PathKind.Dfs
                || kind == PathKind.WslUnc;
        }

        public static bool SupportsConfiguredCredential(PathKind kind)
        {
            return kind == PathKind.UncSmb
                || kind == PathKind.Dfs
                || kind == PathKind.WslUnc;
        }

        public static bool SupportsSharePermissions(PathKind kind)
        {
            return kind == PathKind.UncSmb
                || kind == PathKind.Dfs;
        }

        public static ScanPathCompatibilityEvaluation EvaluatePaths(IEnumerable<string> rootPaths)
        {
            var paths = (rootPaths ?? Array.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToList();
            if (paths.Count == 0)
            {
                return new ScanPathCompatibilityEvaluation
                {
                    EffectivePathKind = PathKind.Unknown,
                    IsSupported = true,
                    SupportsConfiguredCredential = true,
                    SupportsSharePermissions = true,
                    SummaryText = string.Empty,
                    DisabledOptionsText = string.Empty,
                    DisabledOptions = Array.Empty<string>(),
                    ReasonTexts = Array.Empty<string>()
                };
            }

            var kinds = paths
                .Select(PathResolver.DetectPathKind)
                .ToList();
            return BuildForKinds(kinds);
        }

        public static string BuildUnsupportedRootMessage(string rootPath)
        {
            return $"Unsupported path or provider in Windows: {(string.IsNullOrWhiteSpace(rootPath) ? "(empty)" : rootPath)}";
        }

        public static bool LooksLikeUnsupportedRoot(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return false;
            }

            var trimmed = rootPath.Trim();
            if (trimmed.StartsWith(@"\\", StringComparison.Ordinal)
                || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                return false;
            }

            if (trimmed.Length >= 2 && trimmed[1] == ':')
            {
                return false;
            }

            if (trimmed.StartsWith("/", StringComparison.Ordinal)
                || trimmed.StartsWith("~", StringComparison.Ordinal))
            {
                return true;
            }

            return trimmed.IndexOf("://", StringComparison.Ordinal) > 0;
        }

        private static ScanPathCompatibilityEvaluation BuildForKinds(IReadOnlyCollection<PathKind> kinds)
        {
            var normalizedKinds = (kinds == null || kinds.Count == 0
                    ? new[] { PathKind.Unknown }
                    : kinds)
                .Distinct()
                .ToList();

            var isSupported = normalizedKinds.All(IsSupportedPathKind);
            var supportsCredential = normalizedKinds.All(SupportsConfiguredCredential);
            var supportsSharePermissions = normalizedKinds.All(SupportsSharePermissions);
            var reasons = new List<string>();
            var disabledOptions = new List<string>();

            if (!isSupported)
            {
                reasons.Add("The selected root does not use a supported Windows filesystem path.");
                disabledOptions.Add("scan");
            }

            if (!supportsCredential)
            {
                reasons.Add("Global credentials apply only to UNC/DFS/WSL paths exposed by Windows.");
                disabledOptions.Add("credential");
            }

            if (!supportsSharePermissions)
            {
                reasons.Add("SMB share permissions are available only for UNC/DFS shares.");
                disabledOptions.Add("share");
            }

            if (normalizedKinds.Contains(PathKind.WslUnc))
            {
                reasons.Add("\\\\wsl$ paths are treated as Windows UNC paths, but without a separate SMB share layer.");
            }

            if (normalizedKinds.Contains(PathKind.Dfs))
            {
                reasons.Add("DFS namespaces remain supported; jobs run against the selected effective UNC target.");
            }

            var effectivePathKind = normalizedKinds.Count == 1
                ? normalizedKinds[0]
                : normalizedKinds.Contains(PathKind.Unsupported)
                    ? PathKind.Unsupported
                    : PathKind.Unknown;
            var summary = !isSupported
                ? "Some options are blocked because one or more roots are not compatible with Windows."
                : reasons.Count == 0
                    ? "The selected options are compatible with the current root."
                    : string.Join(" ", reasons);

            return new ScanPathCompatibilityEvaluation
            {
                EffectivePathKind = effectivePathKind,
                IsSupported = isSupported,
                SupportsConfiguredCredential = supportsCredential,
                SupportsSharePermissions = supportsSharePermissions,
                SummaryText = summary,
                DisabledOptionsText = disabledOptions.Count == 0 ? string.Empty : string.Join(", ", disabledOptions.Distinct()),
                DisabledOptions = disabledOptions.Distinct().ToList(),
                ReasonTexts = reasons
            };
        }
    }
}
