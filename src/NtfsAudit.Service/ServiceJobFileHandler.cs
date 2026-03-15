using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NtfsAudit.App.Models;

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
                failureReason = string.Format("job corrotto: {0}", ex.Message);
                return false;
            }

            if (job == null)
            {
                failureReason = "job vuoto o non deserializzabile";
                return false;
            }

            runnableOptions = (job.ScanOptions ?? new List<ScanOptions>())
                .Where(option => option != null && !string.IsNullOrWhiteSpace(option.RootPath))
                .ToList();

            if (runnableOptions.Count == 0)
            {
                failureReason = "job senza root valide";
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
            File.WriteAllText(reasonFile, failureReason ?? "job non valido");
            return destinationFile;
        }
    }
}
