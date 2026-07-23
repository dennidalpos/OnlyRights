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
using System.Collections.Generic;
using System;
using System.IO;
using System.Threading;
using NtfsAudit.Core.Models;
using NtfsAudit.Core.Services;
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
                    Assert.NotNull(options.RootPath);
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
            Assert.NotNull(aggregate.Details);
            Assert.NotNull(aggregate.TreeMap);
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

        [Fact]
        public void ScanExportPathBuilder_KeepsStableNamesForSupportedRootShapes()
        {
            var timestamp = new DateTime(2026, 4, 16, 9, 30, 0);

            Assert.Equal("C_2026_04_16_09_30.ntaudit", ScanExportPathBuilder.BuildExportFileName(@"C:\", "ntaudit", timestamp));
            Assert.Equal("Share_2026_04_16_09_30.ntaudit", ScanExportPathBuilder.BuildExportFileName(@"C:\DeptA\Share", "ntaudit", timestamp));
            Assert.Equal("Share_2026_04_16_09_30.ntaudit", ScanExportPathBuilder.BuildExportFileName(@"\\server\DeptA\Share", "ntaudit", timestamp));
        }

        [Fact]
        public void ScanExportPathBuilder_AppendsNumericSuffixWhenArchivePathExists()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "NtfsAudit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);
            var timestamp = new DateTime(2026, 4, 16, 9, 30, 0);
            var firstPath = Path.Combine(tempRoot, "Share_2026_04_16_09_30.ntaudit");
            File.WriteAllText(firstPath, "existing");

            try
            {
                var nextPath = ScanExportPathBuilder.BuildUniqueArchivePath(tempRoot, @"D:\DeptB\Share", "ntaudit", timestamp);

                Assert.Equal(Path.Combine(tempRoot, "Share_2026_04_16_09_30_2.ntaudit"), nextPath);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }
    }
}
