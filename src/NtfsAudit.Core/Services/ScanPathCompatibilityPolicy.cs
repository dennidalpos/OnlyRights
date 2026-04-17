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
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public static class ScanPathCompatibilityPolicy
    {
        public static bool IsSupportedPathKind(PathKind kind)
        {
            return kind == PathKind.Local
                || kind == PathKind.Unc
                || kind == PathKind.Dfs
                || kind == PathKind.WslUnc;
        }

        public static bool SupportsConfiguredCredential(PathKind kind)
        {
            return kind == PathKind.Unc
                || kind == PathKind.Dfs
                || kind == PathKind.WslUnc;
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
    }
}
