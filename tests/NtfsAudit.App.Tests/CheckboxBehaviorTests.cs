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
using System.Linq;
using System.Reflection;
using NtfsAudit.Core.Models;
using NtfsAudit.Core.Services;
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

        [Fact]
        public void FolderTreeProvider_ExposesAnalyzedFilesAsLeafNodes()
        {
            var treeMap = new Dictionary<string, List<string>>
            {
                [@"C:\AuditRoot"] = new List<string> { @"C:\AuditRoot\Child" },
                [@"C:\AuditRoot\Child"] = new List<string> { @"C:\AuditRoot\Child\report.xlsx" },
                [@"C:\AuditRoot\Child\report.xlsx"] = new List<string>()
            };
            var details = new Dictionary<string, FolderDetail>
            {
                [@"C:\AuditRoot\Child"] = new FolderDetail
                {
                    HasFileEntries = true,
                    HasFolderEntries = true
                }
            };
            details[@"C:\AuditRoot\Child"].AllEntries.Add(new AceEntry
            {
                FolderPath = @"C:\AuditRoot\Child",
                TargetPath = @"C:\AuditRoot\Child\report.xlsx",
                ResourceType = "File",
                PermissionLayer = PermissionLayer.Ntfs,
                AllowDeny = "Allow",
                RightsSummary = "Read"
            });

            var provider = new FolderTreeProvider(treeMap, details);

            var fileNode = Assert.Single(provider.GetChildren(@"C:\AuditRoot\Child"), node => node.IsFileNode);

            Assert.Equal("FILE", fileNode.TypeLabel);
            Assert.Equal(@"C:\AuditRoot\Child", fileNode.SelectionPath);
            Assert.Equal("report.xlsx", fileNode.DisplayName);
        }

        [Fact]
        public void TreeFilters_AddFileLeafNodesWhenFolderContainsAnalyzedFiles()
        {
            var viewModel = new MainViewModel();
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
            details[@"C:\AuditRoot\Child"].AllEntries.Add(new AceEntry
            {
                FolderPath = @"C:\AuditRoot\Child",
                TargetPath = @"C:\AuditRoot\Child\report.xlsx",
                ResourceType = "File",
                PermissionLayer = PermissionLayer.Ntfs,
                AllowDeny = "Allow",
                RightsSummary = "Read"
            });

            var method = typeof(MainViewModel).GetMethod("ApplyTreeFilters", BindingFlags.Instance | BindingFlags.NonPublic);
            var filteredTree = Assert.IsType<Dictionary<string, List<string>>>(method.Invoke(viewModel, new object[] { treeMap, details, @"C:\AuditRoot" }));

            Assert.Contains(@"C:\AuditRoot\Child\report.xlsx", filteredTree[@"C:\AuditRoot\Child"]);
            Assert.True(filteredTree.ContainsKey(@"C:\AuditRoot\Child\report.xlsx"));
        }
    }
}
