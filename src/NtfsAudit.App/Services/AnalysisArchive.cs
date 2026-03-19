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
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json;
using NtfsAudit.App.Export;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public class AnalysisArchive
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
                Dictionary<string, FolderDetail> details;
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
            using (var archive = ZipFile.OpenRead(archivePath))
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

        private Dictionary<string, FolderDetail> BuildDetailsFromExport(string dataPath)
        {
            var details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(dataPath)) return details;

            var dedupe = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(dataPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                ExportRecord record;
                try
                {
                    record = JsonConvert.DeserializeObject<ExportRecord>(line);
                }
                catch
                {
                    continue;
                }
                if (record == null) continue;
                var folderPath = NormalizeExportPath(record.FolderPath);
                if (string.IsNullOrWhiteSpace(folderPath)) continue;
                if (string.Equals(record.PrincipalType, "Meta", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(record.PrincipalName, "SCAN_OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                FolderDetail detail;
                if (!details.TryGetValue(folderPath, out detail))
                {
                    detail = new FolderDetail();
                    details[folderPath] = detail;
                }

                var entryKey = BuildEntryKey(record);
                HashSet<string> folderKeys;
                if (!dedupe.TryGetValue(folderPath, out folderKeys))
                {
                    folderKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    dedupe[folderPath] = folderKeys;
                }
                if (!folderKeys.Add(entryKey))
                {
                    continue;
                }

                var entry = new AceEntry
                {
                    FolderPath = folderPath,
                    PrincipalName = record.PrincipalName,
                    PrincipalSid = record.PrincipalSid,
                    PrincipalType = record.PrincipalType,
                    PermissionLayer = record.PermissionLayer,
                    AllowDeny = record.AllowDeny,
                    RightsSummary = record.RightsSummary,
                    RightsMask = record.RightsMask,
                    EffectiveRightsSummary = record.EffectiveRightsSummary,
                    EffectiveRightsMask = record.EffectiveRightsMask,
                    ShareRightsMask = record.ShareRightsMask,
                    NtfsRightsMask = record.NtfsRightsMask,
                    IsInherited = record.IsInherited,
                    AppliesToThisFolder = record.AppliesToThisFolder,
                    AppliesToSubfolders = record.AppliesToSubfolders,
                    AppliesToFiles = record.AppliesToFiles,
                    InheritanceFlags = record.InheritanceFlags,
                    PropagationFlags = record.PropagationFlags,
                    Source = record.Source,
                    PathKind = record.PathKind,
                    Depth = record.Depth,
                    ResourceType = record.ResourceType,
                    TargetPath = NormalizeExportPath(record.TargetPath),
                    Owner = record.Owner,
                    ShareName = record.ShareName,
                    ShareServer = record.ShareServer,
                    AuditSummary = record.AuditSummary,
                    RiskLevel = record.RiskLevel,
                    IsDisabled = record.IsDisabled,
                    IsServiceAccount = record.IsServiceAccount || SidClassifier.IsServiceAccountSid(record.PrincipalSid),
                    IsAdminAccount = record.IsAdminAccount || SidClassifier.IsPrivilegedGroupSid(record.PrincipalSid),
                    HasExplicitPermissions = record.HasExplicitPermissions,
                    IsInheritanceDisabled = record.IsInheritanceDisabled,
                    MemberNames = record.MemberNames == null ? null : new List<string>(record.MemberNames)
                };

                if (entry.PermissionLayer == PermissionLayer.Share)
                {
                    detail.ShareEntries.Add(entry);
                    detail.HasExplicitShare = true;
                    detail.HasShareEntries = true;
                }
                else if (entry.PermissionLayer == PermissionLayer.Effective)
                {
                    detail.EffectiveEntries.Add(entry);
                    detail.HasEffectiveEntries = true;
                }
                else
                {
                    detail.AllEntries.Add(entry);
                    detail.HasExplicitNtfs = detail.HasExplicitNtfs || entry.HasExplicitPermissions || !entry.IsInherited;
                    if (entry.HasExplicitPermissions || !entry.IsInherited)
                    {
                        detail.HasExplicitPermissions = true;
                    }
                }
                if (entry.IsInheritanceDisabled)
                {
                    detail.IsInheritanceDisabled = true;
                }

                detail.HasFileEntries = detail.HasFileEntries || string.Equals(entry.ResourceType, "File", StringComparison.OrdinalIgnoreCase);
                detail.HasFolderEntries = detail.HasFolderEntries || !string.Equals(entry.ResourceType, "File", StringComparison.OrdinalIgnoreCase);
                detail.HasHighRiskEntries = detail.HasHighRiskEntries || string.Equals(entry.RiskLevel, "Alto", StringComparison.OrdinalIgnoreCase);
                detail.HasMediumRiskEntries = detail.HasMediumRiskEntries || string.Equals(entry.RiskLevel, "Medio", StringComparison.OrdinalIgnoreCase);
                detail.HasLowRiskEntries = detail.HasLowRiskEntries || string.Equals(entry.RiskLevel, "Basso", StringComparison.OrdinalIgnoreCase);
            }

            return details;
        }

        private ScanOptions LoadScanOptions(string dataPath)
        {
            if (!File.Exists(dataPath)) return null;
            foreach (var line in File.ReadLines(dataPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                ExportRecord record;
                try
                {
                    record = JsonConvert.DeserializeObject<ExportRecord>(line);
                }
                catch
                {
                    continue;
                }
                if (record == null) continue;
                if (!string.Equals(record.PrincipalType, "Meta", StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(record.PrincipalName, "SCAN_OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return new ScanOptions
                {
                    RootPath = record.FolderPath,
                    IncludeInherited = record.IncludeInherited,
                    ResolveIdentities = record.ResolveIdentities,
                    ExcludeServiceAccounts = record.ExcludeServiceAccounts,
                    ExcludeAdminAccounts = record.ExcludeAdminAccounts,
                    EnableAdvancedAudit = record.EnableAdvancedAudit,
                    ComputeEffectiveAccess = record.ComputeEffectiveAccess,
                    IncludeSharePermissions = record.IncludeSharePermissions,
                    IncludeFiles = record.IncludeFiles,
                    ReadOwnerAndSacl = record.ReadOwnerAndSacl,
                    CompareBaseline = record.CompareBaseline,
                    ScanAllDepths = record.ScanAllDepths,
                    MaxDepth = record.MaxDepth,
                    ExpandGroups = record.ExpandGroups,
                    UsePowerShell = record.UsePowerShell
                };
            }

            return null;
        }

        private Dictionary<string, FolderFlagsPayload> BuildFolderFlags(Dictionary<string, FolderDetail> details)
        {
            var payload = new Dictionary<string, FolderFlagsPayload>(StringComparer.OrdinalIgnoreCase);
            if (details == null) return payload;
            foreach (var entry in details)
            {
                var baselineAdded = entry.Value.BaselineSummary == null ? new List<AclDiffKey>() : entry.Value.BaselineSummary.Added;
                var baselineRemoved = entry.Value.BaselineSummary == null ? new List<AclDiffKey>() : entry.Value.BaselineSummary.Removed;
                payload[entry.Key] = new FolderFlagsPayload
                {
                    HasExplicitPermissions = entry.Value.HasExplicitPermissions,
                    HasExplicitNtfs = entry.Value.HasExplicitNtfs,
                    HasExplicitShare = entry.Value.HasExplicitShare,
                    IsInheritanceDisabled = entry.Value.IsInheritanceDisabled,
                    HasFileEntries = entry.Value.HasFileEntries,
                    HasFolderEntries = entry.Value.HasFolderEntries,
                    HasHighRiskEntries = entry.Value.HasHighRiskEntries,
                    HasMediumRiskEntries = entry.Value.HasMediumRiskEntries,
                    HasLowRiskEntries = entry.Value.HasLowRiskEntries,
                    HasShareEntries = entry.Value.HasShareEntries,
                    HasEffectiveEntries = entry.Value.HasEffectiveEntries,
                    DiffSummary = entry.Value.DiffSummary,
                    BaselineAdded = baselineAdded,
                    BaselineRemoved = baselineRemoved
                };
            }
            return payload;
        }

        private Dictionary<string, FolderFlagsPayload> LoadFolderFlags(string flagsPath)
        {
            if (!File.Exists(flagsPath)) return new Dictionary<string, FolderFlagsPayload>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var flags = JsonConvert.DeserializeObject<Dictionary<string, FolderFlagsPayload>>(File.ReadAllText(flagsPath));
                return flags == null
                    ? new Dictionary<string, FolderFlagsPayload>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, FolderFlagsPayload>(flags, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, FolderFlagsPayload>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private void ApplyFolderFlags(Dictionary<string, FolderDetail> details, Dictionary<string, FolderFlagsPayload> flags)
        {
            if (flags == null || flags.Count == 0) return;
            foreach (var entry in flags)
            {
                if (string.IsNullOrWhiteSpace(entry.Key)) continue;
                FolderDetail detail;
                if (!details.TryGetValue(entry.Key, out detail))
                {
                    detail = new FolderDetail();
                    details[entry.Key] = detail;
                }
                detail.HasExplicitPermissions = detail.HasExplicitPermissions || entry.Value.HasExplicitPermissions;
                detail.HasExplicitNtfs = detail.HasExplicitNtfs || entry.Value.HasExplicitNtfs;
                detail.HasExplicitShare = detail.HasExplicitShare || entry.Value.HasExplicitShare;
                detail.IsInheritanceDisabled = detail.IsInheritanceDisabled || entry.Value.IsInheritanceDisabled;
                detail.HasFileEntries = detail.HasFileEntries || entry.Value.HasFileEntries;
                detail.HasFolderEntries = detail.HasFolderEntries || entry.Value.HasFolderEntries;
                detail.HasHighRiskEntries = detail.HasHighRiskEntries || entry.Value.HasHighRiskEntries;
                detail.HasMediumRiskEntries = detail.HasMediumRiskEntries || entry.Value.HasMediumRiskEntries;
                detail.HasLowRiskEntries = detail.HasLowRiskEntries || entry.Value.HasLowRiskEntries;
                detail.HasShareEntries = detail.HasShareEntries || entry.Value.HasShareEntries;
                detail.HasEffectiveEntries = detail.HasEffectiveEntries || entry.Value.HasEffectiveEntries;
                if (entry.Value.DiffSummary != null)
                {
                    detail.DiffSummary = entry.Value.DiffSummary;
                }
                if ((entry.Value.BaselineAdded != null && entry.Value.BaselineAdded.Count > 0) ||
                    (entry.Value.BaselineRemoved != null && entry.Value.BaselineRemoved.Count > 0))
                {
                    detail.BaselineSummary = new AclDiffSummary();
                    detail.BaselineSummary.Added.AddRange(entry.Value.BaselineAdded ?? new List<AclDiffKey>());
                    detail.BaselineSummary.Removed.AddRange(entry.Value.BaselineRemoved ?? new List<AclDiffKey>());
                }
            }
        }

        private string BuildEntryKey(ExportRecord record)
        {
            var principalKey = string.IsNullOrWhiteSpace(record.PrincipalSid) ? record.PrincipalName : record.PrincipalSid;
            var membersKey = record.MemberNames == null ? string.Empty : string.Join(",", record.MemberNames);
            return string.Format("{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}|{8}|{9}|{10}|{11}|{12}|{13}|{14}|{15}|{16}|{17}|{18}|{19}|{20}|{21}|{22}|{23}|{24}|{25}|{26}|{27}|{28}|{29}|{30}|{31}",
                principalKey ?? string.Empty,
                record.PrincipalType ?? string.Empty,
                record.PermissionLayer.ToString(),
                record.AllowDeny ?? string.Empty,
                record.RightsSummary ?? string.Empty,
                record.RightsMask,
                record.EffectiveRightsSummary ?? string.Empty,
                record.EffectiveRightsMask,
                record.ShareRightsMask,
                record.NtfsRightsMask,
                record.IsInherited,
                record.AppliesToThisFolder,
                record.AppliesToSubfolders,
                record.AppliesToFiles,
                record.InheritanceFlags ?? string.Empty,
                record.PropagationFlags ?? string.Empty,
                record.Source ?? string.Empty,
                record.Depth,
                record.ResourceType ?? string.Empty,
                NormalizeExportPath(record.TargetPath) ?? string.Empty,
                record.Owner ?? string.Empty,
                record.ShareName ?? string.Empty,
                record.ShareServer ?? string.Empty,
                record.AuditSummary ?? string.Empty,
                record.RiskLevel ?? string.Empty,
                record.IsDisabled,
                record.IsServiceAccount,
                record.IsAdminAccount,
                membersKey,
                record.HasExplicitPermissions,
                record.IsInheritanceDisabled,
                record.PathKind);
        }

        private Dictionary<string, List<string>> BuildTreeFromExport(string dataPath, string rootPath)
        {
            var treeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(dataPath)) return treeMap;

            var normalizedRoot = NormalizeTreePath(rootPath);
            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(dataPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                ExportRecord record;
                try
                {
                    record = JsonConvert.DeserializeObject<ExportRecord>(line);
                }
                catch
                {
                    continue;
                }
                if (record == null || string.IsNullOrWhiteSpace(record.FolderPath)) continue;
                var folderPath = NormalizeExportPath(record.FolderPath);
                if (string.IsNullOrWhiteSpace(folderPath)) continue;
                if (string.Equals(record.PrincipalType, "Meta", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(record.PrincipalName, "SCAN_OPTIONS", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                if (!IsWithinRoot(NormalizeTreePath(folderPath), normalizedRoot))
                {
                    continue;
                }
                folders.Add(folderPath);
            }

            var parentCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var toProcess = folders.ToList();
            foreach (var folder in toProcess)
            {
                var parent = SafeGetParent(folder, parentCache);
                while (!string.IsNullOrWhiteSpace(parent))
                {
                    var normalizedParent = NormalizeTreePath(parent);
                    if (!IsWithinRoot(normalizedParent, normalizedRoot))
                    {
                        break;
                    }
                    if (!folders.Add(parent))
                    {
                        break;
                    }
                    if (!string.IsNullOrWhiteSpace(normalizedRoot)
                        && string.Equals(normalizedParent, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                    parent = SafeGetParent(parent, parentCache);
                }
            }

            if (!string.IsNullOrWhiteSpace(rootPath))
            {
                folders.Add(rootPath);
            }

            var treeSets = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var folder in folders)
            {
                if (!treeSets.ContainsKey(folder))
                {
                    treeSets[folder] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                }
            }

            foreach (var folder in folders)
            {
                var parent = SafeGetParent(folder, parentCache);
                if (parent != null)
                {
                    var normalizedParent = NormalizeTreePath(parent);
                    if (!IsWithinRoot(normalizedParent, normalizedRoot))
                    {
                        continue;
                    }
                    HashSet<string> children;
                    if (!treeSets.TryGetValue(parent, out children))
                    {
                        children = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        treeSets[parent] = children;
                    }
                    children.Add(folder);
                }
            }

            foreach (var entry in treeSets)
            {
                treeMap[entry.Key] = entry.Value.ToList();
            }

            return treeMap;
        }


        private static DateTime NormalizeImportedTimestamp(DateTime importedTimestamp, string archivePath)
        {
            if (importedTimestamp != default(DateTime))
            {
                return importedTimestamp.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(importedTimestamp, DateTimeKind.Utc)
                    : importedTimestamp.ToUniversalTime();
            }

            try
            {
                var fallback = File.GetLastWriteTimeUtc(archivePath);
                return fallback == default(DateTime) ? DateTime.UtcNow : fallback;
            }
            catch
            {
                return DateTime.UtcNow;
            }
        }

        private static string NormalizeExportPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            return PathResolver.FromExtendedPath(path).Replace('/', '\\').Trim();
        }

        private static string NormalizeTreePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            var normalized = PathResolver.FromExtendedPath(path);
            return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsWithinRoot(string candidate, string root)
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

        private string SafeGetParent(string path, Dictionary<string, string> cache)
        {
            if (cache != null && cache.TryGetValue(path, out var cachedParent))
            {
                return cachedParent;
            }

            string parentValue;
            try
            {
                var parent = Directory.GetParent(path);
                parentValue = parent == null ? null : parent.FullName;
            }
            catch
            {
                parentValue = null;
            }

            if (cache != null)
            {
                cache[path] = parentValue;
            }

            return parentValue;
        }

        private bool AddFileEntry(ZipArchive archive, string entryName, string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) return false;
            var ioPath = PathResolver.ToExtendedPath(sourcePath);
            if (!File.Exists(ioPath)) return false;
            archive.CreateEntryFromFile(ioPath, entryName);
            return true;
        }

        private void AddJsonEntry(ZipArchive archive, string entryName, object data)
        {
            var entry = archive.CreateEntry(entryName);
            using (var stream = entry.Open())
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(JsonConvert.SerializeObject(data));
            }
        }

        private void ExtractEntry(ZipArchive archive, string entryName, string destinationDir)
        {
            var entry = archive.GetEntry(entryName);
            if (entry == null) return;
            var destinationPath = Path.Combine(destinationDir, entryName);
            var ioDestinationPath = PathResolver.ToExtendedPath(destinationPath);
            entry.ExtractToFile(ioDestinationPath, true);
        }

        private void AddEmptyEntry(ZipArchive archive, string entryName)
        {
            var entry = archive.CreateEntry(entryName);
            using (var stream = entry.Open())
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(string.Empty);
            }
        }


        private static string EnsureArchiveOutputPath(string outputPath)
        {
            var normalized = PathResolver.FromExtendedPath(outputPath).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new ArgumentException("Output path required", "outputPath");
            }

            if (!normalized.EndsWith(ArchiveFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                normalized += ArchiveFileExtension;
            }

            return normalized;
        }

        private static string EnsureArchiveInputPath(string archivePath)
        {
            var normalized = PathResolver.FromExtendedPath(archivePath).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                throw new ArgumentException("Archive path required", "archivePath");
            }

            if (File.Exists(PathResolver.ToExtendedPath(normalized)))
            {
                return normalized;
            }

            if (!normalized.EndsWith(ArchiveFileExtension, StringComparison.OrdinalIgnoreCase))
            {
                var withExtension = normalized + ArchiveFileExtension;
                if (File.Exists(PathResolver.ToExtendedPath(withExtension)))
                {
                    return withExtension;
                }
            }

            return normalized;
        }


        private static void CleanupAnalysisWorkspace(string leafDirectory, TimeSpan retention)
        {
            if (retention <= TimeSpan.Zero)
            {
                return;
            }

            try
            {
                var workspace = Path.Combine(RuntimePaths.GetTempRoot(), leafDirectory);
                if (!Directory.Exists(workspace))
                {
                    return;
                }

                var thresholdUtc = DateTime.UtcNow.Subtract(retention);
                foreach (var directory in Directory.GetDirectories(workspace))
                {
                    try
                    {
                        var info = new DirectoryInfo(directory);
                        var timestamp = info.LastWriteTimeUtc;
                        if (timestamp <= thresholdUtc)
                        {
                            Directory.Delete(directory, true);
                        }
                    }
                    catch
                    {
                    }
                }

                foreach (var file in Directory.GetFiles(workspace))
                {
                    try
                    {
                        var info = new FileInfo(file);
                        var timestamp = info.LastWriteTimeUtc;
                        if (timestamp <= thresholdUtc)
                        {
                            File.Delete(file);
                        }
                    }
                    catch
                    {
                    }
                }

            }
            catch
            {
            }
        }

        private static string GetAnalysisWorkspace(string leafDirectory)
        {
            var workspace = Path.Combine(RuntimePaths.GetTempRoot(), leafDirectory);
            Directory.CreateDirectory(workspace);
            return workspace;
        }

        private string EnsureSqlitePayload(ScanResult result, string exportWorkspace)
        {
            if (result != null
                && !string.IsNullOrWhiteSpace(result.SqliteDatabasePath)
                && File.Exists(PathResolver.ToExtendedPath(result.SqliteDatabasePath)))
            {
                return result.SqliteDatabasePath;
            }

            var sqlitePath = Path.Combine(exportWorkspace, string.Format("analysis_{0}.sqlite", Guid.NewGuid().ToString("N")));
            new AnalysisSqliteStore().CreateDatabase(sqlitePath, result.TempDataPath, result.ErrorPath, result.Details);
            result.SqliteDatabasePath = sqlitePath;
            result.UsesSqliteBackend = true;
            return sqlitePath;
        }

        private Dictionary<string, FolderDetail> BuildDetailsFromFolderFlags(Dictionary<string, List<string>> treeMap, Dictionary<string, FolderFlagsPayload> flags)
        {
            var details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase);
            if (treeMap != null)
            {
                foreach (var path in treeMap.Keys.Where(key => !string.IsNullOrWhiteSpace(key)))
                {
                    details[path] = new FolderDetail { EntriesLoaded = false };
                }
            }

            ApplyFolderFlags(details, flags);
            return details;
        }

        private class ArchiveMeta
        {
            public string RootPath { get; set; }
            public PathKind RootPathKind { get; set; }
            public DateTime CreatedAt { get; set; }
            public int Version { get; set; }
            public ScanOptions ScanOptions { get; set; }
            public int DataRecordCount { get; set; }
            public int ErrorRecordCount { get; set; }
        }

        private class FolderFlagsPayload
        {
            public bool HasExplicitPermissions { get; set; }
            public bool HasExplicitNtfs { get; set; }
            public bool HasExplicitShare { get; set; }
            public bool IsInheritanceDisabled { get; set; }
            public bool HasFileEntries { get; set; }
            public bool HasFolderEntries { get; set; }
            public bool HasHighRiskEntries { get; set; }
            public bool HasMediumRiskEntries { get; set; }
            public bool HasLowRiskEntries { get; set; }
            public bool HasShareEntries { get; set; }
            public bool HasEffectiveEntries { get; set; }
            public AclDiffSummary DiffSummary { get; set; }
            public List<AclDiffKey> BaselineAdded { get; set; }
            public List<AclDiffKey> BaselineRemoved { get; set; }
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
