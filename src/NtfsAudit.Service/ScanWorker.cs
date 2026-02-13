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
                    RunSingleScan(options, token);
                }

                File.Delete(file);
            }
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
            var name = Path.GetFileName(options.RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(name)) name = "scan";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            var output = Path.Combine(options.OutputDirectory, string.Format("{0}_{1}.ntaudit", name, DateTime.Now.ToString("yyyyMMdd_HHmmss")));
            archive.Export(result, options.RootPath, output);
        }
    }
}
