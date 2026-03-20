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
        private static readonly string DefaultServiceDataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NtfsAudit");
        private readonly ServiceJobFileHandler _jobFileHandler;
        private readonly string _serviceDataRoot;
        private readonly string _jobsRoot;
        private readonly string _statusPath;
        private readonly Action<ServiceRuntimeStatus> _statusWriter;
        private readonly Action<ScanOptions, CancellationToken> _scanExecutor;

        public ScanWorker()
            : this(
                null,
                DefaultServiceDataRoot,
                Path.Combine(DefaultServiceDataRoot, "jobs"),
                Path.Combine(DefaultServiceDataRoot, "service-status.json"),
                null,
                null)
        {
        }

        internal ScanWorker(
            ServiceJobFileHandler jobFileHandler,
            string serviceDataRoot,
            string jobsRoot,
            string statusPath,
            Action<ServiceRuntimeStatus> statusWriter,
            Action<ScanOptions, CancellationToken> scanExecutor)
        {
            _jobFileHandler = jobFileHandler ?? new ServiceJobFileHandler();
            _serviceDataRoot = string.IsNullOrWhiteSpace(serviceDataRoot) ? DefaultServiceDataRoot : serviceDataRoot;
            _jobsRoot = string.IsNullOrWhiteSpace(jobsRoot) ? Path.Combine(_serviceDataRoot, "jobs") : jobsRoot;
            _statusPath = string.IsNullOrWhiteSpace(statusPath) ? Path.Combine(_serviceDataRoot, "service-status.json") : statusPath;
            _statusWriter = statusWriter ?? PersistServiceStatus;
            _scanExecutor = scanExecutor ?? RunSingleScan;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    ProcessJobs(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    ReportWorkerError("loop principale", ex, 0, null, null, 0, 0, 0);
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        internal void ProcessJobs(CancellationToken token)
        {
            if (!Directory.Exists(_jobsRoot))
            {
                UpdateServiceStatus(new ServiceRuntimeStatus
                {
                    IsRunning = false,
                    PendingJobs = 0,
                    RemainingRootsInCurrentJob = 0,
                    LastUpdateUtc = DateTime.UtcNow,
                    LastMessage = "In attesa di job"
                });
                return;
            }

            var files = Directory.GetFiles(_jobsRoot, "job_*.json").OrderBy(path => path).ToArray();
            UpdateServiceStatus(new ServiceRuntimeStatus
            {
                IsRunning = false,
                PendingJobs = files.Length,
                RemainingRootsInCurrentJob = 0,
                LastUpdateUtc = DateTime.UtcNow,
                LastMessage = files.Length > 0 ? "Job in coda" : "In attesa di job"
            });

            for (var fileIndex = 0; fileIndex < files.Length; fileIndex++)
            {
                var file = files[fileIndex];
                token.ThrowIfCancellationRequested();
                if (!_jobFileHandler.TryLoad(file, out var job, out var optionsList, out var failureReason))
                {
                    QuarantineInvalidJob(file, failureReason, files.Length, fileIndex);
                    continue;
                }

                var startedAt = DateTime.UtcNow;
                var failedRoots = 0;
                for (var index = 0; index < optionsList.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var options = optionsList[index];
                    UpdateServiceStatus(new ServiceRuntimeStatus
                    {
                        IsRunning = true,
                        CurrentJobId = job.JobId,
                        CurrentRootPath = options.RootPath,
                        CurrentRootIndex = index + 1,
                        TotalRoots = optionsList.Count,
                        PendingJobs = Math.Max(0, files.Length - fileIndex - 1),
                        RemainingRootsInCurrentJob = Math.Max(0, optionsList.Count - (index + 1)),
                        StartedAtUtc = startedAt,
                        LastUpdateUtc = DateTime.UtcNow,
                        LastMessage = string.Format("Scansione root {0}/{1}", index + 1, optionsList.Count)
                    });

                    try
                    {
                        _scanExecutor(options, token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failedRoots++;
                        ReportWorkerError(
                            string.Format("scansione root {0}/{1}", index + 1, optionsList.Count),
                            ex,
                            Math.Max(0, files.Length - fileIndex - 1),
                            job.JobId,
                            options.RootPath,
                            index + 1,
                            optionsList.Count,
                            Math.Max(0, optionsList.Count - (index + 1)));
                    }
                }

                File.Delete(file);
                var pending = Directory.Exists(_jobsRoot)
                    ? Directory.GetFiles(_jobsRoot, "job_*.json").Length
                    : 0;
                UpdateServiceStatus(new ServiceRuntimeStatus
                {
                    IsRunning = false,
                    PendingJobs = pending,
                    RemainingRootsInCurrentJob = 0,
                    LastUpdateUtc = DateTime.UtcNow,
                    LastMessage = BuildCompletionMessage(pending, failedRoots)
                });
            }
        }

        private void QuarantineInvalidJob(string file, string failureReason, int pendingJobs, int fileIndex)
        {
            Exception quarantineError = null;
            var quarantined = false;

            try
            {
                _jobFileHandler.Quarantine(file, failureReason);
                quarantined = true;
            }
            catch (Exception ex)
            {
                quarantineError = ex;
                TryDeleteFile(file);
            }

            var pending = Directory.Exists(_jobsRoot)
                ? Directory.GetFiles(_jobsRoot, "job_*.json").Length
                : Math.Max(0, pendingJobs - fileIndex - 1);
            var lastMessage = quarantined
                ? string.Format("Job non valido isolato: {0}", failureReason ?? "errore sconosciuto")
                : string.Format(
                    "Job non valido rimosso dopo errore quarantena: {0} ({1})",
                    failureReason ?? "errore sconosciuto",
                    quarantineError == null ? "errore non disponibile" : quarantineError.Message);

            UpdateServiceStatus(new ServiceRuntimeStatus
            {
                IsRunning = false,
                PendingJobs = pending,
                RemainingRootsInCurrentJob = 0,
                LastUpdateUtc = DateTime.UtcNow,
                LastMessage = lastMessage
            });
        }

        private string BuildCompletionMessage(int pendingJobs, int failedRoots)
        {
            if (failedRoots > 0)
            {
                var errorSummary = failedRoots == 1
                    ? "1 errore di scansione"
                    : string.Format("{0} errori di scansione", failedRoots);
                return pendingJobs > 0
                    ? string.Format("Job completato con {0}, altri job in coda", errorSummary)
                    : string.Format("Ultimo job completato con {0}", errorSummary);
            }

            return pendingJobs > 0 ? "Job completato, altri job in coda" : "Ultimo job completato";
        }

        private void UpdateServiceStatus(ServiceRuntimeStatus status)
        {
            if (status == null)
            {
                return;
            }

            try
            {
                _statusWriter(status);
            }
            catch (Exception ex)
            {
                TryWriteConsoleError(string.Format("Errore aggiornamento stato servizio: {0}", ex));
            }
        }

        private void PersistServiceStatus(ServiceRuntimeStatus status)
        {
            Directory.CreateDirectory(_serviceDataRoot);
            File.WriteAllText(_statusPath, JsonConvert.SerializeObject(status, Formatting.Indented));
        }

        private void ReportWorkerError(
            string context,
            Exception exception,
            int pendingJobs,
            string jobId,
            string rootPath,
            int currentRootIndex,
            int totalRoots,
            int remainingRoots)
        {
            var message = string.Format(
                "Errore servizio ({0}): {1}",
                string.IsNullOrWhiteSpace(context) ? "contesto sconosciuto" : context,
                exception == null ? "errore non disponibile" : exception.Message);

            TryWriteConsoleError(exception == null ? message : string.Format("{0}{1}{2}", message, Environment.NewLine, exception));
            UpdateServiceStatus(new ServiceRuntimeStatus
            {
                IsRunning = false,
                CurrentJobId = jobId,
                CurrentRootPath = rootPath,
                CurrentRootIndex = currentRootIndex,
                TotalRoots = totalRoots,
                PendingJobs = Math.Max(0, pendingJobs),
                RemainingRootsInCurrentJob = Math.Max(0, remainingRoots),
                LastUpdateUtc = DateTime.UtcNow,
                LastMessage = message
            });
        }

        private static void TryWriteConsoleError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                Console.Error.WriteLine(message);
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

        private static void TryDeleteFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private void RunSingleScan(ScanOptions options, CancellationToken token)
        {
            var runtimeOptions = options == null ? null : options.Clone();
            if (runtimeOptions != null && runtimeOptions.Credential != null)
            {
                runtimeOptions.Credential = ScanCredentialProtector.ResolveForRuntime(runtimeOptions.Credential);
            }

            var sidCache = new SidNameCache();
            var cacheStore = new LocalCacheStore();
            sidCache.Load(cacheStore.GetCacheFilePath("sid-cache.json"));
            var groupCache = new GroupMembershipCache(TimeSpan.FromHours(2));
            var adResolver = new DirectoryServicesResolver(runtimeOptions == null ? null : runtimeOptions.Credential);
            var identityResolver = new IdentityResolver(sidCache, adResolver);
            var groupExpansion = new GroupExpansionService(adResolver, groupCache);
            var scanService = new ScanService(identityResolver, groupExpansion, new SharePermissionService(runtimeOptions == null ? null : runtimeOptions.Credential));
            var result = scanService.Run(runtimeOptions, null, token);
            try
            {
                if (string.IsNullOrWhiteSpace(runtimeOptions.OutputDirectory)) return;
                Directory.CreateDirectory(runtimeOptions.OutputDirectory);
                var archive = new AnalysisArchive();
                var name = BuildScanNameFromRoot(runtimeOptions.RootPath);
                var output = Path.Combine(runtimeOptions.OutputDirectory, string.Format("{0}_{1}.ntaudit", name, DateTime.Now.ToString("yyyy_MM_dd_HH_mm")));
                archive.Export(result, runtimeOptions.RootPath, output);
            }
            finally
            {
                TryDeleteFile(result == null ? null : result.TempDataPath);
                TryDeleteFile(result == null ? null : result.ErrorPath);
            }
        }
    }
}
