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
using System.IO;
using System.IO.Compression;
using Newtonsoft.Json;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public partial class AnalysisArchive
    {
        private const int CurrentArchiveVersion = 7;
        private const string ArchiveFileExtension = ".ntaudit";
        private const string DataEntryName = "data.jsonl";
        private const string ErrorsEntryName = "errors.jsonl";
        private const string TreeEntryName = "tree.json";
        private const string MetaEntryName = "meta.json";
        private const string FolderFlagsEntryName = "folderflags.json";
        private const string SqliteEntryName = "analysis.sqlite";
        private const int SqliteLazyLoadThreshold = 5000;
        private const string AnalysisExportsDir = RuntimePaths.ExportsDirectoryName;
        private const string AnalysisImportsDir = RuntimePaths.ImportsDirectoryName;
        private static readonly TimeSpan AnalysisImportRetention = TimeSpan.FromDays(7);
        private static readonly TimeSpan AnalysisExportRetention = TimeSpan.FromDays(7);

        public void Export(ScanResult result, string rootPath, string outputPath)
        {
            if (result == null) throw new ArgumentNullException("result");
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path required", "outputPath");
            var dataPath = PathResolver.ToExtendedPath(result.TempDataPath);
            if (string.IsNullOrWhiteSpace(result.TempDataPath) || !File.Exists(dataPath))
            {
                throw new FileNotFoundException("Scan data file not found.", result.TempDataPath);
            }

            var exportOptions = (result.ScanOptions ?? LoadScanOptions(result.TempDataPath))?.CreateArchiveSafeCopy();
            var resolvedRootPath = ResolveArchiveRoot(rootPath, result.TempDataPath, exportOptions);
            var resolvedPathKind = result.RootPathKind == PathKind.Unknown ? PathResolver.DetectPathKind(resolvedRootPath) : result.RootPathKind;
            var exportTreeMap = result.TreeMap;
            if (exportTreeMap == null || exportTreeMap.Count == 0)
            {
                exportTreeMap = BuildTreeFromExport(result.TempDataPath, resolvedRootPath);
            }

            var normalizedOutputPath = EnsureArchiveOutputPath(outputPath);
            var ioOutputPath = PathResolver.ToExtendedPath(normalizedOutputPath);
            var outputDirectory = Path.GetDirectoryName(ioOutputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            CleanupAnalysisWorkspace(AnalysisExportsDir, AnalysisExportRetention);
            var exportWorkspace = GetAnalysisWorkspace(AnalysisExportsDir);
            var tempOutput = Path.Combine(exportWorkspace, string.Format("archive_{0}.tmp", Guid.NewGuid().ToString("N")));
            var sqlitePath = EnsureSqlitePayload(result, exportWorkspace);
            if (File.Exists(tempOutput))
            {
                File.Delete(tempOutput);
            }

            try
            {
                using (var archive = ZipFile.Open(tempOutput, ZipArchiveMode.Create))
                {
                    AddFileEntry(archive, DataEntryName, result.TempDataPath);
                    if (!AddFileEntry(archive, ErrorsEntryName, result.ErrorPath))
                    {
                        AddEmptyEntry(archive, ErrorsEntryName);
                    }
                    AddJsonEntry(archive, TreeEntryName, exportTreeMap);
                    AddJsonEntry(archive, FolderFlagsEntryName, BuildFolderFlags(result.Details));
                    AddFileEntry(archive, SqliteEntryName, sqlitePath);
                    AddJsonEntry(archive, MetaEntryName, new ArchiveMeta
                    {
                        RootPath = resolvedRootPath,
                        RootPathKind = resolvedPathKind,
                        CreatedAt = result.ScannedAtUtc == default(DateTime) ? DateTime.UtcNow : result.ScannedAtUtc,
                        Version = CurrentArchiveVersion,
                        ScanOptions = exportOptions,
                        DataRecordCount = CountNonEmptyLines(result.TempDataPath),
                        ErrorRecordCount = CountNonEmptyLines(result.ErrorPath)
                    });
                }

                ValidateArchiveEntries(tempOutput);

                if (File.Exists(ioOutputPath))
                {
                    File.Delete(ioOutputPath);
                }
                File.Move(tempOutput, ioOutputPath);
            }
            finally
            {
                if (File.Exists(tempOutput))
                {
                    File.Delete(tempOutput);
                }
            }
        }

        public AnalysisArchiveResult Import(string archivePath)
        {
            if (string.IsNullOrWhiteSpace(archivePath)) throw new ArgumentException("Archive path required", "archivePath");
            var normalizedArchivePath = EnsureArchiveInputPath(archivePath);
            var ioArchivePath = PathResolver.ToExtendedPath(normalizedArchivePath);
            if (!File.Exists(ioArchivePath)) throw new FileNotFoundException("Analysis archive not found.", archivePath);
            var archiveName = Path.GetFileNameWithoutExtension(normalizedArchivePath);
            if (string.IsNullOrWhiteSpace(archiveName)) archiveName = "import";
            foreach (var invalid in Path.GetInvalidFileNameChars()) archiveName = archiveName.Replace(invalid, '_');
            CleanupAnalysisWorkspace(AnalysisImportsDir, AnalysisImportRetention);
            var tempFolderName = string.Format("{0}_{1}_{2}", archiveName, DateTime.Now.ToString("yyyy_MM_dd_HH_mm"), Guid.NewGuid().ToString("N"));
            var tempDir = Path.Combine(GetAnalysisWorkspace(AnalysisImportsDir), tempFolderName);
            Directory.CreateDirectory(tempDir);
            var importSucceeded = false;

            try
            {
                using (var archive = ZipFile.OpenRead(ioArchivePath))
                {
                    ExtractEntry(archive, DataEntryName, tempDir);
                    ExtractEntry(archive, ErrorsEntryName, tempDir);
                    ExtractEntry(archive, TreeEntryName, tempDir);
                    ExtractEntry(archive, FolderFlagsEntryName, tempDir);
                    ExtractEntry(archive, SqliteEntryName, tempDir);
                    ExtractEntry(archive, MetaEntryName, tempDir);
                }

                var dataPath = Path.Combine(tempDir, DataEntryName);
                var errorPath = Path.Combine(tempDir, ErrorsEntryName);
                var treePath = Path.Combine(tempDir, TreeEntryName);
                var folderFlagsPath = Path.Combine(tempDir, FolderFlagsEntryName);
                var sqlitePath = Path.Combine(tempDir, SqliteEntryName);
                var metaPath = Path.Combine(tempDir, MetaEntryName);
                if (!File.Exists(dataPath))
                {
                    throw new InvalidDataException("Archivio analisi non valido: dati mancanti.");
                }
                if (!File.Exists(errorPath))
                {
                    File.WriteAllText(errorPath, string.Empty);
                }

                var meta = LoadMeta(metaPath);
                ValidateImportedDataForCompatibility(meta, dataPath, errorPath);
                var scanOptions = meta.ScanOptions ?? LoadScanOptions(dataPath);
                var resolvedRootPath = ResolveArchiveRoot(meta.RootPath, dataPath, scanOptions);
                var treeMap = LoadTreeMap(treePath, dataPath, resolvedRootPath);
                var folderFlags = LoadFolderFlags(folderFlagsPath);
                var hasSqlitePayload = File.Exists(sqlitePath);
                var useSqliteBackend = hasSqlitePayload && meta.DataRecordCount >= SqliteLazyLoadThreshold;
                System.Collections.Generic.Dictionary<string, FolderDetail> details;
                if (useSqliteBackend)
                {
                    details = BuildDetailsFromFolderFlags(treeMap, folderFlags);
                    details = new AnalysisSqliteStore().LoadFolderDetailPlaceholders(sqlitePath, details);
                }
                else
                {
                    details = BuildDetailsFromExport(dataPath);
                    ApplyFolderFlags(details, folderFlags);
                }

                var result = new ScanResult
                {
                    TempDataPath = dataPath,
                    ErrorPath = errorPath,
                    SqliteDatabasePath = hasSqlitePayload ? sqlitePath : null,
                    UsesSqliteBackend = useSqliteBackend,
                    Details = details,
                    TreeMap = treeMap,
                    RootPath = resolvedRootPath,
                    RootPathKind = meta.RootPathKind == PathKind.Unknown ? PathResolver.DetectPathKind(resolvedRootPath) : meta.RootPathKind,
                    ScanOptions = scanOptions,
                    ScannedAtUtc = NormalizeImportedTimestamp(meta.CreatedAt, ioArchivePath)
                };

                importSucceeded = true;
                return new AnalysisArchiveResult
                {
                    ScanResult = result,
                    RootPath = resolvedRootPath,
                    RootPathKind = result.RootPathKind,
                    ScannedAtUtc = result.ScannedAtUtc,
                    ScanOptions = scanOptions,
                    UsesSqliteBackend = useSqliteBackend
                };
            }
            finally
            {
                if (!importSucceeded && Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
    }

    public class AnalysisArchiveResult
    {
        public ScanResult ScanResult { get; set; }
        public string RootPath { get; set; }
        public PathKind RootPathKind { get; set; }
        public DateTime ScannedAtUtc { get; set; }
        public ScanOptions ScanOptions { get; set; }
        public bool UsesSqliteBackend { get; set; }
    }
}
