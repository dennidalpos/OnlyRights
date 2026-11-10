using NtfsAudit.App.Services;
using NtfsAudit.Core.Logging;
using NtfsAudit.Core.Export;
using NtfsAudit.Core.Cache;
using NtfsAudit.Core.Models;
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
using System.Reflection;
using NtfsAudit.Core.Services;
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
        [InlineData(@"C:/System Volume Information/DFSR")]
        [InlineData(@"C:/data/DfsrPrivate/Staging/")]
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
