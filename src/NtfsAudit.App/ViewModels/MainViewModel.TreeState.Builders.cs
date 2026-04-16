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
using NtfsAudit.App.Export;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;

namespace NtfsAudit.App.ViewModels
{
    public partial class MainViewModel
    {
        private Dictionary<string, List<string>> BuildTreeMapFromDetails(
            Dictionary<string, FolderDetail> details,
            string fallbackRoot)
        {
            return ScanResultTreeMapBuilder.BuildFromDetails(details, fallbackRoot);
        }

        private Dictionary<string, List<string>> BuildTreeMapFromExportRecords(string dataPath, string fallbackRoot)
        {
            if (string.IsNullOrWhiteSpace(dataPath))
            {
                return null;
            }
            var ioPath = PathResolver.ToExtendedPath(dataPath);
            if (!File.Exists(ioPath))
            {
                return null;
            }

            var normalizedRoot = NormalizeTreePath(fallbackRoot);
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var line in File.ReadLines(ioPath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    ExportRecord record;
                    try
                    {
                        record = Newtonsoft.Json.JsonConvert.DeserializeObject<ExportRecord>(line);
                    }
                    catch
                    {
                        continue;
                    }
                    if (record == null) continue;
                    var path = record.FolderPath;
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    if (string.Equals(record.PrincipalType, "Meta", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(record.PrincipalName, "SCAN_OPTIONS", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    AddPathWithAncestors(map, path, normalizedRoot);
                }
            }
            catch
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(fallbackRoot) && !map.ContainsKey(fallbackRoot))
            {
                map[fallbackRoot] = new List<string>();
            }

            return map.Count == 0 ? null : map;
        }

        private void AddPathWithAncestors(Dictionary<string, List<string>> map, string path)
            => AddPathWithAncestors(map, path, NormalizeTreePath(RootPath));

        private void AddPathWithAncestors(Dictionary<string, List<string>> map, string path, string normalizedRoot)
        {
            ScanResultTreeMapBuilder.AddPathWithAncestors(map, path, normalizedRoot);
        }

        private static string NormalizeTreePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            return ScanResultTreeMapBuilder.NormalizeTreePath(path);
        }

        private static bool IsWithinRoot(string candidate, string root)
        {
            return ScanResultTreeMapBuilder.IsWithinRoot(candidate, root);
        }

        private void ApplyDiffs(ScanResult result)
        {
            if (result == null || result.Details == null) return;
            if (result.UsesSqliteBackend) return;
            var diffService = new AclDiffService();
            diffService.ApplyDiffs(result.Details);
        }

        private FolderDetail GetFolderDetail(string path)
        {
            if (_scanResult == null || string.IsNullOrWhiteSpace(path) || _scanResult.Details == null)
            {
                return null;
            }

            if (!_scanResult.Details.TryGetValue(path, out var detail) || detail == null)
            {
                return null;
            }

            if (!detail.EntriesLoaded
                && _scanResult.UsesSqliteBackend
                && !string.IsNullOrWhiteSpace(_scanResult.SqliteDatabasePath)
                && File.Exists(PathResolver.ToExtendedPath(_scanResult.SqliteDatabasePath)))
            {
                var loadedDetail = _analysisSqliteStore.LoadFolderDetail(_scanResult.SqliteDatabasePath, path);
                if (loadedDetail != null)
                {
                    _scanResult.Details[path] = loadedDetail;
                    detail = loadedDetail;
                }
            }

            return detail;
        }
    }
}
