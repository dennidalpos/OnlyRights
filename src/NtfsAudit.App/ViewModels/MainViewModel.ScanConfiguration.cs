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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;
using WpfMessageBox = System.Windows.MessageBox;

namespace NtfsAudit.App.ViewModels
{
    public partial class MainViewModel
    {
        public bool IsElevated
        {
            get { return _isElevated; }
        }

        public bool IsNotElevated
        {
            get { return !_isElevated; }
        }

        public bool IsServiceRuntimeRunning
        {
            get { return _isServiceRuntimeRunning; }
            private set
            {
                _isServiceRuntimeRunning = value;
                OnPropertyChanged("IsServiceRuntimeRunning");
                OnPropertyChanged("StatusText");
                OnPropertyChanged("StatusBrush");
                OnPropertyChanged("CanStop");
                if (StopCommand != null)
                {
                    StopCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsServiceInstalled
        {
            get { return _isServiceInstalled; }
            private set
            {
                _isServiceInstalled = value;
                OnPropertyChanged("IsServiceInstalled");
                if (InstallServiceCommand != null)
                {
                    InstallServiceCommand.RaiseCanExecuteChanged();
                }
                if (UninstallServiceCommand != null)
                {
                    UninstallServiceCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string ServiceRuntimeStatusText
        {
            get { return _serviceRuntimeStatusText; }
            private set
            {
                _serviceRuntimeStatusText = value;
                OnPropertyChanged("ServiceRuntimeStatusText");
            }
        }

        public string ServiceBadgeText
        {
            get { return _serviceBadgeText; }
            private set
            {
                _serviceBadgeText = value;
                OnPropertyChanged("ServiceBadgeText");
            }
        }

        public string ServiceBadgeBackground
        {
            get { return _serviceBadgeBackground; }
            private set
            {
                _serviceBadgeBackground = value;
                OnPropertyChanged("ServiceBadgeBackground");
            }
        }

        public ObservableCollection<FolderNodeViewModel> FolderTree { get; private set; }
        public ObservableCollection<string> ScanRoots { get; private set; }
        public ObservableCollection<AceEntry> GroupEntries { get; private set; }
        public ObservableCollection<AceEntry> UserEntries { get; private set; }
        public ObservableCollection<AceEntry> AllEntries { get; private set; }
        public ObservableCollection<AceEntry> ShareEntries { get; private set; }
        public ObservableCollection<AceEntry> EffectiveEntries { get; private set; }
        public ObservableCollection<ErrorEntry> Errors { get; private set; }
        public ICollectionView FilteredErrors { get; private set; }
        public ICollectionView FilteredGroupEntries { get; private set; }
        public ICollectionView FilteredUserEntries { get; private set; }
        public ICollectionView FilteredAllEntries { get; private set; }
        public ICollectionView FilteredShareEntries { get; private set; }
        public ICollectionView FilteredEffectiveEntries { get; private set; }

        public RelayCommand BrowseCommand { get; private set; }
        public RelayCommand AddScanRootCommand { get; private set; }
        public RelayCommand RemoveScanRootCommand { get; private set; }
        public RelayCommand BrowseOutputDirectoryCommand { get; private set; }
        public RelayCommand InstallServiceCommand { get; private set; }
        public RelayCommand UninstallServiceCommand { get; private set; }
        public RelayCommand StartCommand { get; private set; }
        public RelayCommand StopCommand { get; private set; }
        public RelayCommand ExportCommand { get; private set; }
        public RelayCommand ImportAnalysisCommand { get; private set; }
        public RelayCommand ResetTreeFiltersCommand { get; private set; }
        public RelayCommand CleanupResidualFilesCommand { get; private set; }
        public RelayCommand SaveScanRootSetCommand { get; private set; }
        public RelayCommand LoadScanRootSetCommand { get; private set; }
        public RelayCommand SaveGlobalCredentialCommand { get; private set; }
        public RelayCommand ClearGlobalCredentialCommand { get; private set; }
        public RelayCommand SaveScanRootCredentialCommand { get; private set; }
        public RelayCommand ClearScanRootCredentialCommand { get; private set; }

        public System.Collections.Generic.IReadOnlyList<LocaleOption> AvailableLocales
        {
            get { return LocalizationManager.SupportedLocales; }
        }

        public LocaleOption SelectedLocale
        {
            get { return _selectedLocale; }
            set
            {
                var next = LocalizationManager.ResolveLocale(value == null ? null : value.Code);
                if (_selectedLocale != null && string.Equals(_selectedLocale.Code, next.Code, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _selectedLocale = next;
                LocalizationManager.Apply(next.Code);
                OnPropertyChanged("SelectedLocale");
                PersistUiPreferencesIfAllowed();
                RefreshLocalizedState();
            }
        }

        public string RootPath
        {
            get { return _rootPath; }
            set
            {
                _rootPath = value;
                OnPropertyChanged("RootPath");
                UpdateDfsTargets();
                OnPropertyChanged("CanStart");
                OnPropertyChanged("ShouldShowStartHint");
                AddScanRootCommand.RaiseCanExecuteChanged();
                StartCommand.RaiseCanExecuteChanged();
                PersistUiPreferencesIfAllowed();
            }
        }

        public string SelectedScanRoot
        {
            get { return _selectedScanRoot; }
            set
            {
                _selectedScanRoot = value;
                OnPropertyChanged("SelectedScanRoot");
                LoadSelectedScanRootDfsTargets();
                LoadSelectedScanRootCredential();
                OnPropertyChanged("HasSelectedScanRoot");
                RemoveScanRootCommand.RaiseCanExecuteChanged();
                SaveScanRootCredentialCommand.RaiseCanExecuteChanged();
                ClearScanRootCredentialCommand.RaiseCanExecuteChanged();
            }
        }

        public ObservableCollection<string> SelectedScanRootDfsTargets
        {
            get { return _selectedScanRootDfsTargets; }
            private set
            {
                _selectedScanRootDfsTargets = value;
                OnPropertyChanged("SelectedScanRootDfsTargets");
                OnPropertyChanged("HasSelectedScanRootDfsTargets");
            }
        }

        public bool HasSelectedScanRootDfsTargets
        {
            get { return SelectedScanRootDfsTargets != null && SelectedScanRootDfsTargets.Count > 0; }
        }

        public string SelectedScanRootDfsTarget
        {
            get { return _selectedScanRootDfsTarget; }
            set
            {
                _selectedScanRootDfsTarget = value;
                if (!string.IsNullOrWhiteSpace(SelectedScanRoot))
                {
                    var key = GetScanRootKey(SelectedScanRoot);
                    string namespacePath;
                    if (_scanRootNamespacePaths.TryGetValue(key, out namespacePath) && !string.IsNullOrWhiteSpace(value))
                    {
                        var newKey = GetScanRootKey(value);
                        var index = ScanRoots.IndexOf(SelectedScanRoot);
                        if (index >= 0 && !string.Equals(newKey, key, StringComparison.OrdinalIgnoreCase))
                        {
                            var duplicate = ScanRoots
                                .Where((_, idx) => idx != index)
                                .Any(path => string.Equals(GetScanRootKey(path), newKey, StringComparison.OrdinalIgnoreCase));
                            if (duplicate)
                            {
                                WpfMessageBox.Show("Il target DFS selezionato è già presente in elenco.", "Target DFS", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                                return;
                            }

                            ScanRoots[index] = value;
                            _scanRootNamespacePaths.Remove(key);
                            _scanRootNamespacePaths[newKey] = namespacePath;
                            _scanRootDfsTargets.Remove(key);
                            _scanRootDfsTargets[newKey] = value;
                            if (_scanRootCredentialOverrides.TryGetValue(key, out var overrideCredential))
                            {
                                _scanRootCredentialOverrides.Remove(key);
                                _scanRootCredentialOverrides[newKey] = overrideCredential;
                            }
                            _selectedScanRoot = value;
                            OnPropertyChanged("SelectedScanRoot");
                            LoadSelectedScanRootDfsTargets();
                            LoadSelectedScanRootCredential();
                            OnPropertyChanged("SelectedScanRootDfsTarget");
                            OnPropertyChanged("CanStart");
                            StartCommand.RaiseCanExecuteChanged();
                            return;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(value))
                    {
                        _scanRootDfsTargets.Remove(key);
                    }
                    else
                    {
                        _scanRootDfsTargets[key] = value;
                    }
                }
                OnPropertyChanged("SelectedScanRootDfsTarget");
                SaveUiPreferences();
            }
        }

        public string GlobalCredentialUserName
        {
            get { return _globalCredentialUserName; }
            set
            {
                _globalCredentialUserName = value;
                OnPropertyChanged("GlobalCredentialUserName");
                OnPropertyChanged("HasGlobalCredential");
                OnPropertyChanged("GlobalCredentialStatusText");
                if (ClearGlobalCredentialCommand != null)
                {
                    ClearGlobalCredentialCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string GlobalCredentialPassword
        {
            get { return _globalCredentialPassword; }
            set
            {
                _globalCredentialPassword = value;
                OnPropertyChanged("GlobalCredentialPassword");
                OnPropertyChanged("HasGlobalCredential");
                OnPropertyChanged("GlobalCredentialStatusText");
                if (ClearGlobalCredentialCommand != null)
                {
                    ClearGlobalCredentialCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool HasGlobalCredential
        {
            get
            {
                return !string.IsNullOrWhiteSpace(GlobalCredentialUserName)
                    && !string.IsNullOrWhiteSpace(GlobalCredentialPassword);
            }
        }

        public string GlobalCredentialStatusText
        {
            get
            {
                if (!HasGlobalCredential)
                {
                    return LocalizationManager.Text("Credential.GlobalNone");
                }

                return LocalizationManager.Format("Credential.GlobalActive", GlobalCredentialUserName);
            }
        }

        public string SelectedScanRootCredentialUserName
        {
            get { return _selectedScanRootCredentialUserName; }
            set
            {
                _selectedScanRootCredentialUserName = value;
                OnPropertyChanged("SelectedScanRootCredentialUserName");
                OnPropertyChanged("HasSelectedScanRootCredentialOverride");
                OnPropertyChanged("SelectedScanRootCredentialStatusText");
                OnPropertyChanged("SelectedScanRootEffectiveCredentialSource");
                if (ClearScanRootCredentialCommand != null)
                {
                    ClearScanRootCredentialCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string SelectedScanRootCredentialPassword
        {
            get { return _selectedScanRootCredentialPassword; }
            set
            {
                _selectedScanRootCredentialPassword = value;
                OnPropertyChanged("SelectedScanRootCredentialPassword");
                OnPropertyChanged("HasSelectedScanRootCredentialOverride");
                OnPropertyChanged("SelectedScanRootCredentialStatusText");
                OnPropertyChanged("SelectedScanRootEffectiveCredentialSource");
                if (ClearScanRootCredentialCommand != null)
                {
                    ClearScanRootCredentialCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool HasSelectedScanRootCredentialOverride
        {
            get
            {
                return !string.IsNullOrWhiteSpace(SelectedScanRootCredentialUserName)
                    && !string.IsNullOrWhiteSpace(SelectedScanRootCredentialPassword);
            }
        }

        public string SelectedScanRootCredentialStatusText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(SelectedScanRoot))
                {
                    return LocalizationManager.Text("Credential.SelectRoot");
                }

                if (HasSelectedScanRootCredentialOverride)
                {
                    return LocalizationManager.Format("Credential.OverrideActive", SelectedScanRootCredentialUserName);
                }

                return LocalizationManager.Text("Credential.OverrideNone");
            }
        }

        public string SelectedScanRootEffectiveCredentialSource
        {
            get
            {
                return DescribeEffectiveCredentialSource(SelectedScanRoot);
            }
        }

        public bool HasSelectedScanRoot
        {
            get { return !string.IsNullOrWhiteSpace(SelectedScanRoot); }
        }

        public string AuditOutputDirectory
        {
            get { return _auditOutputDirectory; }
            set
            {
                _auditOutputDirectory = value;
                OnPropertyChanged("AuditOutputDirectory");
                PersistUiPreferencesIfAllowed();
            }
        }

        public bool UseWindowsServiceMode
        {
            get { return _useWindowsServiceMode; }
            set
            {
                _useWindowsServiceMode = value;
                OnPropertyChanged("UseWindowsServiceMode");
                PersistUiPreferencesIfAllowed();
            }
        }

        public int MaxDepth
        {
            get { return _maxDepth; }
            set
            {
                _maxDepth = ClampMaxDepth(value);
                OnPropertyChanged("MaxDepth");
                PersistUiPreferencesIfAllowed();
            }
        }

        public bool ScanAllDepths
        {
            get { return _scanAllDepths; }
            set
            {
                _scanAllDepths = value;
                if (!_scanAllDepths)
                {
                    MaxDepth = ClampMaxDepth(_maxDepth);
                }
                OnPropertyChanged("ScanAllDepths");
                OnPropertyChanged("IsMaxDepthEnabled");
                PersistUiPreferencesIfAllowed();
            }
        }

        public bool IsMaxDepthEnabled
        {
            get { return !_scanAllDepths; }
        }

        public bool IncludeInherited
        {
            get { return _includeInherited; }
            set
            {
                _includeInherited = value;
                OnPropertyChanged("IncludeInherited");
                PersistUiPreferencesIfAllowed();
            }
        }

        public bool ResolveIdentities
        {
            get { return _resolveIdentities; }
            set
            {
                _resolveIdentities = value;
                OnPropertyChanged("ResolveIdentities");
                OnPropertyChanged("IsExpandGroupsEnabled");
                OnPropertyChanged("IsIdentityOptionsEnabled");
                ApplyIdentityDependencies();
                PersistUiPreferencesIfAllowed();
            }
        }

        public bool IsExpandGroupsEnabled
        {
            get { return _resolveIdentities; }
        }

        public bool IsIdentityOptionsEnabled
        {
            get { return _resolveIdentities; }
        }

        public bool ExcludeServiceAccounts
        {
            get { return _excludeServiceAccounts; }
            set
            {
                _excludeServiceAccounts = value;
                OnPropertyChanged("ExcludeServiceAccounts");
                PersistUiPreferencesIfAllowed();
            }
        }

        public bool ExcludeAdminAccounts
        {
            get { return _excludeAdminAccounts; }
            set
            {
                _excludeAdminAccounts = value;
                OnPropertyChanged("ExcludeAdminAccounts");
                PersistUiPreferencesIfAllowed();
            }
        }

        public bool ExpandGroups
        {
            get { return _expandGroups; }
            set
            {
                _expandGroups = value;
                OnPropertyChanged("ExpandGroups");
                PersistUiPreferencesIfAllowed();
            }
        }

        public bool UsePowerShell
        {
            get { return _usePowerShell; }
            set
            {
                _usePowerShell = value;
                OnPropertyChanged("UsePowerShell");
                PersistUiPreferencesIfAllowed();
            }
        }

        public bool EnableAdvancedAudit
        {
            get { return _enableAdvancedAudit; }
            set
            {
                _enableAdvancedAudit = value;
                OnPropertyChanged("EnableAdvancedAudit");
                OnPropertyChanged("IsAdvancedAuditEnabled");
                ApplyAdvancedAuditDependencies();
                PersistUiPreferencesIfAllowed();
            }
        }

        public bool IsAdvancedAuditEnabled
        {
            get { return _enableAdvancedAudit; }
        }

        public bool ComputeEffectiveAccess
        {
            get { return _computeEffectiveAccess; }
            set
            {
                _computeEffectiveAccess = value;
                OnPropertyChanged("ComputeEffectiveAccess");
                PersistUiPreferencesIfAllowed();
            }
        }

        public bool IncludeSharePermissions
        {
            get { return _includeSharePermissions; }
            set
            {
                _includeSharePermissions = value;
                OnPropertyChanged("IncludeSharePermissions");
                PersistUiPreferencesIfAllowed();
            }
        }

        public bool IncludeFiles
        {
            get { return _includeFiles; }
            set
            {
                _includeFiles = value;
                OnPropertyChanged("IncludeFiles");
                PersistUiPreferencesIfAllowed();
            }
        }

        public bool ReadOwnerAndSacl
        {
            get { return _readOwnerAndSacl; }
            set
            {
                _readOwnerAndSacl = value;
                OnPropertyChanged("ReadOwnerAndSacl");
                PersistUiPreferencesIfAllowed();
            }
        }

        public bool CompareBaseline
        {
            get { return _compareBaseline; }
            set
            {
                _compareBaseline = value;
                OnPropertyChanged("CompareBaseline");
                PersistUiPreferencesIfAllowed();
            }
        }

    }
}
