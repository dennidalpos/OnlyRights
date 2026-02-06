using System.Collections.Generic;

namespace NtfsAudit.App.Export
{
    public class ExportRecord
    {
        public string FolderPath { get; set; }
        public string PrincipalName { get; set; }
        public string PrincipalSid { get; set; }
        public string PrincipalType { get; set; }
        public string AllowDeny { get; set; }
        public string RightsSummary { get; set; }
        public bool IsInherited { get; set; }
        public string InheritanceFlags { get; set; }
        public string PropagationFlags { get; set; }
        public string Source { get; set; }
        public int Depth { get; set; }
        public bool IsDisabled { get; set; }
        public bool IsServiceAccount { get; set; }
        public bool IsAdminAccount { get; set; }
        public List<string> MemberNames { get; set; }
        public bool IncludeInherited { get; set; }
        public bool ResolveIdentities { get; set; }
        public bool ExcludeServiceAccounts { get; set; }
        public bool ExcludeAdminAccounts { get; set; }
    }
}
