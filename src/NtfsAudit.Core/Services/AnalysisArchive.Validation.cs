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
using Newtonsoft.Json;
using NtfsAudit.App.Export;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public partial class AnalysisArchive
    {
        private string ResolveArchiveRoot(string rootPath, string dataPath, ScanOptions scanOptions)
        {
            if (!string.IsNullOrWhiteSpace(rootPath))
            {
                return NormalizeExportPath(rootPath);
            }

            if (scanOptions == null && !string.IsNullOrWhiteSpace(dataPath))
            {
                scanOptions = LoadScanOptions(dataPath);
            }

            if (scanOptions != null && !string.IsNullOrWhiteSpace(scanOptions.RootPath))
            {
                return NormalizeExportPath(scanOptions.RootPath);
            }

            return string.Empty;
        }

        private void ValidateArchiveEntries(string archivePath)
        {
            using (var archive = System.IO.Compression.ZipFile.OpenRead(archivePath))
            {
                var dataEntry = archive.GetEntry(DataEntryName);
                var errorsEntry = archive.GetEntry(ErrorsEntryName);
                var treeEntry = archive.GetEntry(TreeEntryName);
                var flagsEntry = archive.GetEntry(FolderFlagsEntryName);
                var metaEntry = archive.GetEntry(MetaEntryName);

                if (dataEntry == null || dataEntry.Length == 0)
                {
                    throw new InvalidDataException("Incomplete export: data.jsonl is missing or empty.");
                }
                if (errorsEntry == null)
                {
                    throw new InvalidDataException("Incomplete export: errors.jsonl is missing.");
                }
                if (treeEntry == null)
                {
                    throw new InvalidDataException("Incomplete export: tree.json is missing.");
                }
                if (flagsEntry == null)
                {
                    throw new InvalidDataException("Incomplete export: folderflags.json is missing.");
                }
                if (metaEntry == null || metaEntry.Length == 0)
                {
                    throw new InvalidDataException("Incomplete export: meta.json is missing or empty.");
                }
            }
        }

        private void ValidateImportedDataForCompatibility(ArchiveMeta meta, string dataPath, string errorPath)
        {
            if (meta == null)
            {
                throw new InvalidDataException("Archive metadata is invalid.");
            }

            if (meta.Version <= 0)
            {
                meta.Version = 1;
            }

            if (!File.Exists(dataPath))
            {
                throw new InvalidDataException("Analysis archive is invalid: data.jsonl is missing.");
            }

            var parsedRecords = 0;
            foreach (var line in File.ReadLines(dataPath))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var record = JsonConvert.DeserializeObject<ExportRecord>(line);
                    if (record != null)
                    {
                        parsedRecords++;
                    }
                }
                catch
                {
                    if (meta.Version <= 2)
                    {
                        continue;
                    }
                }
            }

            if (parsedRecords == 0)
            {
                throw new InvalidDataException("Analysis archive is invalid or the legacy scan is incompatible: no importable data records were found.");
            }

            if (meta.Version >= 5 && meta.DataRecordCount > 0 && meta.DataRecordCount != parsedRecords)
            {
                throw new InvalidDataException("Analysis archive is incomplete: the exported record count does not match the data file.");
            }

            if (!File.Exists(errorPath))
            {
                File.WriteAllText(errorPath, string.Empty);
            }
            else if (meta.Version >= 5 && meta.ErrorRecordCount > 0)
            {
                var parsedErrors = CountNonEmptyLines(errorPath);
                if (parsedErrors != meta.ErrorRecordCount)
                {
                    throw new InvalidDataException("Analysis archive is incomplete: the exported error count does not match the file.");
                }
            }
        }

        private int CountNonEmptyLines(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return 0;
            }

            var ioPath = PathResolver.ToExtendedPath(path);
            if (!File.Exists(ioPath))
            {
                return 0;
            }

            var count = 0;
            foreach (var line in File.ReadLines(ioPath))
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    count++;
                }
            }

            return count;
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

        private static Dictionary<string, List<string>> NormalizeImportedTreeMap(
            Dictionary<string, List<string>> treeMap,
            Dictionary<string, FolderDetail> details,
            string rootPath)
        {
            var normalizedTreeMap = ScanResultTreeMapBuilder.BuildFromDetails(details, rootPath);
            if (normalizedTreeMap == null || normalizedTreeMap.Count == 0)
            {
                return treeMap ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            }

            if (treeMap == null || treeMap.Count == 0)
            {
                return normalizedTreeMap;
            }

            return normalizedTreeMap.Count > treeMap.Count
                ? normalizedTreeMap
                : treeMap;
        }

        private ArchiveMeta LoadMeta(string metaPath)
        {
            if (!File.Exists(metaPath))
            {
                return new ArchiveMeta { Version = 1, RootPathKind = PathKind.Unknown.ToString() };
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
                return new ArchiveMeta { Version = 1, RootPathKind = PathKind.Unknown.ToString() };
            }
        }

        private static PathKind NormalizeImportedRootPathKind(string value, string rootPath)
        {
            if (string.Equals(value, "Nfs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "4", StringComparison.OrdinalIgnoreCase))
            {
                return PathKind.UncSmb;
            }

            if (string.Equals(value, "Unc", StringComparison.OrdinalIgnoreCase))
            {
                return PathKind.UncSmb;
            }

            PathKind parsed;
            if (Enum.TryParse(value, true, out parsed) && Enum.IsDefined(typeof(PathKind), parsed))
            {
                return parsed == PathKind.Unknown ? PathResolver.DetectPathKind(rootPath) : parsed;
            }

            return PathResolver.DetectPathKind(rootPath);
        }
    }
}
