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
using NtfsAudit.App.Services;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class ScanRootSelectionResolverTests
    {
        [Fact]
        public void Resolve_ReturnsRootPath_WhenPathIsNotDfs()
        {
            var selection = ScanRootSelectionResolver.Resolve(@"C:\data", new List<string>(), string.Empty);

            Assert.Equal(@"C:\data", selection.RootPath);
            Assert.Null(selection.NamespacePath);
            Assert.False(selection.RequiresExplicitSelection);
        }

        [Fact]
        public void Resolve_UsesSelectedDfsTarget_WhenSelectionMatchesAvailableTarget()
        {
            var selection = ScanRootSelectionResolver.Resolve(
                @"\\domain\namespace\share",
                new List<string> { @"\\server-a\share", @"\\server-b\share" },
                @"\\server-b\share");

            Assert.Equal(@"\\server-b\share", selection.RootPath);
            Assert.Equal(@"\\domain\namespace\share", selection.NamespacePath);
            Assert.False(selection.RequiresExplicitSelection);
        }

        [Fact]
        public void Resolve_RequiresExplicitSelection_WhenMultipleTargetsExistButNoValidSelectionIsProvided()
        {
            var selection = ScanRootSelectionResolver.Resolve(
                @"\\domain\namespace\share",
                new List<string> { @"\\server-a\share", @"\\server-b\share" },
                string.Empty);

            Assert.True(selection.RequiresExplicitSelection);
            Assert.Null(selection.RootPath);
            Assert.Equal(@"\\domain\namespace\share", selection.NamespacePath);
        }
    }
}
