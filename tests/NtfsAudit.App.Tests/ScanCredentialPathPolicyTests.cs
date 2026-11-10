using NtfsAudit.App.Services;
using NtfsAudit.Core.Logging;
using NtfsAudit.Core.Export;
using NtfsAudit.Core.Cache;
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
using NtfsAudit.Core.Models;
using NtfsAudit.Core.Services;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class ScanCredentialPathPolicyTests
    {
        [Theory]
        [InlineData(@"C:\Data", false)]
        [InlineData(@"\\server\share", true)]
        [InlineData(@"\\10.0.0.5\share", true)]
        [InlineData(@"\\wsl$\Ubuntu\mnt\data", true)]
        public void ShouldUseConfiguredCredential_DistinguishesLocalAndUncPaths(string path, bool expected)
        {
            Assert.Equal(expected, ScanCredentialPathPolicy.ShouldUseConfiguredCredential(path));
        }

        [Fact]
        public void ShouldUseConfiguredCredential_ReturnsTrue_ForDfsNamespaces()
        {
            var dfsPath = BuildUniqueDfsPath();
            SeedDfsTargets(dfsPath, new List<string> { @"\\server-a\share\folder" });

            try
            {
                Assert.True(ScanCredentialPathPolicy.ShouldUseConfiguredCredential(dfsPath));
            }
            finally
            {
                RemoveDfsTargets(dfsPath);
            }
        }

        [Fact]
        public void ResolveRuntimeCredential_DropsConfiguredCredential_ForLocalPaths()
        {
            var configured = new ScanCredential
            {
                UserName = @"CONTOSO\scanner",
                Password = "Secret!123"
            };

            var runtime = ScanCredentialPathPolicy.ResolveRuntimeCredential(@"C:\Data", configured);

            Assert.Null(runtime);
        }

        [Fact]
        public void ResolveRuntimeCredential_PreservesConfiguredCredential_ForUncPaths()
        {
            var configured = new ScanCredential
            {
                UserName = @"CONTOSO\scanner",
                Password = "Secret!123"
            };

            var runtime = ScanCredentialPathPolicy.ResolveRuntimeCredential(@"\\10.0.0.5\share", configured);

            Assert.NotNull(runtime);
            Assert.NotSame(configured, runtime);
            Assert.Equal(configured.UserName, runtime.UserName);
        }

        [Theory]
        [InlineData(@"C:\Data", "Global", "CurrentUser")]
        [InlineData(@"\\server\share", "Global", "Global")]
        [InlineData(@"\\server\share", "CurrentUser", "CurrentUser")]
        public void ResolveEffectiveSource_FollowsPathPolicy(string path, string configuredSource, string expected)
        {
            Assert.Equal(expected, ScanCredentialPathPolicy.ResolveEffectiveSource(path, configuredSource));
        }

        private static string BuildUniqueDfsPath()
        {
            return string.Format(@"\\dfs-root\share\{0}", Guid.NewGuid().ToString("N"));
        }

        private static void SeedDfsTargets(string path, List<string> targets)
        {
            PathResolver.SetDfsTargetsCacheForTest(path, targets, DateTime.UtcNow);
        }

        private static void RemoveDfsTargets(string path)
        {
            PathResolver.ResetDfsCacheForTest();
        }
    }
}
