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
                writer.Write(JsonConvert.SerializeObject(data, new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Ignore
                }));
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
    }
}
