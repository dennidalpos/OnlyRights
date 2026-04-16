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

namespace NtfsAudit.App.Models
{
    public class ScanProgress
    {
        public int Processed { get; set; }
        public int FilesProcessed { get; set; }
        public int QueueCount { get; set; }
        public int Errors { get; set; }
        public TimeSpan Elapsed { get; set; }
        public string Stage { get; set; }
        public string CurrentPath { get; set; }
    }
}
