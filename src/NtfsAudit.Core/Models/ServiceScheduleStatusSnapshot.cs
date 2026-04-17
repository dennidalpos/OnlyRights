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

namespace NtfsAudit.App.Models
{
    public sealed class ServiceScheduleStatusSnapshot
    {
        public string ScheduleId { get; set; } = string.Empty;
        public DateTime? LastEnqueuedRunLocal { get; set; }
        public DateTime? LastJobCreatedAtUtc { get; set; }
        public DateTime? NextRunLocal { get; set; }
        public string? LastMessage { get; set; }
        public string? LastJobId { get; set; }
        public DateTime UpdatedAtUtc { get; set; }

        public ServiceScheduleStatusSnapshot Clone()
        {
            return new ServiceScheduleStatusSnapshot
            {
                ScheduleId = ScheduleId,
                LastEnqueuedRunLocal = LastEnqueuedRunLocal,
                LastJobCreatedAtUtc = LastJobCreatedAtUtc,
                NextRunLocal = NextRunLocal,
                LastMessage = LastMessage,
                LastJobId = LastJobId,
                UpdatedAtUtc = UpdatedAtUtc
            };
        }
    }
}
