using System.Collections.Generic;

namespace NtfsAudit.App.Models
{
    public class FolderExplanation
    {
        public FolderExplanation()
        {
            Reasons = new List<string>();
            Summary = "Uguale al padre";
            Status = FolderStatus.Same;
        }

        public FolderStatus Status { get; set; }
        public List<string> Reasons { get; private set; }
        public string Summary { get; set; }
    }
}
