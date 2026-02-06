namespace NtfsAudit.App.Models
{
    public class ResolvedPrincipal
    {
        public string Sid { get; set; }
        public string Name { get; set; }
        public bool IsGroup { get; set; }
        public string Type
        {
            get { return IsGroup ? "Group" : "User"; }
        }
    }
}
