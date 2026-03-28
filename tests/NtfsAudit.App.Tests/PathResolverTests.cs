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
using NtfsAudit.App.Services;
using Xunit;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace NtfsAudit.App.Tests
{
    public class PathResolverTests
    {
        [Fact]
        public void GetDfsTargets_DrivePath_DoesNotThrowWhenUncCannotBeResolved()
        {
            var targets = PathResolver.GetDfsTargets("Z:\\folder");

            Assert.NotNull(targets);
        }

        [Fact]
        public void TryGetShareInfo_ParsesUncPathWithForwardSlashes()
        {
            var ok = PathResolver.TryGetShareInfo(@"\\server/share/folder", out var server, out var share);

            Assert.True(ok);
            Assert.Equal("server", server);
            Assert.Equal("share", share);
        }

        [Fact]
        public void FromExtendedPath_ConvertsExtendedUncToStandardUnc()
        {
            var path = PathResolver.FromExtendedPath(@"\\?\UNC\server\share\folder");

            Assert.Equal(@"\\server\share\folder", path);
        }

        [Fact]
        public void ToExtendedPath_NormalizesUncPathWithForwardSlashes()
        {
            var path = PathResolver.ToExtendedPath("//server/share/folder");

            Assert.Equal(@"\\?\UNC\server\share\folder", path);
        }

        [Fact]
        public void DetectPathKind_HandlesUncPathWithForwardSlashes()
        {
            var kind = PathResolver.DetectPathKind("//server/share/folder");

            Assert.True(kind == Models.PathKind.Unc || kind == Models.PathKind.Dfs);
        }

        [Fact]
        public void DetectPathKind_ReturnsUnc_ForIpSharePaths()
        {
            var kind = PathResolver.DetectPathKind(@"\\10.20.30.40\share\folder");

            Assert.Equal(Models.PathKind.Unc, kind);
        }

        [Fact]
        public void DetectPathKind_ReturnsDfs_WhenTargetsAreCached()
        {
            var dfsPath = BuildUniqueDfsPath();
            SeedDfsTargets(dfsPath, new List<string> { @"\\server-a\share\folder", @"\\server-b\share\folder" });

            try
            {
                Assert.Equal(Models.PathKind.Dfs, PathResolver.DetectPathKind(dfsPath));
            }
            finally
            {
                RemoveDfsTargets(dfsPath);
            }
        }

        [Fact]
        public void DetectPathKind_ReturnsNfs_ForNfsSchemePaths()
        {
            var kind = PathResolver.DetectPathKind("nfs://server/export/share");

            Assert.Equal(Models.PathKind.Nfs, kind);
        }

        [Fact]
        public void DetectPathKind_ReturnsNfs_ForWslMountedShares()
        {
            var kind = PathResolver.DetectPathKind(@"\\wsl$\Ubuntu\mnt\data");

            Assert.Equal(Models.PathKind.Nfs, kind);
        }

        private static string BuildUniqueDfsPath()
        {
            return string.Format(@"\\dfs-root\share\{0}", Guid.NewGuid().ToString("N"));
        }

        private static void SeedDfsTargets(string path, List<string> targets)
        {
            GetDfsTargetsCache()[path] = targets;
        }

        private static void RemoveDfsTargets(string path)
        {
            GetDfsTargetsCache().Remove(path);
        }

        private static Dictionary<string, List<string>> GetDfsTargetsCache()
        {
            var field = typeof(PathResolver).GetField("DfsTargetsCache", BindingFlags.NonPublic | BindingFlags.Static);
            return (Dictionary<string, List<string>>)field.GetValue(null);
        }
    }
}
