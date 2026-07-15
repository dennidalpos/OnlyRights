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
        private readonly ServiceScheduleFileStore _scheduleStore;
        private readonly ServiceSchedulePlanner _schedulePlanner;
        private readonly Action<ServiceRuntimeStatus> _statusWriter;
        private readonly Action<ScanOptions, CancellationToken> _scanExecutor;

        public ScanWorker()
            : this(
                null,
                DefaultServiceDataRoot,
                Path.Combine(DefaultServiceDataRoot, "jobs"),
                Path.Combine(DefaultServiceDataRoot, "service-status.json"),
                null,
                null,
                null,
                null)
        {
        }

        internal ScanWorker(
            ServiceJobFileHandler jobFileHandler,
            string serviceDataRoot,
            string jobsRoot,
            string statusPath,
            ServiceScheduleFileStore scheduleStore,
            ServiceSchedulePlanner schedulePlanner,
            Action<ServiceRuntimeStatus> statusWriter,
            Action<ScanOptions, CancellationToken> scanExecutor)
        {
            _jobFileHandler = jobFileHandler ?? new ServiceJobFileHandler();
            _serviceDataRoot = string.IsNullOrWhiteSpace(serviceDataRoot) ? DefaultServiceDataRoot : serviceDataRoot;
            _jobsRoot = string.IsNullOrWhiteSpace(jobsRoot) ? Path.Combine(_serviceDataRoot, "jobs") : jobsRoot;
            _statusPath = string.IsNullOrWhiteSpace(statusPath) ? Path.Combine(_serviceDataRoot, "service-status.json") : statusPath;
            _scheduleStore = scheduleStore ?? new ServiceScheduleFileStore();
            _schedulePlanner = schedulePlanner ?? new ServiceSchedulePlanner();
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
                    ReportWorkerError("main loop", ex, 0, null, null, 0, 0, 0);
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
            var scheduleSummary = MaterializeScheduledJobs();
            if (!Directory.Exists(_jobsRoot))
            {
                UpdateServiceStatus(BuildStatus(new ServiceRuntimeStatus
                {
                    IsRunning = false,
                    PendingJobs = 0,
                    RemainingRootsInCurrentJob = 0,
                    LastUpdateUtc = DateTime.UtcNow,
                    LastMessage = "Waiting for jobs"
                }, scheduleSummary));
                return;
            }

            var files = Directory.GetFiles(_jobsRoot, "job_*.json").OrderBy(path => path).ToArray();
            UpdateServiceStatus(BuildStatus(new ServiceRuntimeStatus
            {
                IsRunning = false,
                PendingJobs = files.Length,
                RemainingRootsInCurrentJob = 0,
                LastUpdateUtc = DateTime.UtcNow,
                LastMessage = files.Length > 0 ? "Jobs queued" : "Waiting for jobs"
            }, scheduleSummary));

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
                    UpdateServiceStatus(BuildStatus(new ServiceRuntimeStatus
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
                        CurrentActivity = string.IsNullOrWhiteSpace(job.JobId) ? "Scan running" : string.Format("Running job {0}", job.JobId),
                        LastMessage = string.Format("Scanning root {0}/{1}", index + 1, optionsList.Count)
                    }, scheduleSummary));

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
                            string.Format("root scan {0}/{1}", index + 1, optionsList.Count),
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
                scheduleSummary = MaterializeScheduledJobs();
                UpdateServiceStatus(BuildStatus(new ServiceRuntimeStatus
                {
                    IsRunning = false,
                    PendingJobs = pending,
                    RemainingRootsInCurrentJob = 0,
                    LastUpdateUtc = DateTime.UtcNow,
                    LastMessage = BuildCompletionMessage(pending, failedRoots)
                }, scheduleSummary));
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
                ? string.Format("Invalid job quarantined: {0}", failureReason ?? "unknown error")
                : string.Format(
                    "Invalid job removed after quarantine failure: {0} ({1})",
                    failureReason ?? "unknown error",
                    quarantineError == null ? "error unavailable" : quarantineError.Message);

            UpdateServiceStatus(BuildStatus(new ServiceRuntimeStatus
            {
                IsRunning = false,
                PendingJobs = pending,
                RemainingRootsInCurrentJob = 0,
                LastUpdateUtc = DateTime.UtcNow,
                LastMessage = lastMessage
            }, MaterializeScheduledJobs()));
        }

        private string BuildCompletionMessage(int pendingJobs, int failedRoots)
        {
            if (failedRoots > 0)
            {
                var errorSummary = failedRoots == 1
                    ? "1 scan error"
                    : string.Format("{0} scan errors", failedRoots);
                return pendingJobs > 0
                    ? string.Format("Job completed with {0}; other jobs are queued", errorSummary)
                    : string.Format("Last job completed with {0}", errorSummary);
            }

            return pendingJobs > 0 ? "Job completed; other jobs are queued" : "Last job completed";
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
                TryWriteConsoleError(string.Format("Service status update error: {0}", ex));
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
                "Service error ({0}): {1}",
                string.IsNullOrWhiteSpace(context) ? "unknown context" : context,
                exception == null ? "error unavailable" : exception.Message);

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
            ApplyGlobalCredentialFallback(runtimeOptions);
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
                var output = ScanExportPathBuilder.BuildUniqueArchivePath(runtimeOptions.OutputDirectory, runtimeOptions.RootPath, "ntaudit");
                archive.Export(result, runtimeOptions.RootPath, output);
            }
            finally
            {
                TryDeleteFile(result == null ? null : result.TempDataPath);
                TryDeleteFile(result == null ? null : result.ErrorPath);
            }
        }

        private static void ApplyGlobalCredentialFallback(ScanOptions options)
        {
            if (options == null || options.Credential != null)
            {
                return;
            }

            var kind = PathResolver.DetectPathKind(options.RootPath);
            if (!ScanPathCompatibilityPolicy.SupportsConfiguredCredential(kind))
            {
                options.CredentialSource = "CurrentUser";
                return;
            }

            try
            {
                var settings = new ScanCredentialStore().Load();
                if (settings.GlobalCredential == null || !settings.GlobalCredential.IsConfigured)
                {
                    options.CredentialSource = "CurrentUser";
                    return;
                }

                options.Credential = settings.GlobalCredential.Clone();
                options.CredentialSource = "Global";
            }
            catch
            {
                options.CredentialSource = "CurrentUser";
            }
        }

        private ServiceRuntimeStatus BuildStatus(ServiceRuntimeStatus status, SchedulePollSummary scheduleSummary)
        {
            status = status ?? new ServiceRuntimeStatus();
            status.ScheduleDefinitionCount = scheduleSummary == null ? 0 : scheduleSummary.DefinitionCount;
            status.EnabledScheduleCount = scheduleSummary == null ? 0 : scheduleSummary.EnabledDefinitionCount;
            status.NextScheduledRunLocal = scheduleSummary == null ? null : scheduleSummary.NextRunLocal;
            if (string.IsNullOrWhiteSpace(status.CurrentActivity))
            {
                status.CurrentActivity = scheduleSummary == null || scheduleSummary.DefinitionCount == 0
                    ? "Service idle"
                    : string.Format("Scheduler active ({0} definitions)", scheduleSummary.EnabledDefinitionCount);
            }

            return status;
        }

        private SchedulePollSummary MaterializeScheduledJobs()
        {
            var definitions = _scheduleStore.LoadDefinitions();
            var runtimeSnapshot = _scheduleStore.LoadRuntimeSnapshot();
            var statuses = runtimeSnapshot.Schedules == null
                ? new List<ServiceScheduleStatusSnapshot>()
                : runtimeSnapshot.Schedules.Select(snapshot => snapshot == null ? null : snapshot.Clone()).Where(snapshot => snapshot != null).ToList();
            var nowLocal = DateTime.Now;

            foreach (var definition in definitions)
            {
                var snapshot = statuses.FirstOrDefault(item => string.Equals(item.ScheduleId, definition.ScheduleId, StringComparison.OrdinalIgnoreCase));
                if (snapshot == null)
                {
                    snapshot = new ServiceScheduleStatusSnapshot { ScheduleId = definition.ScheduleId };
                    statuses.Add(snapshot);
                }

                var evaluation = _schedulePlanner.EvaluateDueRun(definition, nowLocal, snapshot.LastEnqueuedRunLocal);
                snapshot.NextRunLocal = evaluation.NextRunLocal;

                if (!evaluation.IsDue || !evaluation.DueRunLocal.HasValue)
                {
                    snapshot.UpdatedAtUtc = DateTime.UtcNow;
                    if (string.IsNullOrWhiteSpace(snapshot.LastMessage))
                    {
                        snapshot.LastMessage = definition.IsEnabled ? "Waiting for the next run" : "Schedule disabled";
                    }
                    continue;
                }

                var dueRunLocal = evaluation.DueRunLocal.Value;
                var job = BuildScheduledJob(definition, dueRunLocal);
                Directory.CreateDirectory(_jobsRoot);
                var jobPath = Path.Combine(_jobsRoot, string.Format("job_{0}.json", job.JobId));
                File.WriteAllText(jobPath, JsonConvert.SerializeObject(job, Formatting.Indented));

                snapshot.LastEnqueuedRunLocal = dueRunLocal;
                snapshot.LastJobCreatedAtUtc = DateTime.UtcNow;
                snapshot.LastJobId = job.JobId;
                snapshot.LastMessage = string.Format("Job scheduled for {0}", dueRunLocal.ToString("g"));
                snapshot.NextRunLocal = evaluation.NextRunLocal;
                snapshot.UpdatedAtUtc = DateTime.UtcNow;
            }

            runtimeSnapshot.Schedules = statuses;
            _scheduleStore.SaveRuntimeSnapshot(runtimeSnapshot);

            return new SchedulePollSummary
            {
                DefinitionCount = definitions.Count,
                EnabledDefinitionCount = definitions.Count(definition => definition.IsEnabled),
                NextRunLocal = statuses
                    .Where(snapshot => snapshot.NextRunLocal.HasValue)
                    .Select(snapshot => snapshot.NextRunLocal.Value)
                    .OrderBy(value => value)
                    .Cast<DateTime?>()
                    .FirstOrDefault()
            };
        }

        private static ServiceScanJob BuildScheduledJob(ServiceScheduleDefinition definition, DateTime dueRunLocal)
        {
            var template = definition.Template == null ? new ServiceScheduledScanTemplate() : definition.Template.Clone();
            var options = (template.Roots ?? new List<string>())
                .Where(root => !string.IsNullOrWhiteSpace(root))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(root => new ScanOptions
                {
                    RootPath = root,
                    OutputDirectory = template.OutputDirectory,
                    MaxDepth = template.MaxDepth,
                    ScanAllDepths = template.ScanAllDepths,
                    IncludeInherited = template.IncludeInherited,
                    ResolveIdentities = template.ResolveIdentities,
                    ExcludeServiceAccounts = template.ResolveIdentities && template.ExcludeServiceAccounts,
                    ExcludeAdminAccounts = template.ResolveIdentities && template.ExcludeAdminAccounts,
                    ExpandGroups = template.ResolveIdentities && template.ExpandGroups,
                    UsePowerShell = template.ResolveIdentities && template.UsePowerShell,
                    EnableAdvancedAudit = template.EnableAdvancedAudit,
                    ComputeEffectiveAccess = template.EnableAdvancedAudit && template.ComputeEffectiveAccess,
                    IncludeSharePermissions = template.EnableAdvancedAudit && template.IncludeSharePermissions,
                    IncludeFiles = template.EnableAdvancedAudit && template.IncludeFiles,
                    ReadOwnerAndSacl = template.EnableAdvancedAudit && template.ReadOwnerAndSacl,
                    CompareBaseline = template.EnableAdvancedAudit && template.CompareBaseline,
                    AnonymizeIdentities = template.AnonymizeIdentities
                })
                .ToList();

            return new ServiceScanJob
            {
                JobId = string.Format("schedule_{0}_{1}", definition.ScheduleId, dueRunLocal.ToString("yyyyMMddHHmmss")),
                CreatedAtUtc = DateTime.UtcNow,
                ScanOptions = options
            };
        }

        private sealed class SchedulePollSummary
        {
            public int DefinitionCount { get; set; }
            public int EnabledDefinitionCount { get; set; }
            public DateTime? NextRunLocal { get; set; }
        }
    }
}
