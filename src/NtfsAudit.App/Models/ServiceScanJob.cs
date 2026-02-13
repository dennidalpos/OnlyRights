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
