namespace NtfsAudit.App.Models
{
    public enum PermissionLayer
    {
        Ntfs,
        Share,
        Effective
    }

    public enum PermissionDecision
    {
        Allow,
        Deny
    }

    public abstract class PermissionEntry
    {
        public string PrincipalName { get; set; }
        public string PrincipalSid { get; set; }
        public string PrincipalType { get; set; }
        public PermissionDecision AccessType { get; set; }
        public int RightsMask { get; set; }
        public string RightsSummary { get; set; }
        public bool IsInherited { get; set; }
        public bool AppliesToThisFolder { get; set; }
        public bool AppliesToSubfolders { get; set; }
        public bool AppliesToFiles { get; set; }
        public PermissionLayer Source { get; protected set; }
    }

    public class NtfsPermission : PermissionEntry
    {
        public NtfsPermission()
        {
            Source = PermissionLayer.Ntfs;
        }

        public string FolderPath { get; set; }
        public string TargetPath { get; set; }
        public string InheritanceFlags { get; set; }
        public string PropagationFlags { get; set; }
    }

    public class SharePermission : PermissionEntry
    {
        public SharePermission()
        {
            Source = PermissionLayer.Share;
        }

        public string ShareName { get; set; }
        public string ShareServer { get; set; }
    }

    public class EffectivePermission : PermissionEntry
    {
        public EffectivePermission()
        {
            Source = PermissionLayer.Effective;
        }

        public string FolderPath { get; set; }
        public string TargetPath { get; set; }
        public int ShareRightsMask { get; set; }
        public int NtfsRightsMask { get; set; }
    }
}
