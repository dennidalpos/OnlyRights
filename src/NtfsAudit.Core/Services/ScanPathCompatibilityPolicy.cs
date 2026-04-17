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
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
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
                return BuildForKinds(new[] { PathKind.Unknown });
            }

            var kinds = paths
                .Select(PathResolver.DetectPathKind)
                .ToList();
            return BuildForKinds(kinds);
        }

        public static string BuildUnsupportedRootMessage(string rootPath)
        {
            return string.Format(
                "Percorso o provider non supportato in ambiente Windows: {0}",
                string.IsNullOrWhiteSpace(rootPath) ? "(vuoto)" : rootPath);
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
                reasons.Add("La root selezionata non usa un percorso filesystem Windows supportato.");
                disabledOptions.Add("scan");
            }

            if (!supportsCredential)
            {
                reasons.Add("Le credenziali globali si applicano solo a percorsi UNC/DFS/WSL esposti da Windows.");
                disabledOptions.Add("credential");
            }

            if (!supportsSharePermissions)
            {
                reasons.Add("I permessi share SMB sono disponibili solo per share UNC/DFS.");
                disabledOptions.Add("share");
            }

            if (normalizedKinds.Contains(PathKind.WslUnc))
            {
                reasons.Add("I percorsi \\\\wsl$ sono trattati come UNC Windows, ma senza un layer share SMB separato.");
            }

            if (normalizedKinds.Contains(PathKind.Dfs))
            {
                reasons.Add("Le namespace DFS restano supportate; i job vengono eseguiti sul target UNC effettivo selezionato.");
            }

            var effectivePathKind = normalizedKinds.Count == 1
                ? normalizedKinds[0]
                : normalizedKinds.Contains(PathKind.Unsupported)
                    ? PathKind.Unsupported
                    : PathKind.Unknown;
            var summary = !isSupported
                ? "Alcune opzioni sono bloccate perché una o più root non sono compatibili con Windows."
                : reasons.Count == 0
                    ? "Le opzioni selezionate sono compatibili con la root corrente."
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
