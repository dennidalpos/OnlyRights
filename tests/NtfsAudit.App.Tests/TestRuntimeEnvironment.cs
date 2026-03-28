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
using System.Runtime.CompilerServices;

namespace NtfsAudit.App.Tests
{
    internal static class TestRuntimeEnvironment
    {
        private static readonly string RuntimeRoot = Path.Combine(
            Path.GetTempPath(),
            "NtfsAudit.Tests",
            "runtime",
            Guid.NewGuid().ToString("N"));

        [ModuleInitializer]
        internal static void Initialize()
        {
            Environment.SetEnvironmentVariable("NTFSAUDIT_TEMP_ROOT", Path.Combine(RuntimeRoot, "temp", "NtfsAudit"));
            Environment.SetEnvironmentVariable("NTFSAUDIT_LOCAL_CACHE_ROOT", Path.Combine(RuntimeRoot, "local-cache"));
            Environment.SetEnvironmentVariable("NTFSAUDIT_COMMON_DATA_ROOT", Path.Combine(RuntimeRoot, "common-data"));

            AppDomain.CurrentDomain.ProcessExit += (_, __) =>
            {
                try
                {
                    if (Directory.Exists(RuntimeRoot))
                    {
                        Directory.Delete(RuntimeRoot, true);
                    }
                }
                catch
                {
                }
            };
        }
    }
}
