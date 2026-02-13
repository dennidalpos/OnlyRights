using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Newtonsoft.Json;
using NtfsAudit.App.Cache;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;

namespace NtfsAudit.Service
{
    public class ScanWorker : BackgroundService
    {
        private static readonly string ServiceDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NtfsAudit");
        private static readonly string JobsRoot = Path.Combine(ServiceDataRoot, "jobs");
        private static readonly string StatusPath = Path.Combine(ServiceDataRoot, "service-status.json");
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    ProcessJobs(stoppingToken);
                }
                catch
                {
                }

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        private void ProcessJobs(CancellationToken token)
        {
            if (!Directory.Exists(JobsRoot))
            {
                WriteServiceStatus(new ServiceRuntimeStatus
                {
                    IsRunning = false,
                    PendingJobs = 0,
                    LastUpdateUtc = DateTime.UtcNow,
                    LastMessage = "In attesa di job"
                });
                return;
            }

            var files = Directory.GetFiles(JobsRoot, "job_*.json").OrderBy(path => path).ToArray();
            WriteServiceStatus(new ServiceRuntimeStatus
            {
                IsRunning = false,
                PendingJobs = files.Length,
                LastUpdateUtc = DateTime.UtcNow,
                LastMessage = files.Length > 0 ? "Job in coda" : "In attesa di job"
            });

            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                ServiceScanJob job;
                try
                {
                    job = JsonConvert.DeserializeObject<ServiceScanJob>(File.ReadAllText(file));
                }
                catch
                {
                    continue;
                }
                if (job == null || job.ScanOptions == null || job.ScanOptions.Count == 0)
                {
                    continue;
                }

                var optionsList = job.ScanOptions.Where(option => option != null && !string.IsNullOrWhiteSpace(option.RootPath)).ToList();
                if (optionsList.Count == 0)
                {
                    continue;
                }

                var startedAt = DateTime.UtcNow;
                for (var index = 0; index < optionsList.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var options = optionsList[index];
                    WriteServiceStatus(new ServiceRuntimeStatus
                    {
                        IsRunning = true,
                        CurrentJobId = job.JobId,
                        CurrentRootPath = options.RootPath,
                        CurrentRootIndex = index + 1,
                        TotalRoots = optionsList.Count,
                        PendingJobs = Math.Max(0, files.Length - 1),
                        StartedAtUtc = startedAt,
                        LastUpdateUtc = DateTime.UtcNow,
                        LastMessage = string.Format("Scansione root {0}/{1}", index + 1, optionsList.Count)
                    });

                    try
                    {
                        RunSingleScan(options, token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                    }
                }

                File.Delete(file);
                var pending = Directory.Exists(JobsRoot)
                    ? Directory.GetFiles(JobsRoot, "job_*.json").Length
                    : 0;
                WriteServiceStatus(new ServiceRuntimeStatus
                {
                    IsRunning = false,
                    PendingJobs = pending,
                    LastUpdateUtc = DateTime.UtcNow,
                    LastMessage = pending > 0 ? "Job completato, altri job in coda" : "Ultimo job completato"
                });
            }
        }

        private static void WriteServiceStatus(ServiceRuntimeStatus status)
        {
            try
            {
                if (status == null) return;
                Directory.CreateDirectory(ServiceDataRoot);
                File.WriteAllText(StatusPath, JsonConvert.SerializeObject(status, Formatting.Indented));
            }
            catch
            {
            }
        }

        private static string BuildScanNameFromRoot(string root)
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

            if (string.IsNullOrWhiteSpace(name)) name = "scan";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }

        private void RunSingleScan(ScanOptions options, CancellationToken token)
        {
            var sidCache = new SidNameCache();
            var cacheStore = new LocalCacheStore();
            sidCache.Load(cacheStore.GetCacheFilePath("sid-cache.json"));
            var groupCache = new GroupMembershipCache(TimeSpan.FromHours(2));
            var adResolver = new DirectoryServicesResolver();
            var identityResolver = new IdentityResolver(sidCache, adResolver);
            var groupExpansion = new GroupExpansionService(adResolver, groupCache);
            var scanService = new ScanService(identityResolver, groupExpansion);
            var result = scanService.Run(options, null, token);

            if (string.IsNullOrWhiteSpace(options.OutputDirectory)) return;
            Directory.CreateDirectory(options.OutputDirectory);
            var archive = new AnalysisArchive();
            var name = BuildScanNameFromRoot(options.RootPath);
            var output = Path.Combine(options.OutputDirectory, string.Format("{0}_{1}.ntaudit", name, DateTime.Now.ToString("yyyy_MM_dd_HH_mm")));
            archive.Export(result, options.RootPath, output);
        }
    }
}
