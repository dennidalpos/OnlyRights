using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public class AclDiffService
    {
        public void ApplyDiffs(Dictionary<string, FolderDetail> details)
        {
            if (details == null || details.Count == 0) return;

            foreach (var entry in details)
            {
                var path = entry.Key;
                var detail = entry.Value;
                if (detail == null) continue;

                var parentPath = SafeGetParent(path);
                FolderDetail parentDetail = null;
                if (!string.IsNullOrWhiteSpace(parentPath))
                {
                    details.TryGetValue(parentPath, out parentDetail);
                }

                var parentKeys = BuildKeys(parentDetail);
                var childKeys = BuildKeys(detail);
                var summary = Diff(parentKeys, childKeys, detail.IsInheritanceDisabled);
                detail.DiffSummary = summary;
            }
        }

        private static List<AclDiffKey> BuildKeys(FolderDetail detail)
        {
            var keys = new List<AclDiffKey>();
            if (detail == null || detail.AllEntries == null) return keys;

            foreach (var entry in detail.AllEntries)
            {
                var sid = string.IsNullOrWhiteSpace(entry.PrincipalSid) ? entry.PrincipalName : entry.PrincipalSid;
                keys.Add(new AclDiffKey
                {
                    Sid = sid,
                    AllowDeny = entry.AllowDeny,
                    RightsMask = entry.RightsMask,
                    InheritanceFlags = entry.InheritanceFlags,
                    PropagationFlags = entry.PropagationFlags,
                    IsInherited = entry.IsInherited
                });
            }

            return keys;
        }

        private static AclDiffSummary Diff(IEnumerable<AclDiffKey> parent, IEnumerable<AclDiffKey> child, bool isProtected)
        {
            var summary = new AclDiffSummary
            {
                IsProtected = isProtected
            };

            var parentList = parent == null ? new List<AclDiffKey>() : parent.ToList();
            var childList = child == null ? new List<AclDiffKey>() : child.ToList();
            var parentSet = new HashSet<AclDiffKey>(parentList);
            var childSet = new HashSet<AclDiffKey>(childList);

            foreach (var ace in childSet)
            {
                if (!parentSet.Contains(ace))
                {
                    summary.Added.Add(ace);
                }
            }

            foreach (var ace in parentSet)
            {
                if (!childSet.Contains(ace))
                {
                    summary.Removed.Add(ace);
                }
            }

            summary.ExplicitCount = childList.Count(entry => !entry.IsInherited);
            summary.DenyExplicitCount = childList.Count(entry =>
                !entry.IsInherited && string.Equals(entry.AllowDeny, "Deny", StringComparison.OrdinalIgnoreCase));

            var parentGroups = parentList.GroupBy(key => (key.Sid, key.AllowDeny), StringTupleComparer.Instance);
            var childGroups = childList.GroupBy(key => (key.Sid, key.AllowDeny), StringTupleComparer.Instance);
            var parentByPrincipal = parentGroups.ToDictionary(g => g.Key, g => new Queue<AclDiffKey>(g));
            var childByPrincipal = childGroups.ToDictionary(g => g.Key, g => new Queue<AclDiffKey>(g));

            foreach (var group in parentByPrincipal)
            {
                Queue<AclDiffKey> childQueue;
                if (!childByPrincipal.TryGetValue(group.Key, out childQueue)) continue;

                var parentQueue = group.Value;
                while (parentQueue.Count > 0 && childQueue.Count > 0)
                {
                    var parentKey = parentQueue.Dequeue();
                    var childKey = childQueue.Dequeue();
                    if (!parentKey.Equals(childKey))
                    {
                        summary.Modified.Add(new AclDiffPair(parentKey, childKey));
                    }
                }
            }

            return summary;
        }

        private static string SafeGetParent(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                var parent = Directory.GetParent(path);
                return parent == null ? null : parent.FullName;
            }
            catch
            {
                return null;
            }
        }

        private class StringTupleComparer : IEqualityComparer<(string Sid, string AllowDeny)>
        {
            public static readonly StringTupleComparer Instance = new StringTupleComparer();

            public bool Equals((string Sid, string AllowDeny) x, (string Sid, string AllowDeny) y)
            {
                return string.Equals(x.Sid, y.Sid, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.AllowDeny, y.AllowDeny, StringComparison.OrdinalIgnoreCase);
            }

            public int GetHashCode((string Sid, string AllowDeny) obj)
            {
                unchecked
                {
                    var hash = 17;
                    hash = hash * 23 + (obj.Sid == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Sid));
                    hash = hash * 23 + (obj.AllowDeny == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(obj.AllowDeny));
                    return hash;
                }
            }
        }
    }
}
