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
using NtfsAudit.App.Services;

namespace NtfsAudit.App.Cache
{
    public class LocalCacheStore
    {
        public string GetCacheFilePath(string fileName)
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            var localPath = Path.Combine(exeDir, fileName);
            if (IsWritableDirectory(exeDir))
            {
                return localPath;
            }

            var cacheRoot = RuntimePaths.GetLocalCacheRoot();
            Directory.CreateDirectory(cacheRoot);
            return Path.Combine(cacheRoot, fileName);
        }

        private bool IsWritableDirectory(string directory)
        {
            try
            {
                var testPath = Path.Combine(directory, string.Format(".write_{0}", Guid.NewGuid().ToString("N")));
                File.WriteAllText(testPath, "x");
                File.Delete(testPath);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
