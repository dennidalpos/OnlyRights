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
using System.Collections.Generic;

#nullable enable

namespace NtfsAudit.Core.Models
{
    public sealed class ServiceScheduleRuntimeSnapshot
    {
        public List<ServiceScheduleStatusSnapshot> Schedules { get; set; } = new List<ServiceScheduleStatusSnapshot>();
    }
}
