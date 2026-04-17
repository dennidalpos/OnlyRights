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
    public sealed class ServiceScheduleDefinition
    {
        public string ScheduleId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public ServiceScheduleFrequencyKind FrequencyKind { get; set; }
        public DateTime? OneShotLocalDateTime { get; set; }
        public TimeSpan TimeOfDay { get; set; }
        public DayOfWeek? DayOfWeek { get; set; }
        public int? DayOfMonth { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public ServiceScheduledScanTemplate Template { get; set; } = new ServiceScheduledScanTemplate();

        public ServiceScheduleDefinition Clone()
        {
            return new ServiceScheduleDefinition
            {
                ScheduleId = ScheduleId,
                Name = Name,
                IsEnabled = IsEnabled,
                FrequencyKind = FrequencyKind,
                OneShotLocalDateTime = OneShotLocalDateTime,
                TimeOfDay = TimeOfDay,
                DayOfWeek = DayOfWeek,
                DayOfMonth = DayOfMonth,
                CreatedAtUtc = CreatedAtUtc,
                UpdatedAtUtc = UpdatedAtUtc,
                Template = Template == null ? new ServiceScheduledScanTemplate() : Template.Clone()
            };
        }
    }
}
