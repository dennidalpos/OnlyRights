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

#nullable enable

namespace NtfsAudit.Core.Services
{
    public static class ScanExportPathBuilder
    {
        public static string BuildScanNameFromRoot(string? root)
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

        public static string BuildExportFileName(string? rootPath, string extension)
        {
            return BuildExportFileName(rootPath, extension, DateTime.Now);
        }

        internal static string BuildExportFileName(string? rootPath, string extension, DateTime timestamp)
        {
            var baseName = BuildScanNameFromRoot(rootPath);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "Root";
            var normalizedExtension = NormalizeExtension(extension);
            return string.Format("{0}_{1}.{2}", baseName, timestamp.ToString("yyyy_MM_dd_HH_mm"), normalizedExtension);
        }

        public static string BuildUniqueArchivePath(string outputDirectory, string? rootPath, string extension)
        {
            return BuildUniqueArchivePath(outputDirectory, rootPath, extension, DateTime.Now);
        }

        internal static string BuildUniqueArchivePath(string outputDirectory, string? rootPath, string extension, DateTime timestamp)
        {
            if (string.IsNullOrWhiteSpace(outputDirectory)) throw new ArgumentException("Output directory is required.", nameof(outputDirectory));

            var fileName = BuildExportFileName(rootPath, extension, timestamp);
            var candidate = Path.Combine(outputDirectory, fileName);
            if (!File.Exists(PathResolver.ToExtendedPath(candidate)))
            {
                return candidate;
            }

            var baseName = Path.GetFileNameWithoutExtension(fileName);
            var normalizedExtension = NormalizeExtension(extension);
            for (var suffix = 2; suffix < int.MaxValue; suffix++)
            {
                candidate = Path.Combine(outputDirectory, string.Format("{0}_{1}.{2}", baseName, suffix, normalizedExtension));
                if (!File.Exists(PathResolver.ToExtendedPath(candidate)))
                {
                    return candidate;
                }
            }

            throw new IOException("No available archive filename was found.");
        }

        private static string SanitizeFileName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }

            return value;
        }

        private static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension)) throw new ArgumentException("File extension is required.", nameof(extension));
            return extension.Trim().TrimStart('.');
        }
    }
}
