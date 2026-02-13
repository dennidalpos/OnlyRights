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
            var jobsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NtfsAudit", "jobs");
            if (!Directory.Exists(jobsRoot)) return;

            var files = Directory.GetFiles(jobsRoot, "job_*.json").OrderBy(path => path).ToArray();
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

                foreach (var options in job.ScanOptions.Where(option => option != null && !string.IsNullOrWhiteSpace(option.RootPath)))
                {
                    token.ThrowIfCancellationRequested();
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
