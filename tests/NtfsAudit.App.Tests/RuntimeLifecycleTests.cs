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
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;
using Newtonsoft.Json;
using NtfsAudit.Service;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class RuntimeLifecycleTests
    {
        [Fact]
        public void CleanupTempRootPreservingAnalysisWorkspaces_KeepsImportsAndExports()
        {
            var tempRoot = RuntimePaths.GetTempRoot();
            var importsRoot = RuntimePaths.GetAnalysisImportsRoot();
            var exportsRoot = RuntimePaths.GetAnalysisExportsRoot();
            var staleDirectory = Path.Combine(tempRoot, "stale-" + Guid.NewGuid().ToString("N"));
            var staleFile = Path.Combine(tempRoot, "stale-" + Guid.NewGuid().ToString("N") + ".tmp");

            RuntimeCleanupService.TryDeleteDirectory(tempRoot);
            Directory.CreateDirectory(importsRoot);
            Directory.CreateDirectory(exportsRoot);
            Directory.CreateDirectory(staleDirectory);
            Directory.CreateDirectory(tempRoot);
            File.WriteAllText(staleFile, "temp");

            try
            {
                var cleanupService = new RuntimeCleanupService();

                var removedEntries = cleanupService.CleanupTempRootPreservingAnalysisWorkspaces();

                Assert.Equal(2, removedEntries);
                Assert.True(Directory.Exists(importsRoot));
                Assert.True(Directory.Exists(exportsRoot));
                Assert.False(Directory.Exists(staleDirectory));
                Assert.False(File.Exists(staleFile));
            }
            finally
            {
                RuntimeCleanupService.TryDeleteDirectory(staleDirectory);
                RuntimeCleanupService.TryDeleteFile(staleFile);
            }
        }

        [Fact]
        public void TryDeleteFile_CollectsDiagnostic_WhenDeletionFails()
        {
            var tempRoot = RuntimePaths.GetTempRoot();
            Directory.CreateDirectory(tempRoot);
            var lockedFile = Path.Combine(tempRoot, "locked-" + Guid.NewGuid().ToString("N") + ".tmp");
            File.WriteAllText(lockedFile, "temp");
            var diagnostics = new List<string>();

            try
            {
                using (new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var removedEntries = RuntimeCleanupService.TryDeleteFile(lockedFile, diagnostics);

                    Assert.Equal(0, removedEntries);
                    Assert.Single(diagnostics);
                    Assert.Contains("Impossibile rimuovere file runtime", diagnostics[0], StringComparison.Ordinal);
                }
            }
            finally
            {
                RuntimeCleanupService.TryDeleteFile(lockedFile);
            }
        }

        [Fact]
        public void ServiceRuntimeStatusPresenter_FormatsRunningQueueState()
        {
            var presenter = new ServiceRuntimeStatusPresenter();

            var state = presenter.Build(
                isServiceInstalled: true,
                isServiceRunning: true,
                status: new ServiceRuntimeStatus
                {
                    IsRunning = true,
                    CurrentRootPath = @"C:\data",
                    CurrentRootIndex = 2,
                    TotalRoots = 3,
                    PendingJobs = 1,
                    RemainingRootsInCurrentJob = 4
                });

            Assert.True(state.IsServiceRuntimeRunning);
            Assert.Equal("Servizio attivo", state.BadgeText);
            Assert.Contains(@"C:\data", state.StatusText, StringComparison.Ordinal);
            Assert.Contains("2/3", state.StatusText, StringComparison.Ordinal);
            Assert.Contains("code scansioni: 5", state.StatusText, StringComparison.Ordinal);
        }

        [Fact]
        public void SingleInstanceCoordinator_RejectsSecondMutexAcquisition()
        {
            var mutexName = "Local\\NtfsAudit.Tests." + Guid.NewGuid().ToString("N");
            Mutex firstMutex = null;
            Mutex secondMutex = null;

            try
            {
                var firstAttempt = SingleInstanceCoordinator.TryAcquire(mutexName, out firstMutex);
                Assert.True(firstAttempt.IsAcquired);
                Assert.NotNull(firstMutex);

                var secondAttempt = SingleInstanceCoordinator.TryAcquire(mutexName, out secondMutex);
                Assert.False(secondAttempt.IsAcquired);
                Assert.True(secondAttempt.IsAlreadyRunning);
                Assert.Null(secondMutex);
            }
            finally
            {
                if (firstMutex != null)
                {
                    firstMutex.ReleaseMutex();
                    firstMutex.Dispose();
                }
            }
        }

        [Fact]
        public void SingleInstanceCoordinator_ReturnsFailureWhenMutexCreationThrows()
        {
            var result = SingleInstanceCoordinator.TryAcquire(
                "Local\\NtfsAudit.Tests." + Guid.NewGuid().ToString("N"),
                out var mutex,
                _ => throw new InvalidOperationException("mutex boom"));

            Assert.False(result.IsAcquired);
            Assert.False(result.IsAlreadyRunning);
            Assert.Null(mutex);
            Assert.Contains("mutex boom", result.ErrorMessage, StringComparison.Ordinal);
        }

        [Fact]
        public void ServiceJobFileHandler_QuarantinesInvalidJobFile()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "NtfsAudit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var jobPath = Path.Combine(tempRoot, "job_invalid.json");
                File.WriteAllText(jobPath, "{ invalid json");
                var handler = new ServiceJobFileHandler();

                var loaded = handler.TryLoad(jobPath, out var job, out var options, out var reason);
                var quarantinedPath = handler.Quarantine(jobPath, reason);

                Assert.False(loaded);
                Assert.Null(job);
                Assert.Null(options);
                Assert.False(string.IsNullOrWhiteSpace(reason));
                Assert.False(File.Exists(jobPath));
                Assert.True(File.Exists(quarantinedPath));
                Assert.True(File.Exists(quarantinedPath + ".txt"));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Fact]
        public void ScanWorker_ProcessJobs_ProcessesQueuedRootsAndTracksRunningState()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "NtfsAudit.Tests", Guid.NewGuid().ToString("N"));
            var jobsRoot = Path.Combine(tempRoot, "jobs");
            var statusPath = Path.Combine(tempRoot, "service-status.json");
            Directory.CreateDirectory(jobsRoot);

            try
            {
                var jobFile = Path.Combine(jobsRoot, "job_valid.json");
                File.WriteAllText(jobFile, JsonConvert.SerializeObject(new ServiceScanJob
                {
                    JobId = "job-valid",
                    CreatedAtUtc = DateTime.UtcNow,
                    ScanOptions = new List<ScanOptions>
                    {
                        new ScanOptions { RootPath = @"C:\data-a" },
                        new ScanOptions { RootPath = @"C:\data-b" }
                    }
                }));

                var executedRoots = new List<string>();
                var statuses = new List<ServiceRuntimeStatus>();
                var worker = CreateScanWorker(
                    tempRoot,
                    jobsRoot,
                    statusPath,
                    statuses,
                    (options, _) => executedRoots.Add(options.RootPath));

                worker.ProcessJobs(CancellationToken.None);

                Assert.Equal(new[] { @"C:\data-a", @"C:\data-b" }, executedRoots);
                Assert.Empty(Directory.GetFiles(jobsRoot, "job_*.json"));

                var runningStatuses = statuses.Where(status => status.IsRunning).ToList();
                Assert.Equal(2, runningStatuses.Count);
                Assert.Equal(@"C:\data-a", runningStatuses[0].CurrentRootPath);
                Assert.Equal(1, runningStatuses[0].RemainingRootsInCurrentJob);
                Assert.Equal(@"C:\data-b", runningStatuses[1].CurrentRootPath);
                Assert.Equal(0, runningStatuses[1].RemainingRootsInCurrentJob);

                var finalStatus = statuses.Last();
                Assert.False(finalStatus.IsRunning);
                Assert.Equal("Ultimo job completato", finalStatus.LastMessage);
                Assert.Equal(0, finalStatus.PendingJobs);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Fact]
        public void ScanWorker_ProcessJobs_QuarantinesInvalidJobAndPublishesStatus()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "NtfsAudit.Tests", Guid.NewGuid().ToString("N"));
            var jobsRoot = Path.Combine(tempRoot, "jobs");
            var statusPath = Path.Combine(tempRoot, "service-status.json");
            Directory.CreateDirectory(jobsRoot);

            try
            {
                var invalidJobPath = Path.Combine(jobsRoot, "job_invalid.json");
                File.WriteAllText(invalidJobPath, "{ invalid json");

                var statuses = new List<ServiceRuntimeStatus>();
                var worker = CreateScanWorker(tempRoot, jobsRoot, statusPath, statuses, (_, __) => { });

                worker.ProcessJobs(CancellationToken.None);

                Assert.False(File.Exists(invalidJobPath));
                var quarantinedFiles = Directory.GetFiles(Path.Combine(jobsRoot, "invalid"), "job_invalid_*.json");
                Assert.Single(quarantinedFiles);
                Assert.True(File.Exists(quarantinedFiles[0] + ".txt"));
                Assert.Contains(statuses, status => !status.IsRunning && status.LastMessage.IndexOf("Job non valido isolato", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Fact]
        public void ScanWorker_ProcessJobs_ReportsRootFailuresAndContinuesNextRoot()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "NtfsAudit.Tests", Guid.NewGuid().ToString("N"));
            var jobsRoot = Path.Combine(tempRoot, "jobs");
            var statusPath = Path.Combine(tempRoot, "service-status.json");
            Directory.CreateDirectory(jobsRoot);

            try
            {
                var jobFile = Path.Combine(jobsRoot, "job_failure.json");
                File.WriteAllText(jobFile, JsonConvert.SerializeObject(new ServiceScanJob
                {
                    JobId = "job-failure",
                    CreatedAtUtc = DateTime.UtcNow,
                    ScanOptions = new List<ScanOptions>
                    {
                        new ScanOptions { RootPath = @"C:\broken-root" },
                        new ScanOptions { RootPath = @"C:\healthy-root" }
                    }
                }));

                var executedRoots = new List<string>();
                var statuses = new List<ServiceRuntimeStatus>();
                var worker = CreateScanWorker(
                    tempRoot,
                    jobsRoot,
                    statusPath,
                    statuses,
                    (options, _) =>
                    {
                        executedRoots.Add(options.RootPath);
                        if (string.Equals(options.RootPath, @"C:\broken-root", StringComparison.Ordinal))
                        {
                            throw new IOException("scan boom");
                        }
                    });

                worker.ProcessJobs(CancellationToken.None);

                Assert.Equal(new[] { @"C:\broken-root", @"C:\healthy-root" }, executedRoots);
                Assert.Contains(statuses, status => status.LastMessage.IndexOf("Errore servizio (scansione root 1/2): scan boom", StringComparison.OrdinalIgnoreCase) >= 0);
                Assert.Contains("1 errore di scansione", statuses.Last().LastMessage, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        private static ScanWorker CreateScanWorker(
            string serviceDataRoot,
            string jobsRoot,
            string statusPath,
            List<ServiceRuntimeStatus> statuses,
            Action<ScanOptions, CancellationToken> scanExecutor)
        {
            return new ScanWorker(
                new ServiceJobFileHandler(),
                serviceDataRoot,
                jobsRoot,
                statusPath,
                status => statuses.Add(CloneStatus(status)),
                scanExecutor);
        }

        private static ServiceRuntimeStatus CloneStatus(ServiceRuntimeStatus status)
        {
            return new ServiceRuntimeStatus
            {
                IsRunning = status.IsRunning,
                CurrentJobId = status.CurrentJobId,
                CurrentRootPath = status.CurrentRootPath,
                CurrentRootIndex = status.CurrentRootIndex,
                TotalRoots = status.TotalRoots,
                PendingJobs = status.PendingJobs,
                RemainingRootsInCurrentJob = status.RemainingRootsInCurrentJob,
                StartedAtUtc = status.StartedAtUtc,
                LastUpdateUtc = status.LastUpdateUtc,
                LastMessage = status.LastMessage
            };
        }
    }
}
