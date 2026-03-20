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
                    throw new InvalidDataException("Export incompleto: data.jsonl mancante o vuoto.");
                }
                if (errorsEntry == null)
                {
                    throw new InvalidDataException("Export incompleto: errors.jsonl mancante.");
                }
                if (treeEntry == null)
                {
                    throw new InvalidDataException("Export incompleto: tree.json mancante.");
                }
                if (flagsEntry == null)
                {
                    throw new InvalidDataException("Export incompleto: folderflags.json mancante.");
                }
                if (metaEntry == null || metaEntry.Length == 0)
                {
                    throw new InvalidDataException("Export incompleto: meta.json mancante o vuoto.");
                }
            }
        }

        private void ValidateImportedDataForCompatibility(ArchiveMeta meta, string dataPath, string errorPath)
        {
            if (meta == null)
            {
                throw new InvalidDataException("Metadati archivio non validi.");
            }

            if (meta.Version <= 0)
            {
                meta.Version = 1;
            }

            if (!File.Exists(dataPath))
            {
                throw new InvalidDataException("Archivio analisi non valido: data.jsonl mancante.");
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
                throw new InvalidDataException("Archivio analisi non valido o scansione legacy non compatibile: nessun record dati importabile.");
            }

            if (meta.Version >= 5 && meta.DataRecordCount > 0 && meta.DataRecordCount != parsedRecords)
            {
                throw new InvalidDataException("Archivio analisi incompleto: il numero di record esportati non corrisponde ai dati nel file.");
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
                    throw new InvalidDataException("Archivio analisi incompleto: il numero di errori esportati non corrisponde al file.");
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

        private ArchiveMeta LoadMeta(string metaPath)
        {
            if (!File.Exists(metaPath))
            {
                return new ArchiveMeta { Version = 1, RootPathKind = PathKind.Unknown };
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
                return new ArchiveMeta { Version = 1, RootPathKind = PathKind.Unknown };
            }
        }
    }
}
