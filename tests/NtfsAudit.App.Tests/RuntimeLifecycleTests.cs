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
using System.Threading;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;
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
                Assert.True(SingleInstanceCoordinator.TryAcquire(mutexName, out firstMutex));
                Assert.NotNull(firstMutex);
                Assert.False(SingleInstanceCoordinator.TryAcquire(mutexName, out secondMutex));
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
    }
}
