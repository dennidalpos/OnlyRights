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

namespace NtfsAudit.App.Models
{
    public class ServiceScanJob
    {
        public string JobId { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public List<ScanOptions> ScanOptions { get; set; }
    }
}
