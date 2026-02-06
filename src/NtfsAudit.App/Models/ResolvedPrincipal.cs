namespace NtfsAudit.App.Models
{
    public class ResolvedPrincipal
    {
        public string Sid { get; set; }
        public string Name { get; set; }
        public bool IsGroup { get; set; }
        public bool IsDisabled { get; set; }
        public bool IsServiceAccount { get; set; }
        public bool IsAdminAccount { get; set; }
        public string Type
        {
            get { return IsGroup ? "Group" : "User"; }
        }
    }
}
