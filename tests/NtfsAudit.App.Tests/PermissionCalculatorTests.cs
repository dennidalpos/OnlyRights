using System.Collections.Generic;
using System.Security.AccessControl;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class PermissionCalculatorTests
    {
        [Fact]
        public void IntersectMasks_UsesShareIntersectionWhenAvailable()
        {
            var ntfsPermissions = new List<PermissionEntry>
            {
                new NtfsPermission
                {
                    PrincipalSid = "S-1-1-0",
                    AccessType = PermissionDecision.Allow,
                    RightsMask = (int)(FileSystemRights.ReadData | FileSystemRights.WriteData),
                    IsInherited = false
                }
            };
            var sharePermissions = new List<PermissionEntry>
            {
                new SharePermission
                {
                    PrincipalSid = "S-1-1-0",
                    AccessType = PermissionDecision.Allow,
                    RightsMask = (int)FileSystemRights.ReadData,
                    IsInherited = false
                }
            };

            var ntfsMap = PermissionCalculator.BuildAccessMap(ntfsPermissions, true);
            var shareMap = PermissionCalculator.BuildAccessMap(sharePermissions, true);

            var ntfsMask = PermissionCalculator.GetEffectiveMask(ntfsMap, "S-1-1-0");
            var shareMask = PermissionCalculator.GetEffectiveMask(shareMap, "S-1-1-0");

            var effective = PermissionCalculator.IntersectMasks(ntfsMask, shareMask, true);

            Assert.Equal((int)FileSystemRights.ReadData, effective);
        }

        [Fact]
        public void ResolveScope_RespectsInheritOnlyPropagation()
        {
            var scope = PermissionCalculator.ResolveScope(
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.InheritOnly);

            Assert.False(scope.AppliesToThisFolder);
            Assert.True(scope.AppliesToSubfolders);
            Assert.True(scope.AppliesToFiles);
        }

        [Fact]
        public void BuildBaselineDiff_TracksAddedRemovedAndExplicitCounts()
        {
            var baseline = new List<AclDiffKey>
            {
                new AclDiffKey
                {
                    Sid = "S-1-1-0",
                    AllowDeny = "Allow",
                    RightsMask = (int)FileSystemRights.ReadData,
                    InheritanceFlags = "None",
                    PropagationFlags = "None",
                    IsInherited = false
                }
            };
            var current = new List<AclDiffKey>
            {
                new AclDiffKey
                {
                    Sid = "S-1-1-0",
                    AllowDeny = "Allow",
                    RightsMask = (int)FileSystemRights.ReadData,
                    InheritanceFlags = "None",
                    PropagationFlags = "None",
                    IsInherited = false
                },
                new AclDiffKey
                {
                    Sid = "S-1-5-11",
                    AllowDeny = "Deny",
                    RightsMask = (int)FileSystemRights.WriteData,
                    InheritanceFlags = "None",
                    PropagationFlags = "None",
                    IsInherited = false
                }
            };

            var summary = AclBaselineComparer.BuildBaselineDiff(baseline, current);

            Assert.Single(summary.Added);
            Assert.Empty(summary.Removed);
            Assert.Equal(2, summary.ExplicitCount);
            Assert.Equal(1, summary.DenyExplicitCount);
        }
    }
}
