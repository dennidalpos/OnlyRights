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
using NtfsAudit.App.Models;

#nullable enable

namespace NtfsAudit.App.ViewModels
{
    public sealed class ServiceScheduleItemViewModel
    {
        public ServiceScheduleDefinition Definition { get; set; } = new ServiceScheduleDefinition();
        public ServiceScheduleStatusSnapshot? Runtime { get; set; }
        public string FrequencyLabel { get; set; } = string.Empty;
        public string NextRunText { get; set; } = string.Empty;
        public string RootsSummary { get; set; } = string.Empty;
        public string LastMessage { get; set; } = string.Empty;
        public string StatusBadgeText { get; set; } = string.Empty;
        public string StatusBadgeBackground { get; set; } = "#FF90A4AE";
    }
}
