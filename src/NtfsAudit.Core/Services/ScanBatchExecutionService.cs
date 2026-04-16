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
using System.Threading;
using NtfsAudit.App.Models;

#nullable enable

namespace NtfsAudit.App.Services
{
    public sealed class ScanBatchExecutionService
    {
        private readonly AnalysisArchive _analysisArchive;

        public ScanBatchExecutionService()
            : this(new AnalysisArchive())
        {
        }

        public ScanBatchExecutionService(AnalysisArchive analysisArchive)
        {
            _analysisArchive = analysisArchive ?? throw new ArgumentNullException(nameof(analysisArchive));
        }

        public ScanResult Execute(
            IReadOnlyList<string>? roots,
            ScanOptions optionsTemplate,
            Func<ScanOptions, string, ScanOptions> createRootOptions,
            Func<ScanOptions, CancellationToken, ScanResult> executeRootScan,
            CancellationToken token)
        {
            if (optionsTemplate == null) throw new ArgumentNullException(nameof(optionsTemplate));
            if (createRootOptions == null) throw new ArgumentNullException(nameof(createRootOptions));
            if (executeRootScan == null) throw new ArgumentNullException(nameof(executeRootScan));

            var firstRoot = roots == null || roots.Count == 0 ? optionsTemplate.RootPath : roots[0];
            var aggregateResult = new ScanResult
            {
                RootPath = firstRoot,
                Details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase),
                TreeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                ScanOptions = CloneOptions(optionsTemplate, firstRoot),
                ScannedAtUtc = DateTime.UtcNow
            };

            if (roots == null)
            {
                return aggregateResult;
            }

            foreach (var root in roots)
            {
                token.ThrowIfCancellationRequested();
                var options = createRootOptions(optionsTemplate, root);
                var result = executeRootScan(options, token);
                if (result == null)
                {
                    continue;
                }

                if (result.TreeMap == null || result.TreeMap.Count == 0)
                {
                    result.TreeMap = ScanResultTreeMapBuilder.BuildFromDetails(result.Details, result.RootPath);
                }

                var previousTempDataPath = aggregateResult.TempDataPath;
                var previousErrorPath = aggregateResult.ErrorPath;
                MergeScanResult(aggregateResult, result);
                DeleteSupersededRuntimeFile(previousTempDataPath, aggregateResult.TempDataPath);
                DeleteSupersededRuntimeFile(previousErrorPath, aggregateResult.ErrorPath);

                ExportSingleRootArchive(options, root, result);
            }

            return aggregateResult;
        }

        public static ScanOptions CloneOptions(ScanOptions template, string root)
        {
            var options = template.Clone();
            options.RootPath = root;
            return options;
        }

        public static void MergeScanResult(ScanResult aggregate, ScanResult current)
        {
            if (aggregate == null || current == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(current.TempDataPath)) aggregate.TempDataPath = current.TempDataPath;
            if (!string.IsNullOrWhiteSpace(current.ErrorPath)) aggregate.ErrorPath = current.ErrorPath;
            if (!string.IsNullOrWhiteSpace(current.SqliteDatabasePath)) aggregate.SqliteDatabasePath = current.SqliteDatabasePath;
            aggregate.UsesSqliteBackend = aggregate.UsesSqliteBackend || current.UsesSqliteBackend;
            if (!string.IsNullOrWhiteSpace(current.RootPath) && string.IsNullOrWhiteSpace(aggregate.RootPath)) aggregate.RootPath = current.RootPath;
            if (aggregate.RootPathKind == PathKind.Unknown && current.RootPathKind != PathKind.Unknown) aggregate.RootPathKind = current.RootPathKind;
            if (current.ScannedAtUtc != default(DateTime)) aggregate.ScannedAtUtc = current.ScannedAtUtc;

            if (aggregate.Details == null) aggregate.Details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase);
            if (current.Details != null)
            {
                foreach (var detailPair in current.Details)
                {
                    if (detailPair.Value == null) continue;
                    FolderDetail? existing;
                    if (!aggregate.Details.TryGetValue(detailPair.Key, out existing))
                    {
                        aggregate.Details[detailPair.Key] = detailPair.Value;
                        continue;
                    }

                    FolderDetailMerger.Merge(existing, detailPair.Value);
                }
            }

            if (aggregate.TreeMap == null) aggregate.TreeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (current.TreeMap != null)
            {
                MergeTreeMap(aggregate.TreeMap, current.TreeMap);
            }
        }

        private void ExportSingleRootArchive(ScanOptions options, string root, ScanResult result)
        {
            if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            {
                return;
            }

            var outputDirectory = PathResolver.FromExtendedPath(options.OutputDirectory).Trim();
            Directory.CreateDirectory(PathResolver.ToExtendedPath(outputDirectory));
            var outputFile = Path.Combine(outputDirectory, BuildExportFileName(root, "ntaudit"));
            _analysisArchive.Export(result, root, outputFile);
        }

        private static void DeleteSupersededRuntimeFile(string? previousPath, string? currentPath)
        {
            if (!string.IsNullOrWhiteSpace(previousPath)
                && !string.Equals(previousPath, currentPath, StringComparison.OrdinalIgnoreCase))
            {
                RuntimeCleanupService.TryDeleteFile(previousPath);
            }
        }

        private static void MergeTreeMap(Dictionary<string, List<string>> target, Dictionary<string, List<string>> source)
        {
            if (target == null || source == null)
            {
                return;
            }

            foreach (var node in source)
            {
                List<string>? children;
                if (!target.TryGetValue(node.Key, out children) || children == null)
                {
                    target[node.Key] = node.Value == null ? new List<string>() : new List<string>(node.Value);
                    continue;
                }

                if (node.Value == null)
                {
                    continue;
                }

                foreach (var child in node.Value)
                {
                    if (!children.Contains(child, StringComparer.OrdinalIgnoreCase))
                    {
                        children.Add(child);
                    }
                }
            }
        }

        private static string BuildExportFileName(string rootPath, string extension)
        {
            var baseName = BuildScanNameFromRoot(rootPath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "Root";
            var timestamp = DateTime.Now.ToString("yyyy_MM_dd_HH_mm");
            return string.Format("{0}_{1}.{2}", baseName, timestamp, extension);
        }

        private static string BuildScanNameFromRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return "scan";
            var normalized = root.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(normalized)) return "scan";

            string name;
            if (normalized.StartsWith("\\", StringComparison.Ordinal))
            {
                var segments = normalized.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                name = segments.Length > 0 ? segments[segments.Length - 1] : string.Empty;
            }
            else
            {
                name = Path.GetFileName(normalized);
                if (string.IsNullOrWhiteSpace(name) && normalized.Length >= 2 && normalized[1] == ':')
                {
                    name = normalized.Substring(0, 1);
                }
            }

            name = SanitizeFileName(name);
            return string.IsNullOrWhiteSpace(name) ? "scan" : name;
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }
            return value;
        }
    }
}
