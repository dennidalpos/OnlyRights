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
using System.Threading.Tasks;
using NtfsAudit.Viewer;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class ViewerStartupCoordinatorTests
    {
        [Fact]
        public void ResolveArchivePath_ReturnsNullWhenArgsMissing()
        {
            Assert.Null(ViewerStartupCoordinator.ResolveArchivePath(null, _ => false));
            Assert.Null(ViewerStartupCoordinator.ResolveArchivePath(new string[0], _ => false));
            Assert.Null(ViewerStartupCoordinator.ResolveArchivePath(new[] { " " }, _ => false));
        }

        [Fact]
        public void ResolveArchivePath_UsesExactArchiveArgumentWhenFileExists()
        {
            var resolved = ViewerStartupCoordinator.ResolveArchivePath(
                new[] { @"C:\reports\audit.ntaudit" },
                path => path == @"C:\reports\audit.ntaudit");

            Assert.Equal(@"C:\reports\audit.ntaudit", resolved);
        }

        [Fact]
        public void ResolveArchivePath_FallsBackToNtauditExtension()
        {
            var resolved = ViewerStartupCoordinator.ResolveArchivePath(
                new[] { @"C:\reports\audit" },
                path => path == @"C:\reports\audit.ntaudit");

            Assert.Equal(@"C:\reports\audit.ntaudit", resolved);
        }

        [Fact]
        public async Task TryImportFromArgsAsync_InvokesImportOnlyForResolvedArchive()
        {
            var importedPaths = new List<string>();

            await ViewerStartupCoordinator.TryImportFromArgsAsync(
                new[] { @"C:\reports\audit" },
                path =>
                {
                    importedPaths.Add(path);
                    return Task.CompletedTask;
                },
                path => path == @"C:\reports\audit.ntaudit");

            await ViewerStartupCoordinator.TryImportFromArgsAsync(
                new[] { @"C:\reports\missing" },
                path =>
                {
                    importedPaths.Add(path);
                    return Task.CompletedTask;
                },
                _ => false);

            Assert.Single(importedPaths);
            Assert.Equal(@"C:\reports\audit.ntaudit", importedPaths[0]);
        }
    }
}
