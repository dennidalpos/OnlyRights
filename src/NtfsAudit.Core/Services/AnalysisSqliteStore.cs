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
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using NtfsAudit.Core.Export;
using NtfsAudit.Core.Models;

namespace NtfsAudit.Core.Services
{
    public class AnalysisSqliteStore
    {
        public void CreateDatabase(string databasePath, string dataPath, string errorPath, Dictionary<string, FolderDetail> details)
        {
            if (string.IsNullOrWhiteSpace(databasePath)) throw new ArgumentException("Database path required", "databasePath");
            if (string.IsNullOrWhiteSpace(dataPath)) throw new ArgumentException("Data path required", "dataPath");

            var ioDatabasePath = PathResolver.ToExtendedPath(databasePath);
            var ioDataPath = PathResolver.ToExtendedPath(dataPath);
            if (!File.Exists(ioDataPath)) throw new FileNotFoundException("Scan data file not found.", dataPath);

            var directory = Path.GetDirectoryName(ioDatabasePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(ioDatabasePath))
            {
                File.Delete(ioDatabasePath);
            }

            using (var connection = OpenReadWrite(databasePath))
            {
                ExecuteNonQuery(connection, null, "PRAGMA journal_mode = DELETE;");
                ExecuteNonQuery(connection, null, "PRAGMA synchronous = NORMAL;");

                using (var transaction = connection.BeginTransaction())
                {
                    ExecuteNonQuery(connection, transaction, "CREATE TABLE acl_entries (folder_path TEXT NOT NULL, json_payload TEXT NOT NULL);");
                    ExecuteNonQuery(connection, transaction, "CREATE TABLE folder_summaries (folder_path TEXT PRIMARY KEY, summary_json TEXT NOT NULL);");
                    ExecuteNonQuery(connection, transaction, "CREATE TABLE errors (row_number INTEGER NOT NULL, error_text TEXT NOT NULL);");
                    ExecuteNonQuery(connection, transaction, "CREATE INDEX idx_acl_entries_folder_path ON acl_entries(folder_path);");

                    var summaries = new Dictionary<string, FolderSummarySnapshot>(StringComparer.OrdinalIgnoreCase);
                    using (var insertEntry = connection.CreateCommand())
                    {
                        insertEntry.Transaction = transaction;
                        insertEntry.CommandText = "INSERT INTO acl_entries(folder_path, json_payload) VALUES ($folder_path, $json_payload);";
                        var folderPathParameter = insertEntry.CreateParameter();
                        folderPathParameter.ParameterName = "$folder_path";
                        insertEntry.Parameters.Add(folderPathParameter);
                        var payloadParameter = insertEntry.CreateParameter();
                        payloadParameter.ParameterName = "$json_payload";
                        insertEntry.Parameters.Add(payloadParameter);

                        foreach (var line in File.ReadLines(ioDataPath))
                        {
                            if (string.IsNullOrWhiteSpace(line))
                            {
                                continue;
                            }

                            ExportRecord record;
                            try
                            {
                                record = JsonConvert.DeserializeObject<ExportRecord>(line);
                            }
                            catch
                            {
                                continue;
                            }

                            if (record == null
                                || string.Equals(record.PrincipalType, "Meta", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(record.PrincipalName, "SCAN_OPTIONS", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            var folderPath = NormalizePath(record.FolderPath);
                            if (string.IsNullOrWhiteSpace(folderPath))
                            {
                                continue;
                            }

                            var entry = ConvertToAceEntry(record, folderPath);
                            folderPathParameter.Value = folderPath;
                            payloadParameter.Value = JsonConvert.SerializeObject(entry);
                            insertEntry.ExecuteNonQuery();
                            UpdateSummary(summaries, entry);
                        }
                    }

                    MergeFolderFlags(summaries, details);
                    using (var insertSummary = connection.CreateCommand())
                    {
                        insertSummary.Transaction = transaction;
                        insertSummary.CommandText = "INSERT INTO folder_summaries(folder_path, summary_json) VALUES ($folder_path, $summary_json);";
                        var folderPathParameter = insertSummary.CreateParameter();
                        folderPathParameter.ParameterName = "$folder_path";
                        insertSummary.Parameters.Add(folderPathParameter);
                        var payloadParameter = insertSummary.CreateParameter();
                        payloadParameter.ParameterName = "$summary_json";
                        insertSummary.Parameters.Add(payloadParameter);

                        foreach (var summary in summaries)
                        {
                            folderPathParameter.Value = summary.Key;
                            payloadParameter.Value = JsonConvert.SerializeObject(summary.Value);
                            insertSummary.ExecuteNonQuery();
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(errorPath))
                    {
                        var ioErrorPath = PathResolver.ToExtendedPath(errorPath);
                        if (File.Exists(ioErrorPath))
                        {
                            using (var insertError = connection.CreateCommand())
                            {
                                insertError.Transaction = transaction;
                                insertError.CommandText = "INSERT INTO errors(row_number, error_text) VALUES ($row_number, $error_text);";
                                var rowParameter = insertError.CreateParameter();
                                rowParameter.ParameterName = "$row_number";
                                insertError.Parameters.Add(rowParameter);
                                var errorTextParameter = insertError.CreateParameter();
                                errorTextParameter.ParameterName = "$error_text";
                                insertError.Parameters.Add(errorTextParameter);
                                var rowNumber = 0;
                                foreach (var line in File.ReadLines(ioErrorPath))
                                {
                                    rowNumber++;
                                    if (string.IsNullOrWhiteSpace(line))
                                    {
                                        continue;
                                    }

                                    rowParameter.Value = rowNumber;
                                    errorTextParameter.Value = line;
                                    insertError.ExecuteNonQuery();
                                }
                            }
                        }
                    }

                    transaction.Commit();
                }
            }
        }

        public FolderDetail LoadFolderDetail(string databasePath, string folderPath)
        {
            var detail = new FolderDetail();
            ApplySummary(detail, LoadFolderSummary(databasePath, folderPath));
            detail.EntriesLoaded = true;

            using (var connection = OpenReadOnly(databasePath))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT json_payload FROM acl_entries WHERE folder_path = $folder_path ORDER BY rowid;";
                command.Parameters.AddWithValue("$folder_path", NormalizePath(folderPath));
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var payload = reader.IsDBNull(0) ? null : reader.GetString(0);
                        if (string.IsNullOrWhiteSpace(payload))
                        {
                            continue;
                        }

                        AceEntry entry;
                        try
                        {
                            entry = JsonConvert.DeserializeObject<AceEntry>(payload);
                        }
                        catch
                        {
                            continue;
                        }

                        if (entry == null)
                        {
                            continue;
                        }

                        detail.AllEntries.Add(entry);
                        if (entry.PermissionLayer == PermissionLayer.Share)
                        {
                            detail.ShareEntries.Add(entry);
                            detail.HasShareEntries = true;
                        }
                        else if (entry.PermissionLayer == PermissionLayer.Effective)
                        {
                            detail.EffectiveEntries.Add(entry);
                            detail.HasEffectiveEntries = true;
                        }

                        if (entry.PermissionLayer == PermissionLayer.Ntfs)
                        {
                            if (string.Equals(entry.PrincipalType, "Group", StringComparison.OrdinalIgnoreCase))
                            {
                                detail.GroupEntries.Add(entry);
                            }
                            else
                            {
                                detail.UserEntries.Add(entry);
                            }
                        }
                    }
                }
            }

            return detail;
        }

        public Dictionary<string, FolderDetail> LoadFolderDetailPlaceholders(string databasePath, Dictionary<string, FolderDetail> fallbackDetails)
        {
            var details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase);
            using (var connection = OpenReadOnly(databasePath))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT folder_path, summary_json FROM folder_summaries;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var folderPath = reader.IsDBNull(0) ? null : reader.GetString(0);
                        if (string.IsNullOrWhiteSpace(folderPath))
                        {
                            continue;
                        }

                        var detail = new FolderDetail { EntriesLoaded = false };
                        var payload = reader.IsDBNull(1) ? null : reader.GetString(1);
                        ApplySummary(detail, DeserializeSummary(payload));
                        details[folderPath] = detail;
                    }
                }
            }

            if (fallbackDetails != null)
            {
                foreach (var entry in fallbackDetails)
                {
                    if (string.IsNullOrWhiteSpace(entry.Key))
                    {
                        continue;
                    }

                    if (!details.ContainsKey(entry.Key))
                    {
                        details[entry.Key] = entry.Value ?? new FolderDetail { EntriesLoaded = false };
                    }
                }
            }

            return details;
        }

        public FolderSummarySnapshot LoadFolderSummary(string databasePath, string folderPath)
        {
            using (var connection = OpenReadOnly(databasePath))
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT summary_json FROM folder_summaries WHERE folder_path = $folder_path;";
                command.Parameters.AddWithValue("$folder_path", NormalizePath(folderPath));
                var payload = command.ExecuteScalar() as string;
                return DeserializeSummary(payload);
            }
        }

        private static void MergeFolderFlags(Dictionary<string, FolderSummarySnapshot> summaries, Dictionary<string, FolderDetail> details)
        {
            if (details == null)
            {
                return;
            }

            foreach (var entry in details)
            {
                if (string.IsNullOrWhiteSpace(entry.Key))
                {
                    continue;
                }

                if (!summaries.TryGetValue(entry.Key, out var summary))
                {
                    summary = new FolderSummarySnapshot();
                    summaries[entry.Key] = summary;
                }

                var detail = entry.Value ?? new FolderDetail();
                summary.HasExplicitPermissions = detail.HasExplicitPermissions;
                summary.HasExplicitNtfs = detail.HasExplicitNtfs;
                summary.HasExplicitShare = detail.HasExplicitShare;
                summary.IsInheritanceDisabled = detail.IsInheritanceDisabled;
                summary.HasFileEntries = detail.HasFileEntries;
                summary.HasFolderEntries = detail.HasFolderEntries;
                summary.HasHighRiskEntries = detail.HasHighRiskEntries;
                summary.HasMediumRiskEntries = detail.HasMediumRiskEntries;
                summary.HasLowRiskEntries = detail.HasLowRiskEntries;
                summary.HasShareEntries = detail.HasShareEntries || detail.ShareEntries.Count > 0;
                summary.HasEffectiveEntries = detail.HasEffectiveEntries || detail.EffectiveEntries.Count > 0;
                summary.DiffSummary = detail.DiffSummary;
                summary.BaselineSummary = detail.BaselineSummary;
            }
        }

        private static void UpdateSummary(Dictionary<string, FolderSummarySnapshot> summaries, AceEntry entry)
        {
            if (!summaries.TryGetValue(entry.FolderPath, out var summary))
            {
                summary = new FolderSummarySnapshot();
                summaries[entry.FolderPath] = summary;
            }

            summary.HasExplicitPermissions = summary.HasExplicitPermissions || entry.HasExplicitPermissions;
            summary.HasExplicitNtfs = summary.HasExplicitNtfs || (entry.PermissionLayer == PermissionLayer.Ntfs && entry.HasExplicitPermissions);
            summary.HasExplicitShare = summary.HasExplicitShare || entry.PermissionLayer == PermissionLayer.Share;
            summary.IsInheritanceDisabled = summary.IsInheritanceDisabled || entry.IsInheritanceDisabled;
            summary.HasFileEntries = summary.HasFileEntries || string.Equals(entry.ResourceType, "File", StringComparison.OrdinalIgnoreCase);
            summary.HasFolderEntries = summary.HasFolderEntries || !string.Equals(entry.ResourceType, "File", StringComparison.OrdinalIgnoreCase);
            summary.HasHighRiskEntries = summary.HasHighRiskEntries
                || string.Equals(entry.RiskLevel, "High", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.RiskLevel, "Alto", StringComparison.OrdinalIgnoreCase);
            summary.HasMediumRiskEntries = summary.HasMediumRiskEntries
                || string.Equals(entry.RiskLevel, "Medium", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.RiskLevel, "Medio", StringComparison.OrdinalIgnoreCase);
            summary.HasLowRiskEntries = summary.HasLowRiskEntries
                || string.Equals(entry.RiskLevel, "Low", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.RiskLevel, "Basso", StringComparison.OrdinalIgnoreCase);
            summary.HasShareEntries = summary.HasShareEntries || entry.PermissionLayer == PermissionLayer.Share;
            summary.HasEffectiveEntries = summary.HasEffectiveEntries || entry.PermissionLayer == PermissionLayer.Effective;
        }

        private static AceEntry ConvertToAceEntry(ExportRecord record, string folderPath)
        {
            return new AceEntry
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
                TargetPath = NormalizePath(record.TargetPath),
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
        }

        private static void ApplySummary(FolderDetail detail, FolderSummarySnapshot summary)
        {
            if (detail == null || summary == null)
            {
                return;
            }

            detail.HasExplicitPermissions = summary.HasExplicitPermissions;
            detail.HasExplicitNtfs = summary.HasExplicitNtfs;
            detail.HasExplicitShare = summary.HasExplicitShare;
            detail.IsInheritanceDisabled = summary.IsInheritanceDisabled;
            detail.HasFileEntries = summary.HasFileEntries;
            detail.HasFolderEntries = summary.HasFolderEntries;
            detail.HasHighRiskEntries = summary.HasHighRiskEntries;
            detail.HasMediumRiskEntries = summary.HasMediumRiskEntries;
            detail.HasLowRiskEntries = summary.HasLowRiskEntries;
            detail.HasShareEntries = summary.HasShareEntries;
            detail.HasEffectiveEntries = summary.HasEffectiveEntries;
            detail.DiffSummary = summary.DiffSummary;
            detail.BaselineSummary = summary.BaselineSummary;
        }

        private static FolderSummarySnapshot DeserializeSummary(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<FolderSummarySnapshot>(payload);
            }
            catch
            {
                return null;
            }
        }

        private static SqliteConnection OpenReadWrite(string databasePath)
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = PathResolver.FromExtendedPath(databasePath),
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            }.ToString());
            connection.Open();
            return connection;
        }

        private static SqliteConnection OpenReadOnly(string databasePath)
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = PathResolver.FromExtendedPath(databasePath),
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            }.ToString());
            connection.Open();
            return connection;
        }

        private static void ExecuteNonQuery(SqliteConnection connection, SqliteTransaction transaction, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                if (transaction != null)
                {
                    command.Transaction = transaction;
                }
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }

        private static string NormalizePath(string path)
        {
            return string.IsNullOrWhiteSpace(path)
                ? string.Empty
                : PathResolver.FromExtendedPath(path).Replace('/', '\\').Trim();
        }
    }

    public class FolderSummarySnapshot
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
        public AclDiffSummary BaselineSummary { get; set; }
    }
}
