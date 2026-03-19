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

namespace NtfsAudit.App.Services
{
    internal static class RuntimePaths
    {
        internal const string ProductDirectoryName = "NtfsAudit";
        internal const string ImportsDirectoryName = "imports";
        internal const string ExportsDirectoryName = "exports";

        internal static string GetTempRoot()
        {
            return Path.Combine(Path.GetTempPath(), ProductDirectoryName);
        }

        internal static string GetAnalysisImportsRoot()
        {
            return Path.Combine(GetTempRoot(), ImportsDirectoryName);
        }

        internal static string GetAnalysisExportsRoot()
        {
            return Path.Combine(GetTempRoot(), ExportsDirectoryName);
        }

        internal static string GetLocalCacheRoot()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductDirectoryName, "Cache");
        }

        internal static string GetCommonDataRoot()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ProductDirectoryName);
        }

        internal static string GetJobsRoot()
        {
            return Path.Combine(GetCommonDataRoot(), "jobs");
        }

        internal static string GetServiceStatusPath()
        {
            return Path.Combine(GetCommonDataRoot(), "service-status.json");
        }

        internal static string GetWindowsSystemTempRoot()
        {
            var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrWhiteSpace(windowsPath))
            {
                return null;
            }

            return Path.Combine(windowsPath, "SystemTemp", ProductDirectoryName);
        }
    }
}
