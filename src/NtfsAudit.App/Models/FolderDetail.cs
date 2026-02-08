using System.Collections.Generic;

namespace NtfsAudit.App.Models
{
    public class FolderDetail
    {
        public FolderDetail()
        {
            AllEntries = new List<AceEntry>();
            GroupEntries = new List<AceEntry>();
            UserEntries = new List<AceEntry>();
            ShareEntries = new List<AceEntry>();
            EffectiveEntries = new List<AceEntry>();
        }

        public List<AceEntry> AllEntries { get; private set; }
        public List<AceEntry> GroupEntries { get; private set; }
        public List<AceEntry> UserEntries { get; private set; }
        public List<AceEntry> ShareEntries { get; private set; }
        public List<AceEntry> EffectiveEntries { get; private set; }
        public bool HasExplicitPermissions { get; set; }
        public bool HasExplicitNtfs { get; set; }
        public bool HasExplicitShare { get; set; }
        public bool IsInheritanceDisabled { get; set; }
        public AclDiffSummary DiffSummary { get; set; }
        public AclDiffSummary BaselineSummary { get; set; }
    }
}
