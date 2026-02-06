using System;

namespace NtfsAudit.App.Models
{
    public class ScanProgress
    {
        public int Processed { get; set; }
        public int QueueCount { get; set; }
        public int Errors { get; set; }
        public TimeSpan Elapsed { get; set; }
    }
}
