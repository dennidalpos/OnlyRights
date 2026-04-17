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
using System.IO;
using System.Reflection;
using System.Windows;
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
        public void ApplyCompatibleScanOptionsCommand_ReevaluatesCompatibilityForSelectedRoots()
        {
            var viewModel = new MainViewModel();

            viewModel.RootPath = @"C:\Data";
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

            Assert.True(viewModel.UseWindowsServiceMode);
            Assert.False(viewModel.ScanAllDepths);
            Assert.False(viewModel.IncludeInherited);
            Assert.True(viewModel.ResolveIdentities);
            Assert.False(viewModel.IncludeSharePermissions);
            Assert.Contains("SMB", viewModel.PathCompatibilitySummaryText, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ScheduleEditor_DisablesSaveAndShowsValidation_WhenTimeIsInvalid()
        {
            var viewModel = new MainViewModel();
            viewModel.ScanRoots.Add(@"C:\Data");

            typeof(MainViewModel)
                .GetField("_isServiceInstalled", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(viewModel, true);

            viewModel.ScheduleName = "Nightly";
            viewModel.ScheduleTimeText = "25:99";

            Assert.False(viewModel.CanSaveSchedule);
            Assert.Equal("25:99", viewModel.ScheduleTimeText);
            Assert.Contains("HH:mm", viewModel.ScheduleEditorFeedbackText, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ScheduleEditor_ShowsReadyHint_WhenNameRootsAndTimeAreValid()
        {
            var viewModel = new MainViewModel();
            viewModel.ScanRoots.Add(@"C:\Data");

            typeof(MainViewModel)
                .GetField("_isServiceInstalled", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(viewModel, true);

            viewModel.ScheduleName = "Nightly";
            viewModel.ScheduleTimeText = "09:30";

            Assert.True(viewModel.CanSaveSchedule);
            Assert.Equal("09:30", viewModel.ScheduleTimeText);
            Assert.Contains("ready", viewModel.ScheduleEditorFeedbackText, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ServiceInstallationState_RefreshesServiceAndScheduleButtons()
        {
            var viewModel = new MainViewModel();
            viewModel.ScanRoots.Add(@"C:\Data");
            viewModel.ScheduleName = "Nightly";
            viewModel.ScheduleTimeText = "09:30";

            Assert.True(viewModel.InstallServiceCommand.CanExecute(null));
            Assert.False(viewModel.UninstallServiceCommand.CanExecute(null));
            Assert.False(viewModel.StartServiceRuntimeCommand.CanExecute(null));
            Assert.False(viewModel.StopServiceRuntimeCommand.CanExecute(null));
            Assert.False(viewModel.NewScheduleCommand.CanExecute(null));
            Assert.False(viewModel.SaveScheduleCommand.CanExecute(null));
            Assert.False(viewModel.DeleteScheduleCommand.CanExecute(null));

            typeof(MainViewModel)
                .GetProperty("IsServiceInstalled", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .SetValue(viewModel, true);

            Assert.False(viewModel.InstallServiceCommand.CanExecute(null));
            Assert.True(viewModel.UninstallServiceCommand.CanExecute(null));
            Assert.True(viewModel.StartServiceRuntimeCommand.CanExecute(null));
            Assert.True(viewModel.StopServiceRuntimeCommand.CanExecute(null));
            Assert.True(viewModel.NewScheduleCommand.CanExecute(null));
            Assert.True(viewModel.SaveScheduleCommand.CanExecute(null));
            Assert.False(viewModel.DeleteScheduleCommand.CanExecute(null));
            Assert.Contains("ready", viewModel.ScheduleEditorFeedbackText, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void AclFilter_MatchesEnglishRiskAliases_WhenEntriesUseStoredItalianRiskLabels()
        {
            var viewModel = new MainViewModel();
            viewModel.AllEntries.Add(new AceEntry
            {
                PrincipalName = "CONTOSO\\ops-team",
                PrincipalSid = "S-1-5-21-100",
                PrincipalType = "Group",
                PermissionLayer = PermissionLayer.Ntfs,
                AllowDeny = "Allow",
                RiskLevel = "Alto",
                ResourceType = "Folder",
                FolderPath = @"C:\AuditRoot"
            });

            viewModel.AclFilter = "High";

            Assert.Single(viewModel.FilteredAllEntries);
        }

        [Fact]
        public void SelectFolder_UsesLocalizedInheritanceAndRiskSummary()
        {
            LocalizationManager.Apply(new ResourceDictionary(), "en");
            var viewModel = new MainViewModel();
            var scanResult = new ScanResult
            {
                RootPath = @"C:\AuditRoot",
                Details = new Dictionary<string, FolderDetail>
                {
                    [@"C:\AuditRoot"] = new FolderDetail
                    {
                        IsInheritanceDisabled = true,
                        HasFolderEntries = true,
                        AllEntries =
                        {
                            new AceEntry
                            {
                                FolderPath = @"C:\AuditRoot",
                                PrincipalName = @"CONTOSO\ops-team",
                                PrincipalSid = "S-1-5-21-200",
                                PrincipalType = "Group",
                                PermissionLayer = PermissionLayer.Ntfs,
                                AllowDeny = "Allow",
                                RiskLevel = "Alto",
                                ResourceType = "Folder",
                                Owner = @"CONTOSO\owner"
                            }
                        }
                    }
                }
            };

            typeof(MainViewModel)
                .GetField("_scanResult", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(viewModel, scanResult);

            viewModel.SelectFolder(@"C:\AuditRoot");

            Assert.True(viewModel.SelectedInheritanceDisabled);
            Assert.Equal("Inheritance disabled", viewModel.SelectedInheritanceSummary);
            Assert.Equal("High: 1, Medium: 0, Low: 0", viewModel.SelectedRiskSummary);
        }

        private static void ClearScanInputs(MainViewModel viewModel)
        {
            viewModel.RootPath = string.Empty;
            viewModel.ScanRoots.Clear();
        }
    }
}
