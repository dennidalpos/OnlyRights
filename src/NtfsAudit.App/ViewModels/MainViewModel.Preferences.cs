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
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;

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

            _scanRootCredentialOverrides.Clear();
            foreach (var entry in settings.RootOverrides)
            {
                if (entry.Value == null || !entry.Value.IsConfigured)
                {
                    continue;
                }

                _scanRootCredentialOverrides[entry.Key] = entry.Value.Clone();
            }

            LoadSelectedScanRootCredential();
        }

        private void LoadSelectedScanRootCredential()
        {
            if (string.IsNullOrWhiteSpace(SelectedScanRoot))
            {
                SelectedScanRootCredentialUserName = string.Empty;
                SelectedScanRootCredentialPassword = string.Empty;
                return;
            }

            if (_scanRootCredentialOverrides.TryGetValue(GetScanRootKey(SelectedScanRoot), out var overrideCredential)
                && overrideCredential != null
                && overrideCredential.IsConfigured)
            {
                SelectedScanRootCredentialUserName = overrideCredential.UserName;
                SelectedScanRootCredentialPassword = overrideCredential.Password;
                return;
            }

            SelectedScanRootCredentialUserName = string.Empty;
            SelectedScanRootCredentialPassword = string.Empty;
        }

        private ScanCredential ResolveScanCredentialForRoot(string root)
        {
            if (!string.IsNullOrWhiteSpace(root)
                && _scanRootCredentialOverrides.TryGetValue(GetScanRootKey(root), out var overrideCredential)
                && overrideCredential != null
                && overrideCredential.IsConfigured)
            {
                return overrideCredential.Clone();
            }

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

        private string ResolveCredentialSource(string root)
        {
            if (!string.IsNullOrWhiteSpace(root)
                && _scanRootCredentialOverrides.TryGetValue(GetScanRootKey(root), out var overrideCredential)
                && overrideCredential != null
                && overrideCredential.IsConfigured)
            {
                return "RootOverride";
            }

            if (HasGlobalCredential)
            {
                return "Global";
            }

            return "CurrentUser";
        }

        private ScanOptions BuildOptionsForRoot(ScanOptions template, string root, bool protectCredentialForService)
        {
            var options = CloneOptions(template, root);
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
                AuditOutputDirectory = prefs.AuditOutputDirectory;
                UseWindowsServiceMode = prefs.UseWindowsServiceMode;
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
                    AuditOutputDirectory = AuditOutputDirectory,
                    UseWindowsServiceMode = UseWindowsServiceMode,
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
            public string AuditOutputDirectory { get; set; }
            public bool UseWindowsServiceMode { get; set; }
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
