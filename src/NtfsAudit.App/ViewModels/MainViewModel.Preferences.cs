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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using NtfsAudit.Core.Models;
using NtfsAudit.App.Services;
using NtfsAudit.Core.Services;

namespace NtfsAudit.App.ViewModels
{
    public partial class MainViewModel
    {
        private void LoadCache()
        {
            var cachePath = _cacheStore.GetCacheFilePath("sid-cache.json");
            _sidNameCache.Load(cachePath);
            _uiPreferencesPath = _cacheStore.GetCacheFilePath("ui-preferences.json");
            LoadUiPreferences();
        }

        private void LoadCredentialSettings()
        {
            var settings = _scanCredentialStore.Load();
            GlobalCredentialUserName = settings.GlobalCredential == null ? string.Empty : settings.GlobalCredential.UserName;
            GlobalCredentialPassword = settings.GlobalCredential == null ? string.Empty : settings.GlobalCredential.Password;
        }

        private ScanCredential ResolveScanCredentialForRoot(string root)
        {
            return ScanCredentialPathPolicy.ResolveRuntimeCredential(root, ResolveConfiguredScanCredentialForRoot(root));
        }

        private ScanCredential ResolveConfiguredScanCredentialForRoot(string root)
        {
            if (HasGlobalCredential)
            {
                return new ScanCredential
                {
                    UserName = GlobalCredentialUserName,
                    Password = GlobalCredentialPassword
                };
            }

            return null;
        }

        private string ResolveConfiguredCredentialSource(string root)
        {
            if (HasGlobalCredential)
            {
                return "Global";
            }

            return "CurrentUser";
        }

        private string ResolveCredentialSource(string root)
        {
            return ScanCredentialPathPolicy.ResolveEffectiveSource(root, ResolveConfiguredCredentialSource(root));
        }

        private string DescribeEffectiveCredentialSource(string root)
        {
            var effectiveSource = ScanCredentialPathPolicy.ResolveEffectiveSource(root, ResolveConfiguredCredentialSource(root));
            return effectiveSource == "Global"
                ? LocalizationManager.Text("Credential.SourceGlobal")
                : LocalizationManager.Text("Credential.SourceCurrentUser");
        }

        private ScanOptions BuildOptionsForRoot(ScanOptions template, string root, bool protectCredentialForService)
        {
            var options = ScanBatchExecutionService.CloneOptions(template, root);
            var credential = ResolveScanCredentialForRoot(root);
            options.CredentialSource = ResolveCredentialSource(root);
            options.Credential = protectCredentialForService
                ? ScanCredentialProtector.ProtectForLocalMachine(credential)
                : credential == null ? null : credential.Clone();
            return options;
        }

        private void SaveCache()
        {
            var cachePath = _cacheStore.GetCacheFilePath("sid-cache.json");
            _sidNameCache.Save(cachePath);
            SaveUiPreferences();
        }

        private void PersistUiPreferencesIfAllowed()
        {
            if (!_suspendUiPreferencePersistence)
            {
                SaveUiPreferences();
            }
        }

