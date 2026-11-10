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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace NtfsAudit.Core.Services
{
    internal sealed class RuntimeCleanupService
    {
        public RuntimeCleanupResult CleanupOperationalData()
        {
            var removedEntries = 0;
            var diagnostics = new List<string>();
            removedEntries += CleanupTempRootPreservingAnalysisWorkspaces(diagnostics);
            removedEntries += TryDeleteDirectory(RuntimePaths.GetWindowsSystemTempRoot(), diagnostics);
            removedEntries += TryDeleteDirectory(RuntimePaths.GetLocalCacheRoot(), diagnostics);
            removedEntries += TryDeleteDirectory(RuntimePaths.GetJobsRoot(), diagnostics);
            removedEntries += TryDeleteFile(RuntimePaths.GetServiceStatusPath(), diagnostics);

            Directory.CreateDirectory(RuntimePaths.GetTempRoot());

            return new RuntimeCleanupResult
            {
                RemovedEntries = removedEntries,
                TempRootPath = RuntimePaths.GetTempRoot(),
                Diagnostics = new ReadOnlyCollection<string>(diagnostics)
            };
        }

        internal int CleanupTempRootPreservingAnalysisWorkspaces()
        {
            return CleanupTempRootPreservingAnalysisWorkspaces(null);
        }

        internal int CleanupTempRootPreservingAnalysisWorkspaces(ICollection<string> diagnostics)
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

                removedEntries += TryDeleteDirectory(fullPath, diagnostics);
            }

            foreach (var file in Directory.GetFiles(tempRoot))
            {
                removedEntries += TryDeleteFile(file, diagnostics);
            }

            return removedEntries;
        }

        internal static int TryDeleteDirectory(string directoryPath)
        {
            return TryDeleteDirectory(directoryPath, null);
        }

        internal static int TryDeleteDirectory(string directoryPath, ICollection<string> diagnostics)
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
            catch (Exception ex)
            {
                AddDiagnostic(diagnostics, string.Format("Unable to remove runtime directory: {0}. Details: {1}", directoryPath, ex.Message));
                return 0;
            }
        }

        internal static int TryDeleteFile(string path)
        {
            return TryDeleteFile(path, null);
        }

        internal static int TryDeleteFile(string path, ICollection<string> diagnostics)
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
            catch (Exception ex)
            {
                AddDiagnostic(diagnostics, string.Format("Unable to remove runtime file: {0}. Details: {1}", path, ex.Message));
                return 0;
            }
        }

        private static void AddDiagnostic(ICollection<string> diagnostics, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            Debug.WriteLine("[RuntimeCleanup] " + message);
            diagnostics?.Add(message);
        }
    }

    internal sealed class RuntimeCleanupResult
    {
        public int RemovedEntries { get; set; }
        public string TempRootPath { get; set; }
        public IReadOnlyCollection<string> Diagnostics { get; set; }
    }
}
