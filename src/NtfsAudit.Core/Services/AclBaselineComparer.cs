/*
 * OnlyRights
 * Copyright (c) 2026 Danny Perondi
 * All rights reserved.
 *
 * Proprietary and confidential.
 * Viewing is permitted only for reference, evaluation, or internal review.
 * Unauthorized copying, modification, distribution, sublicensing,
 * commercial use, or reuse of this file is prohibited without prior
 * written permission from Danny Perondi.
 */
using System;
using System.Collections.Generic;
using System.Linq;
using NtfsAudit.Core.Models;

namespace NtfsAudit.Core.Services
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
