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
    }
}
