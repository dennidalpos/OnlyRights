using System;
using System.Collections.Generic;
using System.Linq;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public static class AclBaselineComparer
    {
        public static AclDiffSummary BuildBaselineDiff(IEnumerable<AclDiffKey> baseline, IEnumerable<AclDiffKey> current)
        {
            var summary = new AclDiffSummary();
            if (baseline == null || current == null) return summary;

            var baselineList = baseline.ToList();
            var currentList = current.ToList();
            var baselineSet = new HashSet<AclDiffKey>(baselineList);
            var currentSet = new HashSet<AclDiffKey>(currentList);

            foreach (var ace in currentSet)
            {
                if (!baselineSet.Contains(ace))
                {
                    summary.Added.Add(ace);
                }
            }

            foreach (var ace in baselineSet)
            {
                if (!currentSet.Contains(ace))
                {
                    summary.Removed.Add(ace);
                }
            }

            summary.ExplicitCount = currentList.Count(entry => !entry.IsInherited);
            summary.DenyExplicitCount = currentList.Count(entry =>
                !entry.IsInherited && string.Equals(entry.AllowDeny, "Deny", StringComparison.OrdinalIgnoreCase));
            return summary;
        }
    }
}
