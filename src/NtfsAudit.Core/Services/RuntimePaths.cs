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
        private const string TempRootOverrideEnvVar = "NTFSAUDIT_TEMP_ROOT";
        private const string LocalCacheRootOverrideEnvVar = "NTFSAUDIT_LOCAL_CACHE_ROOT";
        private const string CommonDataRootOverrideEnvVar = "NTFSAUDIT_COMMON_DATA_ROOT";
        internal const string ProductDirectoryName = "NtfsAudit";
        internal const string ImportsDirectoryName = "imports";
        internal const string ExportsDirectoryName = "exports";

        internal static string GetTempRoot()
        {
            var overrideRoot = GetConfiguredRoot(TempRootOverrideEnvVar);
            if (!string.IsNullOrWhiteSpace(overrideRoot))
            {
                return overrideRoot;
            }

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
            var overrideRoot = GetConfiguredRoot(LocalCacheRootOverrideEnvVar);
            if (!string.IsNullOrWhiteSpace(overrideRoot))
            {
                return overrideRoot;
            }

            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), ProductDirectoryName, "Cache");
        }

        internal static string GetCommonDataRoot()
        {
            var overrideRoot = GetConfiguredRoot(CommonDataRootOverrideEnvVar);
            if (!string.IsNullOrWhiteSpace(overrideRoot))
            {
                return overrideRoot;
            }

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
            if (!string.IsNullOrWhiteSpace(GetConfiguredRoot(TempRootOverrideEnvVar)))
            {
                return null;
            }

            var windowsPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrWhiteSpace(windowsPath))
            {
                return null;
            }

            return Path.Combine(windowsPath, "SystemTemp", ProductDirectoryName);
        }

        private static string GetConfiguredRoot(string environmentVariable)
        {
            var value = Environment.GetEnvironmentVariable(environmentVariable);
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            return Path.GetFullPath(value.Trim());
        }
    }
}
