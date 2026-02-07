using System.Collections.Generic;

namespace NtfsAudit.App.Models
{
    public class AclDiffSummary
    {
        public bool IsProtected { get; set; }
        public int ExplicitCount { get; set; }
        public int DenyExplicitCount { get; set; }
        public List<AclDiffKey> Added { get; private set; } = new List<AclDiffKey>();
        public List<AclDiffKey> Removed { get; private set; } = new List<AclDiffKey>();
        public List<AclDiffPair> Modified { get; private set; } = new List<AclDiffPair>();
    }

    public class AclDiffPair
    {
        public AclDiffPair(AclDiffKey parent, AclDiffKey child)
        {
            Parent = parent;
            Child = child;
        }

        public AclDiffKey Parent { get; private set; }
        public AclDiffKey Child { get; private set; }
    }
}
