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
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    internal sealed class ServiceRuntimeStatusPresenter
    {
        public ServiceRuntimeViewState Build(bool isServiceInstalled, bool isServiceRunning, ServiceRuntimeStatus status)
        {
            if (!isServiceInstalled)
            {
                return new ServiceRuntimeViewState
                {
                    BadgeText = LocalizationManager.Text("Service.NotInstalledBadge"),
                    BadgeBackground = "#FF9E9E9E",
                    StatusText = LocalizationManager.Text("Service.NotInstalledStatus"),
                    IsServiceRuntimeRunning = false
                };
            }

            if (status == null)
            {
                return new ServiceRuntimeViewState
                {
                    BadgeText = isServiceRunning ? LocalizationManager.Text("Service.ActiveBadge") : LocalizationManager.Text("Service.InstalledBadge"),
                    BadgeBackground = isServiceRunning ? "#FF2E7D32" : "#FF1565C0",
                    StatusText = isServiceRunning
                        ? LocalizationManager.Text("Service.StartedNoDetails")
                        : LocalizationManager.Text("Service.InstalledStopped"),
                    IsServiceRuntimeRunning = false
                };
            }

            var queuedJobs = Math.Max(0, status.PendingJobs);
            var queuedRoots = Math.Max(0, status.RemainingRootsInCurrentJob);
            var queueText = LocalizationManager.Format("Service.QueueText", queuedJobs + queuedRoots);

            if (status.IsRunning)
            {
                var rootLabel = string.IsNullOrWhiteSpace(status.CurrentRootPath) ? LocalizationManager.Text("Service.UnknownRoot") : status.CurrentRootPath;
                var progress = status.TotalRoots > 0
                    ? string.Format("{0}/{1}", status.CurrentRootIndex, status.TotalRoots)
                    : "?/?";

                return new ServiceRuntimeViewState
                {
                    BadgeText = LocalizationManager.Text("Service.ActiveBadge"),
                    BadgeBackground = "#FF2E7D32",
                    StatusText = LocalizationManager.Format("Service.RunningStatus", rootLabel, progress, queueText),
                    IsServiceRuntimeRunning = true
                };
            }

            return new ServiceRuntimeViewState
            {
                BadgeText = isServiceRunning ? LocalizationManager.Text("Service.ActiveBadge") : LocalizationManager.Text("Service.InstalledBadge"),
                BadgeBackground = isServiceRunning ? "#FF2E7D32" : "#FF1565C0",
                StatusText = string.IsNullOrWhiteSpace(status.LastMessage)
                    ? LocalizationManager.Format("Service.Waiting", queueText)
                    : LocalizationManager.Format("Service.WithMessage", status.LastMessage, queueText),
                IsServiceRuntimeRunning = false
            };
        }
    }

    internal sealed class ServiceRuntimeViewState
    {
        public string BadgeText { get; set; }
        public string BadgeBackground { get; set; }
        public string StatusText { get; set; }
        public bool IsServiceRuntimeRunning { get; set; }
    }
}
