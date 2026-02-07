using System;

namespace NtfsAudit.App.Models
{
    public class AclDiffKey : IEquatable<AclDiffKey>
    {
        public string Sid { get; set; }
        public string AllowDeny { get; set; }
        public int RightsMask { get; set; }
        public string InheritanceFlags { get; set; }
        public string PropagationFlags { get; set; }
        public bool IsInherited { get; set; }

        public bool Equals(AclDiffKey other)
        {
            if (other == null) return false;
            return string.Equals(Sid, other.Sid, StringComparison.OrdinalIgnoreCase)
                && string.Equals(AllowDeny, other.AllowDeny, StringComparison.OrdinalIgnoreCase)
                && RightsMask == other.RightsMask
                && string.Equals(InheritanceFlags, other.InheritanceFlags, StringComparison.OrdinalIgnoreCase)
                && string.Equals(PropagationFlags, other.PropagationFlags, StringComparison.OrdinalIgnoreCase)
                && IsInherited == other.IsInherited;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as AclDiffKey);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 23 + (Sid == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Sid));
                hash = hash * 23 + (AllowDeny == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(AllowDeny));
                hash = hash * 23 + RightsMask.GetHashCode();
                hash = hash * 23 + (InheritanceFlags == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(InheritanceFlags));
                hash = hash * 23 + (PropagationFlags == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(PropagationFlags));
                hash = hash * 23 + IsInherited.GetHashCode();
                return hash;
            }
        }
    }
}
