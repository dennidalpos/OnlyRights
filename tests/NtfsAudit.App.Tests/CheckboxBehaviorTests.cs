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
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;
using NtfsAudit.App.ViewModels;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class CheckboxBehaviorTests
    {
        [Fact]
        public void ResolveIdentities_DisablingItClearsDependentIdentityOptions()
        {
            var viewModel = new MainViewModel();

            viewModel.ResolveIdentities = true;
            viewModel.ExpandGroups = true;
            viewModel.UsePowerShell = true;
            viewModel.ExcludeServiceAccounts = true;
            viewModel.ExcludeAdminAccounts = true;

            viewModel.ResolveIdentities = false;

            Assert.False(viewModel.ResolveIdentities);
            Assert.False(viewModel.ExpandGroups);
            Assert.False(viewModel.UsePowerShell);
            Assert.False(viewModel.ExcludeServiceAccounts);
            Assert.False(viewModel.ExcludeAdminAccounts);
            Assert.False(viewModel.IsExpandGroupsEnabled);
            Assert.False(viewModel.IsIdentityOptionsEnabled);
        }

        [Fact]
        public void EnableAdvancedAudit_DisablingItClearsDependentAdvancedOptions()
        {
            var viewModel = new MainViewModel();

            viewModel.EnableAdvancedAudit = true;
            viewModel.ComputeEffectiveAccess = true;
            viewModel.IncludeFiles = true;
            viewModel.ReadOwnerAndSacl = true;
            viewModel.CompareBaseline = true;

            viewModel.EnableAdvancedAudit = false;

            Assert.False(viewModel.EnableAdvancedAudit);
            Assert.False(viewModel.ComputeEffectiveAccess);
            Assert.False(viewModel.IncludeFiles);
            Assert.False(viewModel.ReadOwnerAndSacl);
            Assert.False(viewModel.CompareBaseline);
            Assert.False(viewModel.IsAdvancedAuditEnabled);
        }

        [Fact]
        public void ResultsFilters_KeepAtLeastOneSelectionPerMutuallyExclusiveGroup()
        {
            var viewModel = new MainViewModel();

            viewModel.ShowAllow = false;
            Assert.True(viewModel.ShowDeny);

            viewModel.ShowInherited = false;
            Assert.True(viewModel.ShowExplicit);

            viewModel.ShowEveryone = false;
            viewModel.ShowAuthenticatedUsers = false;
            viewModel.ShowServiceAccounts = false;
            viewModel.ShowAdminAccounts = false;
            viewModel.ShowOtherPrincipals = false;

            Assert.True(viewModel.ShowOtherPrincipals);
        }

        [Fact]
        public void TreeTypeFilters_KeepAtLeastOneSelectionEnabled()
        {
            var viewModel = new MainViewModel();

            viewModel.TreeFilterFilesOnly = false;
            Assert.True(viewModel.TreeFilterFoldersOnly);

            viewModel.TreeFilterFoldersOnly = false;
            Assert.True(viewModel.TreeFilterFilesOnly);
        }

        [Fact]
        public void FolderTreeProvider_ExposesFileFlagForNodesWithAnalyzedFiles()
        {
            var treeMap = new Dictionary<string, List<string>>
            {
                [@"C:\AuditRoot"] = new List<string> { @"C:\AuditRoot\Child" },
                [@"C:\AuditRoot\Child"] = new List<string>()
            };
            var details = new Dictionary<string, FolderDetail>
            {
                [@"C:\AuditRoot\Child"] = new FolderDetail
                {
                    HasFileEntries = true,
                    HasFolderEntries = true
                }
            };
            var provider = new FolderTreeProvider(treeMap, details);

            var child = Assert.Single(provider.GetChildren(@"C:\AuditRoot"));

            Assert.True(child.HasFileEntries);
        }
    }
}
