using System;

namespace NtfsAudit.App.Models
{
    public class ServiceRuntimeStatus
    {
        public bool IsRunning { get; set; }
        public string CurrentJobId { get; set; }
        public string CurrentRootPath { get; set; }
        public int CurrentRootIndex { get; set; }
        public int TotalRoots { get; set; }
        public int PendingJobs { get; set; }
        public DateTime StartedAtUtc { get; set; }
        public DateTime LastUpdateUtc { get; set; }
        public string LastMessage { get; set; }
    }
}
