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
using System.Collections.Generic;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class AclDiffServiceTests
    {
        [Fact]
        public void ApplyDiffs_ComputesAddedRemovedAndModifiedEntries()
        {
            var service = new AclDiffService();
            var details = new Dictionary<string, FolderDetail>
            {
                [@"C:\root"] = BuildDetail(
                    BuildEntry("S-1-mod", "Allow", 1, false, inheritanceFlags: "ContainerInherit", propagationFlags: "None"),
                    BuildEntry("S-1-removed", "Allow", 2, false)),
                [@"C:\root\child"] = BuildDetail(
                    BuildEntry("S-1-mod", "Allow", 4, false, inheritanceFlags: "ContainerInherit", propagationFlags: "None"),
                    BuildEntry("S-1-added", "Allow", 8, false))
            };

            service.ApplyDiffs(details);

            var summary = details[@"C:\root\child"].DiffSummary;
            Assert.NotNull(summary);
            Assert.Contains(summary.Added, entry => entry.Sid == "S-1-added");
            Assert.Contains(summary.Removed, entry => entry.Sid == "S-1-removed");
            var modified = Assert.Single(summary.Modified);
            Assert.Equal("S-1-mod", modified.Parent.Sid);
            Assert.Equal(1, modified.Parent.RightsMask);
            Assert.Equal(4, modified.Child.RightsMask);
        }

        [Fact]
        public void ApplyDiffs_TracksExplicitAndExplicitDenyCountsForProtectedFolder()
        {
            var service = new AclDiffService();
            var details = new Dictionary<string, FolderDetail>
            {
                [@"C:\root"] = BuildDetail(BuildEntry("S-1-parent", "Allow", 1, true)),
                [@"C:\root\child"] = BuildDetail(
                    true,
                    BuildEntry("S-1-allow", "Allow", 1, false),
                    BuildEntry("S-1-deny", "Deny", 2, false),
                    BuildEntry("S-1-inherited", "Allow", 4, true))
            };

            service.ApplyDiffs(details);

            var summary = details[@"C:\root\child"].DiffSummary;
            Assert.NotNull(summary);
            Assert.True(summary.IsProtected);
            Assert.Equal(2, summary.ExplicitCount);
            Assert.Equal(1, summary.DenyExplicitCount);
        }

        [Fact]
        public void ApplyDiffs_TreatsFoldersWithoutParentAsAllAdded()
        {
            var service = new AclDiffService();
            var details = new Dictionary<string, FolderDetail>
            {
                [@"C:\orphan\child"] = BuildDetail(
                    BuildEntry("S-1-orphan", "Allow", 1, false),
                    BuildEntry("S-1-inherited", "Deny", 2, true))
            };

            service.ApplyDiffs(details);

            var summary = details[@"C:\orphan\child"].DiffSummary;
            Assert.NotNull(summary);
            Assert.False(summary.IsProtected);
            Assert.Equal(2, summary.Added.Count);
            Assert.Empty(summary.Removed);
            Assert.Empty(summary.Modified);
        }

        private static FolderDetail BuildDetail(params AceEntry[] entries)
        {
            return BuildDetail(false, entries);
        }

        private static FolderDetail BuildDetail(bool isInheritanceDisabled, params AceEntry[] entries)
        {
            var detail = new FolderDetail
            {
                IsInheritanceDisabled = isInheritanceDisabled
            };

            foreach (var entry in entries)
            {
                detail.AllEntries.Add(entry);
            }

            return detail;
        }

        private static AceEntry BuildEntry(string sid, string allowDeny, int rightsMask, bool isInherited, string inheritanceFlags = null, string propagationFlags = null)
        {
            return new AceEntry
            {
                PrincipalSid = sid,
                PrincipalName = sid,
                AllowDeny = allowDeny,
                RightsMask = rightsMask,
                IsInherited = isInherited,
                InheritanceFlags = inheritanceFlags,
                PropagationFlags = propagationFlags
            };
        }
    }
}
