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
    public class ScanResult
    {
        public string TempDataPath { get; set; }
        public string ErrorPath { get; set; }
        public Dictionary<string, FolderDetail> Details { get; set; }
        public Dictionary<string, List<string>> TreeMap { get; set; }
        public string RootPath { get; set; }
        public PathKind RootPathKind { get; set; }
        public ScanOptions ScanOptions { get; set; }
        public DateTime ScannedAtUtc { get; set; }
        public string SqliteDatabasePath { get; set; }
        public bool UsesSqliteBackend { get; set; }
    }
}
