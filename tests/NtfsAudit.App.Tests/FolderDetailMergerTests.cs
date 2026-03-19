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
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class FolderDetailMergerTests
    {
        [Fact]
        public void Merge_RecomputesDerivedFlagsAndPreservesSpecializedEntries()
        {
            var target = new FolderDetail
            {
                EntriesLoaded = false
            };
            target.AllEntries.Add(new AceEntry
            {
                PrincipalName = "Domain Users",
                PrincipalType = "Group",
                PermissionLayer = PermissionLayer.Ntfs,
                ResourceType = "Folder",
                RiskLevel = "Basso"
            });
            target.GroupEntries.Add(target.AllEntries[0]);

            var source = new FolderDetail
            {
                HasExplicitPermissions = true,
                HasExplicitNtfs = true,
                IsInheritanceDisabled = true
            };
            source.AllEntries.Add(new AceEntry
            {
                PrincipalName = "svc-backup",
                PrincipalType = "User",
                PermissionLayer = PermissionLayer.Ntfs,
                ResourceType = "File",
                RiskLevel = "Alto"
            });
            source.AllEntries.Add(new AceEntry
            {
                PrincipalName = "Share Users",
                PrincipalType = "Group",
                PermissionLayer = PermissionLayer.Share,
                ResourceType = "Folder",
                RiskLevel = "Medio"
            });
            source.AllEntries.Add(new AceEntry
            {
                PrincipalName = "Effective User",
                PrincipalType = "User",
                PermissionLayer = PermissionLayer.Effective,
                ResourceType = "Folder",
                RiskLevel = "Medio"
            });
            source.UserEntries.Add(source.AllEntries[0]);
            source.GroupEntries.Add(source.AllEntries[1]);
            source.ShareEntries.Add(source.AllEntries[1]);
            source.EffectiveEntries.Add(source.AllEntries[2]);

            FolderDetailMerger.Merge(target, source);

            Assert.Equal(4, target.AllEntries.Count);
            Assert.Equal(2, target.GroupEntries.Count);
            Assert.Single(target.UserEntries);
            Assert.Single(target.ShareEntries);
            Assert.Single(target.EffectiveEntries);
            Assert.True(target.HasExplicitPermissions);
            Assert.True(target.HasExplicitNtfs);
            Assert.True(target.IsInheritanceDisabled);
            Assert.True(target.HasFileEntries);
            Assert.True(target.HasFolderEntries);
            Assert.True(target.HasHighRiskEntries);
            Assert.True(target.HasMediumRiskEntries);
            Assert.True(target.HasLowRiskEntries);
            Assert.True(target.HasShareEntries);
            Assert.True(target.HasEffectiveEntries);
            Assert.True(target.EntriesLoaded);
        }
    }
}
