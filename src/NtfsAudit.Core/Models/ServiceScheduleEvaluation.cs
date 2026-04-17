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
    public sealed class ServiceScheduleEvaluation
    {
        public bool IsDue { get; set; }
        public DateTime? DueRunLocal { get; set; }
        public DateTime? NextRunLocal { get; set; }
        public bool IsExpired { get; set; }
    }
}
