using NtfsAudit.Core.Logging;
using NtfsAudit.Core.Export;
using NtfsAudit.Core.Cache;
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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NtfsAudit.Core.Models;

namespace NtfsAudit.Service
{
    internal sealed class ServiceJobFileHandler
    {
        private const string InvalidJobsDirectoryName = "invalid";

        public bool TryLoad(string filePath, out ServiceScanJob job, out List<ScanOptions> runnableOptions, out string failureReason)
        {
            job = null;
            runnableOptions = null;
            failureReason = null;

            try
            {
                job = JsonConvert.DeserializeObject<ServiceScanJob>(File.ReadAllText(filePath));
            }
            catch (Exception ex)
            {
                failureReason = string.Format("corrupted job: {0}", ex.Message);
                return false;
            }

            if (job == null)
            {
                failureReason = "empty or non-deserializable job";
                return false;
            }

            runnableOptions = (job.ScanOptions ?? new List<ScanOptions>())
                .Where(option => option != null && !string.IsNullOrWhiteSpace(option.RootPath))
                .ToList();

            if (runnableOptions.Count == 0)
            {
                failureReason = "job without valid roots";
                return false;
            }

            return true;
        }

        public string Quarantine(string filePath, string failureReason)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            var jobsRoot = Path.GetDirectoryName(filePath);
            var invalidRoot = Path.Combine(jobsRoot ?? string.Empty, InvalidJobsDirectoryName);
            Directory.CreateDirectory(invalidRoot);

            var destinationFile = Path.Combine(
                invalidRoot,
                string.Format("{0}_{1}.json", Path.GetFileNameWithoutExtension(filePath), DateTime.UtcNow.ToString("yyyyMMddHHmmss")));
            File.Move(filePath, destinationFile);

            var reasonFile = destinationFile + ".txt";
            File.WriteAllText(reasonFile, failureReason ?? "invalid job");
            return destinationFile;
        }
    }
}
