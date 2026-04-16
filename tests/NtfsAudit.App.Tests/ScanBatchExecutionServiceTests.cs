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
using System.Threading;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;
using Xunit;

#nullable enable

namespace NtfsAudit.App.Tests
{
    public class ScanBatchExecutionServiceTests
    {
        [Fact]
        public void Execute_MergesRootsAndBuildsMissingTreeMapWithoutViewModelState()
        {
            var service = new ScanBatchExecutionService();
            var roots = new[] { @"C:\Root\A", @"C:\Root\B" };
            var scannedRoots = new List<string>();
            var template = new ScanOptions
            {
                MaxDepth = 3,
                IncludeInherited = true
            };

            var aggregate = service.Execute(
                roots,
                template,
                (source, root) => ScanBatchExecutionService.CloneOptions(source, root),
                (options, _) =>
                {
                    scannedRoots.Add(options.RootPath);
                    return new ScanResult
                    {
                        RootPath = options.RootPath,
                        Details = new Dictionary<string, FolderDetail>
                        {
                            [options.RootPath] = new FolderDetail(),
                            [options.RootPath + @"\Child"] = new FolderDetail()
                        },
                        TreeMap = null!
                    };
                },
                CancellationToken.None);

            Assert.Equal(roots, scannedRoots);
            Assert.Equal(@"C:\Root\A", aggregate.RootPath);
            Assert.Contains(@"C:\Root\A", aggregate.Details.Keys);
            Assert.Contains(@"C:\Root\B", aggregate.Details.Keys);
            Assert.Contains(@"C:\Root\A\Child", aggregate.TreeMap[@"C:\Root\A"]);
            Assert.Contains(@"C:\Root\B\Child", aggregate.TreeMap[@"C:\Root\B"]);
        }

        [Fact]
        public void MergeScanResult_MergesTreeChildrenWithoutDuplicates()
        {
            var aggregate = new ScanResult
            {
                RootPath = @"C:\Root",
                Details = new Dictionary<string, FolderDetail>
                {
                    [@"C:\Root"] = new FolderDetail()
                },
                TreeMap = new Dictionary<string, List<string>>
                {
                    [@"C:\Root"] = new List<string> { @"C:\Root\Child" }
                }
            };
            var current = new ScanResult
            {
                RootPath = @"C:\Root",
                Details = new Dictionary<string, FolderDetail>
                {
                    [@"C:\Root\Child"] = new FolderDetail()
                },
                TreeMap = new Dictionary<string, List<string>>
                {
                    [@"C:\Root"] = new List<string> { @"C:\Root\Child", @"C:\Root\Other" }
                }
            };

            ScanBatchExecutionService.MergeScanResult(aggregate, current);

            Assert.Contains(@"C:\Root\Child", aggregate.Details.Keys);
            Assert.Equal(2, aggregate.TreeMap[@"C:\Root"].Count);
            Assert.Contains(@"C:\Root\Other", aggregate.TreeMap[@"C:\Root"]);
        }
    }
}
