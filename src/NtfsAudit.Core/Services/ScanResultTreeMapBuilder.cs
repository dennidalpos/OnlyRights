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
using NtfsAudit.Core.Models;

#nullable enable

namespace NtfsAudit.Core.Services
{
    public static class ScanResultTreeMapBuilder
    {
        public static Dictionary<string, List<string>>? BuildFromDetails(
            Dictionary<string, FolderDetail>? details,
            string? fallbackRoot)
        {
            if (details == null || details.Count == 0)
            {
                if (string.IsNullOrWhiteSpace(fallbackRoot))
                {
                    return null;
                }
                return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    [fallbackRoot] = new List<string>()
                };
            }

            var normalizedRoot = NormalizeTreePath(fallbackRoot);
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in details.Keys.Where(key => !string.IsNullOrWhiteSpace(key)))
            {
                AddPathWithAncestors(map, path, normalizedRoot);
            }

            if (!string.IsNullOrWhiteSpace(fallbackRoot) && !map.ContainsKey(fallbackRoot))
            {
                map[fallbackRoot] = new List<string>();
            }

            return map;
        }

        public static void AddPathWithAncestors(Dictionary<string, List<string>> map, string path, string? normalizedRoot)
        {
            if (!IsWithinRoot(NormalizeTreePath(path), normalizedRoot))
            {
                return;
            }
            var current = path;
            if (!map.ContainsKey(current))
            {
                map[current] = new List<string>();
            }

            while (true)
            {
                var parent = SafeGetParentPath(current);
                if (string.IsNullOrWhiteSpace(parent))
                {
                    break;
                }
                var normalizedParent = NormalizeTreePath(parent);
                if (!IsWithinRoot(normalizedParent, normalizedRoot))
                {
                    break;
                }
                if (!map.ContainsKey(parent))
                {
                    map[parent] = new List<string>();
                }
                if (!map[parent].Contains(current, StringComparer.OrdinalIgnoreCase))
                {
                    map[parent].Add(current);
                }
                if (!string.IsNullOrWhiteSpace(normalizedRoot)
                    && string.Equals(normalizedParent, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                current = parent;
            }
        }

        public static string NormalizeTreePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            var normalized = PathResolver.FromExtendedPath(path);
            return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public static bool IsWithinRoot(string? candidate, string? root)
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

        private static string? SafeGetParentPath(string path)
        {
            try
            {
                var parent = Directory.GetParent(path);
                return parent == null ? null : parent.FullName;
            }
            catch
            {
                return null;
            }
        }
    }
}
