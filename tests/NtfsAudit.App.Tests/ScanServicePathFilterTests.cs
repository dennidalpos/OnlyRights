using System;
using System.Reflection;
using NtfsAudit.App.Services;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class ScanServicePathFilterTests
    {
        private static bool InvokeIsDfsCachePath(string path)
        {
            var method = typeof(ScanService).GetMethod("IsDfsCachePath", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);
            return (bool)method.Invoke(null, new object[] { path });
        }

        [Theory]
        [InlineData(@"C:\System Volume Information")]
        [InlineData(@"C:\System Volume Information\DFSR")]
        [InlineData(@"C:\data\DfsrPrivate")]
        [InlineData(@"C:\data\DfsrPrivate\ConflictAndDeleted")]
        [InlineData(@"C:\data\DFSR\Staging")]
        public void IsDfsCachePath_ReturnsTrue_ForSystemDfsrFolders(string path)
        {
            Assert.True(InvokeIsDfsCachePath(path));
        }

        [Theory]
        [InlineData(@"C:\data")]
        [InlineData(@"C:\data\Projects")]
        [InlineData(@"\\server\share\business")]
        public void IsDfsCachePath_ReturnsFalse_ForRegularFolders(string path)
        {
            Assert.False(InvokeIsDfsCachePath(path));
        }
    }
}
