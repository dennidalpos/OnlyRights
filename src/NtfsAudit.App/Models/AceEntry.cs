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
        public PathKind PathKind { get; set; }
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

        public bool HasFullControl { get { return HasRight(GetRightsSummaryForFlags(), "FullControl"); } }
        public bool HasModify { get { return HasRight(GetRightsSummaryForFlags(), "Modify"); } }
        public bool HasReadAndExecute { get { return HasRight(GetRightsSummaryForFlags(), "ReadAndExecute"); } }
        public bool HasList { get { return HasRight(GetRightsSummaryForFlags(), "List"); } }
        public bool HasRead { get { return HasRight(GetRightsSummaryForFlags(), "Read"); } }
        public bool HasWrite { get { return HasRight(GetRightsSummaryForFlags(), "Write"); } }
        public bool IsExplicitDeny
        {
            get
            {
                return !IsInherited && string.Equals(AllowDeny, "Deny", System.StringComparison.OrdinalIgnoreCase);
            }
        }

        public string HighestRightKey
        {
            get
            {
                var summary = GetRightsSummaryForFlags();
                if (HasRight(summary, "FullControl")) return "Full";
                if (HasRight(summary, "Modify")) return "Modify";
                if (HasRight(summary, "ReadAndExecute")) return "ReadAndExecute";
                if (HasRight(summary, "Write")) return "Write";
                if (HasRight(summary, "List")) return "List";
                if (HasRight(summary, "Read")) return "Read";
                return string.Empty;
            }
        }

        public string HighestRightLabel
        {
            get
            {
                switch (HighestRightKey)
                {
                    case "Full":
                        return "Full";
                    case "Modify":
                        return "Modify";
                    case "ReadAndExecute":
                        return "R&E";
                    case "Write":
                        return "Write";
                    case "List":
                        return "List";
                    case "Read":
                        return "Read";
                    default:
                        return string.Empty;
                }
            }
        }

        public string HighestRightToolTip
        {
            get
            {
                switch (HighestRightKey)
                {
                    case "Full":
                        return "Controllo completo";
                    case "Modify":
                        return "Modifica";
                    case "ReadAndExecute":
                        return "Lettura ed esecuzione";
                    case "Write":
                        return "Scrittura";
                    case "List":
                        return "Visualizza elenco contenuti";
                    case "Read":
                        return "Permesso di lettura";
                    default:
                        return string.Empty;
                }
            }
        }

        private string GetRightsSummaryForFlags()
        {
            return string.IsNullOrWhiteSpace(EffectiveRightsSummary) ? RightsSummary : EffectiveRightsSummary;
        }

        private bool HasRight(string summary, string right)
        {
            if (string.IsNullOrWhiteSpace(summary)) return false;
            var parts = summary.Split(new[] { '|', ',' }, System.StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                if (string.Equals(part.Trim(), right, System.StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
