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
using NtfsAudit.App.ViewModels;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class MainViewModelDisplayStateTests
    {
        [Fact]
        public void InitialDisplayState_ShowsEmptyScanAndStartHint()
        {
            var viewModel = new MainViewModel();
            ClearScanInputs(viewModel);

            Assert.True(viewModel.HasNoScanResult);
            Assert.False(viewModel.HasScanResult);
            Assert.False(viewModel.HasSelectedFolder);
            Assert.False(viewModel.HasNoSelectedFolder);
            Assert.True(viewModel.ShouldShowStartHint);
        }

        [Fact]
        public void StartHint_HidesWhenRootPathIsAvailable()
        {
            var viewModel = new MainViewModel();
            ClearScanInputs(viewModel);

            viewModel.RootPath = @"C:\Data";

            Assert.False(viewModel.ShouldShowStartHint);
            Assert.True(viewModel.CanStart);
        }

        [Fact]
        public void StartHint_HidesWhenScanRootIsAvailable()
        {
            var viewModel = new MainViewModel();
            ClearScanInputs(viewModel);

            viewModel.ScanRoots.Add(@"C:\Data");

            Assert.False(viewModel.ShouldShowStartHint);
            Assert.True(viewModel.CanStart);
        }

        [Fact]
        public void ViewerMode_NeverShowsStartHint()
        {
            var viewModel = new MainViewModel(true);

            Assert.False(viewModel.ShouldShowStartHint);
            Assert.False(viewModel.CanStart);
        }

        private static void ClearScanInputs(MainViewModel viewModel)
        {
            viewModel.RootPath = string.Empty;
            viewModel.ScanRoots.Clear();
        }
    }
}
