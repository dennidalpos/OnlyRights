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
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using NtfsAudit.Core.Services;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class ScanRootPathConvertersTests
    {
        [Fact]
        public void LabelConverter_ReturnsExpectedLabelsForKnownPathKinds()
        {
            LocalizationManager.Apply(new ResourceDictionary(), "en");
            var converter = new ScanRootPathKindLabelConverter();
            var dfsPath = BuildUniqueDfsPath();
            SeedDfsTargets(dfsPath, new List<string>
            {
                @"\\server-a\share\root",
                @"\\server-b\share\root"
            });

            try
            {
                Assert.Equal("Local", converter.Convert(@"C:\data", typeof(string), null, CultureInfo.InvariantCulture));
                Assert.Equal("WSL UNC", converter.Convert(@"\\wsl$\Ubuntu\mnt\data", typeof(string), null, CultureInfo.InvariantCulture));
                Assert.Equal("DFS", converter.Convert(dfsPath, typeof(string), null, CultureInfo.InvariantCulture));
                Assert.Equal("Unsupported", converter.Convert("nfs://server/export/share", typeof(string), null, CultureInfo.InvariantCulture));
                Assert.Equal("Unknown", converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture));
            }
            finally
            {
                RemoveDfsTargets(dfsPath);
            }
        }

        [Fact]
        public void BrushConverter_ReturnsExpectedBrushesForKnownPathKinds()
        {
            var converter = new ScanRootPathKindBrushConverter();
            var dfsPath = BuildUniqueDfsPath();
            SeedDfsTargets(dfsPath, new List<string>
            {
                @"\\server-a\share\root",
                @"\\server-b\share\root"
            });

            try
            {
                Assert.Equal(Color.FromRgb(245, 245, 245), GetColor(converter.Convert(@"C:\data", typeof(Brush), null, CultureInfo.InvariantCulture)));
                Assert.Equal(Color.FromRgb(255, 243, 224), GetColor(converter.Convert(@"\\wsl$\Ubuntu\mnt\data", typeof(Brush), null, CultureInfo.InvariantCulture)));
                Assert.Equal(Color.FromRgb(232, 245, 233), GetColor(converter.Convert(dfsPath, typeof(Brush), null, CultureInfo.InvariantCulture)));
                Assert.Equal(Color.FromRgb(255, 235, 238), GetColor(converter.Convert("nfs://server/export/share", typeof(Brush), null, CultureInfo.InvariantCulture)));
            }
            finally
            {
                RemoveDfsTargets(dfsPath);
            }
        }

        [Fact]
        public void DfsVisibilityConverter_OnlyShowsForMultiTargetDfsPaths()
        {
            var converter = new DfsMultiServerVisibilityConverter();
            var dfsPath = BuildUniqueDfsPath();
            SeedDfsTargets(dfsPath, new List<string>
            {
                @"\\server-a\share\root",
                @"\\server-b\share\root"
            });

            try
            {
                Assert.Equal(Visibility.Collapsed, converter.Convert(@"C:\data", typeof(Visibility), null, CultureInfo.InvariantCulture));
                Assert.Equal(Visibility.Visible, converter.Convert(dfsPath, typeof(Visibility), null, CultureInfo.InvariantCulture));
            }
            finally
            {
                RemoveDfsTargets(dfsPath);
            }
        }

        [Fact]
        public void ConvertBack_IsNotSupportedForAllConverters()
        {
            Assert.Throws<NotSupportedException>(() => new ScanRootPathKindLabelConverter().ConvertBack(null, typeof(string), null, CultureInfo.InvariantCulture));
            Assert.Throws<NotSupportedException>(() => new ScanRootPathKindBrushConverter().ConvertBack(null, typeof(Brush), null, CultureInfo.InvariantCulture));
            Assert.Throws<NotSupportedException>(() => new DfsMultiServerVisibilityConverter().ConvertBack(null, typeof(Visibility), null, CultureInfo.InvariantCulture));
        }

        private static Color GetColor(object value)
        {
            var brush = Assert.IsType<SolidColorBrush>(value);
            return brush.Color;
        }

        private static string BuildUniqueDfsPath()
        {
            return string.Format(@"\\dfs-root\share\{0}", Guid.NewGuid().ToString("N"));
        }

        private static void SeedDfsTargets(string path, List<string> targets)
        {
            PathResolver.SetDfsTargetsCacheForTest(path, targets, DateTime.UtcNow);
        }

        private static void RemoveDfsTargets(string path)
        {
            PathResolver.ResetDfsCacheForTest();
        }
    }
}
