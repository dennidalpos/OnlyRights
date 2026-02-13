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
    }
}
