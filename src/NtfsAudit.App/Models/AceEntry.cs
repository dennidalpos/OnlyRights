using System.Collections.Generic;

namespace NtfsAudit.App.Models
{
    public class AceEntry
    {
        public string FolderPath { get; set; }
        public string PrincipalName { get; set; }
        public string PrincipalSid { get; set; }
        public string PrincipalType { get; set; }
        public PermissionLayer PermissionLayer { get; set; }
        public string AllowDeny { get; set; }
        public string RightsSummary { get; set; }
        public int RightsMask { get; set; }
        public string EffectiveRightsSummary { get; set; }
        public int EffectiveRightsMask { get; set; }
        public int ShareRightsMask { get; set; }
        public int NtfsRightsMask { get; set; }
        public bool IsInherited { get; set; }
        public bool AppliesToThisFolder { get; set; }
        public bool AppliesToSubfolders { get; set; }
        public bool AppliesToFiles { get; set; }
        public string InheritanceFlags { get; set; }
        public string PropagationFlags { get; set; }
        public string Source { get; set; }
        public int Depth { get; set; }
        public string ResourceType { get; set; }
        public string TargetPath { get; set; }
        public string Owner { get; set; }
        public string ShareName { get; set; }
        public string ShareServer { get; set; }
        public string AuditSummary { get; set; }
        public string RiskLevel { get; set; }
        public bool IsDisabled { get; set; }
        public bool IsServiceAccount { get; set; }
        public bool IsAdminAccount { get; set; }
        public bool HasExplicitPermissions { get; set; }
        public bool IsInheritanceDisabled { get; set; }
        public List<string> MemberNames { get; set; }
        public string MembersSummary
        {
            get { return MemberNames == null ? string.Empty : string.Join(", ", MemberNames); }
        }

        public bool HasFullControl { get { return HasRight("FullControl"); } }
        public bool HasModify { get { return HasRight("Modify"); } }
        public bool HasReadAndExecute { get { return HasRight("ReadAndExecute"); } }
        public bool HasList { get { return HasRight("List"); } }
        public bool HasRead { get { return HasRight("Read"); } }
        public bool HasWrite { get { return HasRight("Write"); } }
        public string HighestRightBadge
        {
            get
            {
                var summary = string.IsNullOrWhiteSpace(EffectiveRightsSummary)
                    ? RightsSummary
                    : EffectiveRightsSummary;
                if (string.IsNullOrWhiteSpace(summary)) return string.Empty;
                if (HasRight(summary, "FullControl")) return "Full";
                if (HasRight(summary, "Modify")) return "Modify";
                if (HasRight(summary, "ReadAndExecute")) return "R&E";
                if (HasRight(summary, "Write")) return "Write";
                if (HasRight(summary, "List")) return "List";
                if (HasRight(summary, "Read")) return "Read";
                return string.Empty;
            }
        }

        public bool HasHighestRightBadge
        {
            get { return !string.IsNullOrWhiteSpace(HighestRightBadge); }
        }
        public bool IsExplicitDeny
        {
            get
            {
                return !IsInherited && string.Equals(AllowDeny, "Deny", System.StringComparison.OrdinalIgnoreCase);
            }
        }

        private bool HasRight(string right)
        {
            return HasRight(RightsSummary, right);
        }

        private bool HasRight(string summary, string right)
        {
            if (string.IsNullOrWhiteSpace(summary)) return false;
            var parts = summary.Split('|');
            foreach (var part in parts)
            {
                if (string.Equals(part, right, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
