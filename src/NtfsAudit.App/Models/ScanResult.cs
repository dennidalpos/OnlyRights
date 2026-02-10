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
    }
}
