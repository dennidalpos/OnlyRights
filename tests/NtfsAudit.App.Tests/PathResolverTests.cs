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
    }
}
