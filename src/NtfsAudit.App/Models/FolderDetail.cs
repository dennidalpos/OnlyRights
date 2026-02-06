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
        }

        public List<AceEntry> AllEntries { get; private set; }
        public List<AceEntry> GroupEntries { get; private set; }
        public List<AceEntry> UserEntries { get; private set; }
    }
}