        private void LoadUiPreferences()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_uiPreferencesPath) || !File.Exists(_uiPreferencesPath)) return;
                var json = File.ReadAllText(_uiPreferencesPath);
                var prefs = JsonConvert.DeserializeObject<UiPreferences>(json);
                if (prefs == null) return;

                _suspendUiPreferencePersistence = true;

                ShowAllow = prefs.ShowAllow;
                ShowDeny = prefs.ShowDeny;
                ShowInherited = prefs.ShowInherited;
                ShowExplicit = prefs.ShowExplicit;
                ShowProtected = prefs.ShowProtected;
                ShowDisabled = prefs.ShowDisabled;
                ShowEveryone = prefs.ShowEveryone;
                ShowAuthenticatedUsers = prefs.ShowAuthenticatedUsers;
                ShowServiceAccounts = prefs.ShowServiceAccounts;
                ShowAdminAccounts = prefs.ShowAdminAccounts;
                ShowOtherPrincipals = prefs.ShowOtherPrincipals;
                TreeFilterExplicitOnly = prefs.TreeFilterExplicitOnly;
                TreeFilterInheritanceDisabledOnly = prefs.TreeFilterInheritanceDisabledOnly;
                TreeFilterDiffOnly = prefs.TreeFilterDiffOnly;
                TreeFilterExplicitDenyOnly = prefs.TreeFilterExplicitDenyOnly;
                TreeFilterBaselineMismatchOnly = prefs.TreeFilterBaselineMismatchOnly;
                TreeFilterFilesOnly = prefs.TreeFilterFilesOnly;
                TreeFilterFoldersOnly = prefs.TreeFilterFoldersOnly;
                if (!string.IsNullOrWhiteSpace(prefs.RootPath))
                {
                    RootPath = prefs.RootPath;
                }
                AuditOutputDirectory = prefs.AuditOutputDirectory;
                UseWindowsServiceMode = prefs.UseWindowsServiceMode;
                ScanAllDepths = prefs.ScanAllDepths;
                MaxDepth = prefs.MaxDepth;
                IncludeInherited = prefs.IncludeInherited;
                ResolveIdentities = prefs.ResolveIdentities;
                ExcludeServiceAccounts = prefs.ResolveIdentities && prefs.ExcludeServiceAccounts;
                ExcludeAdminAccounts = prefs.ResolveIdentities && prefs.ExcludeAdminAccounts;
                ExpandGroups = prefs.ResolveIdentities && prefs.ExpandGroups;
                UsePowerShell = prefs.ResolveIdentities && prefs.UsePowerShell;
                AnonymizeIdentities = prefs.AnonymizeIdentities;
                EnableAdvancedAudit = prefs.EnableAdvancedAudit;
                ComputeEffectiveAccess = prefs.EnableAdvancedAudit && prefs.ComputeEffectiveAccess;
                IncludeSharePermissions = prefs.EnableAdvancedAudit && prefs.IncludeSharePermissions;
                IncludeFiles = prefs.EnableAdvancedAudit && prefs.IncludeFiles;
                ReadOwnerAndSacl = prefs.EnableAdvancedAudit && prefs.ReadOwnerAndSacl;
                CompareBaseline = prefs.EnableAdvancedAudit && prefs.CompareBaseline;
                SelectedLocale = LocalizationManager.ResolveLocale(prefs.Locale);
                ScanRoots.Clear();
                _scanRootDfsTargets.Clear();
                _scanRootNamespacePaths.Clear();
                if (prefs.ScanRoots != null)
                {
                    foreach (var root in prefs.ScanRoots.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        ScanRoots.Add(root);
                    }
                }
                if (prefs.ScanRootTargets != null)
                {
                    foreach (var target in prefs.ScanRootTargets.Where(item => item != null && !string.IsNullOrWhiteSpace(item.RootPath)))
                    {
                        var key = GetScanRootKey(target.RootPath);
                        _scanRootDfsTargets[key] = target.DfsTarget;
                        if (!string.IsNullOrWhiteSpace(target.NamespacePath))
                        {
                            _scanRootNamespacePaths[key] = target.NamespacePath;
                        }
                    }
                }
                if (ScanRoots.Count > 0)
                {
                    SelectedScanRoot = ScanRoots[0];
                }
                if (!string.IsNullOrWhiteSpace(prefs.SelectedDfsTarget)
                    && DfsTargets.Any(target => string.Equals(target, prefs.SelectedDfsTarget, StringComparison.OrdinalIgnoreCase)))
                {
                    SelectedDfsTarget = DfsTargets.First(target => string.Equals(target, prefs.SelectedDfsTarget, StringComparison.OrdinalIgnoreCase));
                }
            }
            catch
            {
            }
            finally
            {
                _suspendUiPreferencePersistence = false;
            }
        }

        private void SaveUiPreferences()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_uiPreferencesPath)) return;
                var prefs = new UiPreferences
                {
                    ShowAllow = ShowAllow,
                    ShowDeny = ShowDeny,
                    ShowInherited = ShowInherited,
                    ShowExplicit = ShowExplicit,
                    ShowProtected = ShowProtected,
                    ShowDisabled = ShowDisabled,
                    ShowEveryone = ShowEveryone,
                    ShowAuthenticatedUsers = ShowAuthenticatedUsers,
                    ShowServiceAccounts = ShowServiceAccounts,
                    ShowAdminAccounts = ShowAdminAccounts,
                    ShowOtherPrincipals = ShowOtherPrincipals,
                    TreeFilterExplicitOnly = TreeFilterExplicitOnly,
                    TreeFilterInheritanceDisabledOnly = TreeFilterInheritanceDisabledOnly,
                    TreeFilterDiffOnly = TreeFilterDiffOnly,
                    TreeFilterExplicitDenyOnly = TreeFilterExplicitDenyOnly,
                    TreeFilterBaselineMismatchOnly = TreeFilterBaselineMismatchOnly,
                    TreeFilterFilesOnly = TreeFilterFilesOnly,
                    TreeFilterFoldersOnly = TreeFilterFoldersOnly,
                    RootPath = RootPath,
                    SelectedDfsTarget = SelectedDfsTarget,
                    AuditOutputDirectory = AuditOutputDirectory,
                    UseWindowsServiceMode = UseWindowsServiceMode,
                    MaxDepth = MaxDepth,
                    ScanAllDepths = ScanAllDepths,
                    IncludeInherited = IncludeInherited,
                    ResolveIdentities = ResolveIdentities,
                    ExcludeServiceAccounts = ExcludeServiceAccounts,
                    ExcludeAdminAccounts = ExcludeAdminAccounts,
                    ExpandGroups = ExpandGroups,
                    UsePowerShell = UsePowerShell,
                    AnonymizeIdentities = AnonymizeIdentities,
                    EnableAdvancedAudit = EnableAdvancedAudit,
                    ComputeEffectiveAccess = ComputeEffectiveAccess,
                    IncludeSharePermissions = IncludeSharePermissions,
                    IncludeFiles = IncludeFiles,
                    ReadOwnerAndSacl = ReadOwnerAndSacl,
                    CompareBaseline = CompareBaseline,
                    Locale = SelectedLocale == null ? LocalizationManager.DefaultLocale : SelectedLocale.Code,
                    ScanRoots = ScanRoots.ToList(),
                    ScanRootTargets = _scanRootDfsTargets.Select(item => new ScanRootTargetPreference
                    {
                        RootPath = GetScanRootKey(item.Key),
                        DfsTarget = item.Value,
                        NamespacePath = _scanRootNamespacePaths.ContainsKey(item.Key) ? _scanRootNamespacePaths[item.Key] : null
                    }).ToList()
                };
                File.WriteAllText(_uiPreferencesPath, JsonConvert.SerializeObject(prefs, Formatting.Indented));
            }
            catch
            {
            }
        }

        private sealed class UiPreferences
        {
            public bool ShowAllow { get; set; } = true;
            public bool ShowDeny { get; set; } = true;
            public bool ShowInherited { get; set; } = true;
            public bool ShowExplicit { get; set; } = true;
            public bool ShowProtected { get; set; } = true;
            public bool ShowDisabled { get; set; } = true;
            public bool ShowEveryone { get; set; } = true;
            public bool ShowAuthenticatedUsers { get; set; } = true;
            public bool ShowServiceAccounts { get; set; } = true;
            public bool ShowAdminAccounts { get; set; } = true;
            public bool ShowOtherPrincipals { get; set; } = true;
            public bool TreeFilterExplicitOnly { get; set; }
            public bool TreeFilterInheritanceDisabledOnly { get; set; }
            public bool TreeFilterDiffOnly { get; set; }
            public bool TreeFilterExplicitDenyOnly { get; set; }
            public bool TreeFilterBaselineMismatchOnly { get; set; }
            public bool TreeFilterFilesOnly { get; set; } = true;
            public bool TreeFilterFoldersOnly { get; set; } = true;
            public string RootPath { get; set; }
            public string SelectedDfsTarget { get; set; }
            public string AuditOutputDirectory { get; set; }
            public bool UseWindowsServiceMode { get; set; }
            public int MaxDepth { get; set; } = 5;
            public bool ScanAllDepths { get; set; } = true;
            public bool IncludeInherited { get; set; } = true;
            public bool ResolveIdentities { get; set; } = true;
            public bool ExcludeServiceAccounts { get; set; }
            public bool ExcludeAdminAccounts { get; set; }
            public bool ExpandGroups { get; set; } = true;
            public bool UsePowerShell { get; set; } = true;
            public bool AnonymizeIdentities { get; set; }
            public bool EnableAdvancedAudit { get; set; } = true;
            public bool ComputeEffectiveAccess { get; set; } = true;
            public bool IncludeSharePermissions { get; set; } = true;
            public bool IncludeFiles { get; set; }
            public bool ReadOwnerAndSacl { get; set; }
            public bool CompareBaseline { get; set; } = true;
            public string Locale { get; set; } = LocalizationManager.DefaultLocale;
            public List<string> ScanRoots { get; set; }
            public List<ScanRootTargetPreference> ScanRootTargets { get; set; }
        }

        private sealed class ScanRootTargetPreference
        {
            public string RootPath { get; set; }
            public string DfsTarget { get; set; }
            public string NamespacePath { get; set; }
        }

        private sealed class ScanRootSet
        {
            public DateTime CreatedAtUtc { get; set; }
            public string RootPath { get; set; }
            public string AuditOutputDirectory { get; set; }
            public List<string> ScanRoots { get; set; }
            public List<ScanRootTargetPreference> ScanRootTargets { get; set; }
        }
    }
}
