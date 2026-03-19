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

namespace NtfsAudit.App.Services
{
    internal sealed class RuntimeCleanupService
    {
        public RuntimeCleanupResult CleanupOperationalData()
        {
            var removedEntries = 0;
            removedEntries += CleanupTempRootPreservingAnalysisWorkspaces();
            removedEntries += TryDeleteDirectory(RuntimePaths.GetWindowsSystemTempRoot());
            removedEntries += TryDeleteDirectory(RuntimePaths.GetLocalCacheRoot());
            removedEntries += TryDeleteDirectory(RuntimePaths.GetJobsRoot());
            removedEntries += TryDeleteFile(RuntimePaths.GetServiceStatusPath());

            Directory.CreateDirectory(RuntimePaths.GetTempRoot());

            return new RuntimeCleanupResult
            {
                RemovedEntries = removedEntries,
                TempRootPath = RuntimePaths.GetTempRoot()
            };
        }

        internal int CleanupTempRootPreservingAnalysisWorkspaces()
        {
            var tempRoot = RuntimePaths.GetTempRoot();
            if (!Directory.Exists(tempRoot))
            {
                return 0;
            }

            var preservedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                Path.GetFullPath(RuntimePaths.GetAnalysisImportsRoot()),
                Path.GetFullPath(RuntimePaths.GetAnalysisExportsRoot())
            };

            var removedEntries = 0;
            foreach (var directory in Directory.GetDirectories(tempRoot))
            {
                var fullPath = Path.GetFullPath(directory);
                if (preservedDirectories.Contains(fullPath))
                {
                    continue;
                }

                removedEntries += TryDeleteDirectory(fullPath);
            }

            foreach (var file in Directory.GetFiles(tempRoot))
            {
                removedEntries += TryDeleteFile(file);
            }

            return removedEntries;
        }

        internal static int TryDeleteDirectory(string directoryPath)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return 0;
            }

            try
            {
                Directory.Delete(directoryPath, true);
                return 1;
            }
            catch
            {
                return 0;
            }
        }

        internal static int TryDeleteFile(string path)
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

            try
            {
                File.Delete(ioPath);
                return 1;
            }
            catch
            {
                return 0;
            }
        }
    }

    internal sealed class RuntimeCleanupResult
    {
        public int RemovedEntries { get; set; }
        public string TempRootPath { get; set; }
    }
}
