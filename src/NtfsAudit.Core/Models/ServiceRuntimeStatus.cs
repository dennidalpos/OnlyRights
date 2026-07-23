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

#nullable enable

namespace NtfsAudit.Core.Models
{
    public class ServiceRuntimeStatus
    {
        public bool IsRunning { get; set; }
        public string? CurrentJobId { get; set; }
        public string? CurrentRootPath { get; set; }
        public int CurrentRootIndex { get; set; }
        public int TotalRoots { get; set; }
        public int PendingJobs { get; set; }
        public int RemainingRootsInCurrentJob { get; set; }
        public int ScheduleDefinitionCount { get; set; }
        public int EnabledScheduleCount { get; set; }
        public DateTime? NextScheduledRunLocal { get; set; }
        public string? CurrentActivity { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime LastUpdateUtc { get; set; }
        public string? LastMessage { get; set; }
    }
}
