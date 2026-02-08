namespace NtfsAudit.App.Models
{
    public class ScanOptions
    {
        public string RootPath { get; set; }
        public int MaxDepth { get; set; }
        public bool ScanAllDepths { get; set; }
        public bool IncludeInherited { get; set; }
        public bool ResolveIdentities { get; set; }
        public bool ExcludeServiceAccounts { get; set; }
        public bool ExcludeAdminAccounts { get; set; }
        public bool ExpandGroups { get; set; }
        public bool UsePowerShell { get; set; }
        public bool EnableAdvancedAudit { get; set; }
        public bool ComputeEffectiveAccess { get; set; }
        public bool IncludeSharePermissions { get; set; }
        public bool IncludeFiles { get; set; }
        public bool ReadOwnerAndSacl { get; set; }
        public bool CompareBaseline { get; set; }
    }
}
