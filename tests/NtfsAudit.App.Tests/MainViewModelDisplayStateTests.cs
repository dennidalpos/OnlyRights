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
using System.IO;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;
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

        [Fact]
        public void SaveGlobalCredential_WithEmptyFields_ClearsStoredCredentialAndUsesCurrentUser()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "NtfsAudit.Tests", Guid.NewGuid().ToString("N"));
            var storePath = Path.Combine(tempRoot, "scan-credentials.json");
            Directory.CreateDirectory(tempRoot);

            try
            {
                var store = new ScanCredentialStore(storePath);
                store.SaveGlobal(new ScanCredential
                {
                    UserName = @"CONTOSO\scanner",
                    Password = "Secret!123"
                });
                var viewModel = new MainViewModel(store);

                viewModel.GlobalCredentialUserName = string.Empty;
                viewModel.GlobalCredentialPassword = string.Empty;
                viewModel.SaveGlobalCredentialCommand.Execute(null);

                Assert.False(viewModel.HasGlobalCredential);
                Assert.Null(store.Load().GlobalCredential);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Fact]
        public void ClassifyGlobalCredentialInput_RequiresBothUserAndPasswordOrBothEmpty()
        {
            Assert.Equal(GlobalCredentialInputState.Empty, MainViewModel.ClassifyGlobalCredentialInput(string.Empty, string.Empty));
            Assert.Equal(GlobalCredentialInputState.Partial, MainViewModel.ClassifyGlobalCredentialInput(@"CONTOSO\scanner", string.Empty));
            Assert.Equal(GlobalCredentialInputState.Partial, MainViewModel.ClassifyGlobalCredentialInput(string.Empty, "Secret!123"));
            Assert.Equal(GlobalCredentialInputState.Complete, MainViewModel.ClassifyGlobalCredentialInput(@"CONTOSO\scanner", "Secret!123"));
        }

        [Fact]
        public void ApplyCompatibleScanOptionsCommand_SetsMaximumCompatibilityPreset()
        {
            var viewModel = new MainViewModel();

            viewModel.UseWindowsServiceMode = true;
            viewModel.ScanAllDepths = false;
            viewModel.IncludeInherited = false;
            viewModel.ResolveIdentities = true;
            viewModel.ExcludeServiceAccounts = true;
            viewModel.ExcludeAdminAccounts = true;
            viewModel.ExpandGroups = true;
            viewModel.UsePowerShell = true;
            viewModel.EnableAdvancedAudit = true;
            viewModel.ComputeEffectiveAccess = true;
            viewModel.IncludeSharePermissions = true;
            viewModel.IncludeFiles = true;
            viewModel.ReadOwnerAndSacl = true;
            viewModel.CompareBaseline = true;

            viewModel.ApplyCompatibleScanOptionsCommand.Execute(null);

            Assert.False(viewModel.UseWindowsServiceMode);
            Assert.True(viewModel.ScanAllDepths);
            Assert.True(viewModel.IncludeInherited);
            Assert.False(viewModel.ResolveIdentities);
            Assert.False(viewModel.ExcludeServiceAccounts);
            Assert.False(viewModel.ExcludeAdminAccounts);
            Assert.False(viewModel.ExpandGroups);
            Assert.False(viewModel.UsePowerShell);
            Assert.False(viewModel.EnableAdvancedAudit);
            Assert.False(viewModel.ComputeEffectiveAccess);
            Assert.False(viewModel.IncludeSharePermissions);
            Assert.False(viewModel.IncludeFiles);
            Assert.False(viewModel.ReadOwnerAndSacl);
            Assert.False(viewModel.CompareBaseline);
        }

        private static void ClearScanInputs(MainViewModel viewModel)
        {
            viewModel.RootPath = string.Empty;
            viewModel.ScanRoots.Clear();
        }
    }
}
