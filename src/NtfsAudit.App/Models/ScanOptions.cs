namespace NtfsAudit.App.Models
{
    public class ScanOptions
    {
        public string RootPath { get; set; }
        public int MaxDepth { get; set; }
        public bool ScanAllDepths { get; set; }
        public bool ExpandGroups { get; set; }
        public bool UsePowerShell { get; set; }
        public bool ExportOnComplete { get; set; }
    }
}
