using NtfsAudit.Core.Logging;
using NtfsAudit.Core.Export;
using NtfsAudit.Core.Cache;
using NtfsAudit.Core.Models;
using NtfsAudit.Core.Services;
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
using System.Threading.Tasks;

namespace NtfsAudit.Viewer
{
    public static class ViewerStartupCoordinator
    {
        public static string ResolveArchivePath(string[] args, Func<string, bool> fileExists = null)
        {
            if (args == null || args.Length == 0)
            {
                return null;
            }

            var archivePath = args[0];
            if (string.IsNullOrWhiteSpace(archivePath))
            {
                return null;
            }

            var exists = fileExists ?? File.Exists;
            if (exists(archivePath))
            {
                return archivePath;
            }

            if (!archivePath.EndsWith(".ntaudit", StringComparison.OrdinalIgnoreCase))
            {
                var withExtension = archivePath + ".ntaudit";
                if (exists(withExtension))
                {
                    return withExtension;
                }
            }

            return null;
        }

        public static Task TryImportFromArgsAsync(string[] args, Func<string, Task> importArchiveAsync, Func<string, bool> fileExists = null)
        {
            if (importArchiveAsync == null)
            {
                return Task.CompletedTask;
            }

            var archivePath = ResolveArchivePath(args, fileExists);
            return string.IsNullOrWhiteSpace(archivePath)
                ? Task.CompletedTask
                : importArchiveAsync(archivePath);
        }
    }
}
