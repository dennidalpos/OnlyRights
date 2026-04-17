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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;

namespace NtfsAudit.App.ViewModels
{
    public partial class MainViewModel
    {
        public string ProgressText
        {
            get { return _progressText; }
            set
            {
                _progressText = value;
                OnPropertyChanged("ProgressText");
            }
        }

        public string CurrentPathText
        {
            get { return _currentPathText; }
            set
            {
                _currentPathText = value;
                OnPropertyChanged("CurrentPathText");
            }
        }

        public int ProcessedCount
        {
            get { return _processedCount; }
            set
            {
                _processedCount = value;
                OnPropertyChanged("ProcessedCount");
            }
        }

        public int ProcessedFilesCount
        {
            get { return _processedFilesCount; }
            set
            {
                _processedFilesCount = value;
                OnPropertyChanged("ProcessedFilesCount");
            }
        }

        public int ErrorCount
        {
            get { return _errorCount; }
            set
            {
                _errorCount = value;
                OnPropertyChanged("ErrorCount");
            }
        }

        public string ElapsedText
        {
            get { return _elapsedText; }
            set
            {
                _elapsedText = value;
                OnPropertyChanged("ElapsedText");
            }
        }

        public string CurrentPathBackground
        {
            get { return _currentPathBackground; }
            set
            {
                _currentPathBackground = value;
                OnPropertyChanged("CurrentPathBackground");
            }
        }

        public string SelectedFolderPath
        {
            get { return _selectedFolderPath; }
            set
            {
                _selectedFolderPath = value;
                OnPropertyChanged("SelectedFolderPath");
                OnPropertyChanged("HasSelectedFolder");
                OnPropertyChanged("HasNoSelectedFolder");
                SelectedFolderName = GetFolderName(_selectedFolderPath);
            }
        }

        public string SelectedFolderName
        {
            get { return _selectedFolderName; }
            private set
            {
                _selectedFolderName = value;
                OnPropertyChanged("SelectedFolderName");
            }
        }

        public string AclFilter
        {
            get { return _aclFilter; }
            set
            {
                _aclFilter = value;
                OnPropertyChanged("AclFilter");
                RefreshAclFilters();
            }
        }

        public bool ColorizeRights
        {
            get { return _colorizeRights; }
            set
            {
                _colorizeRights = value;
                OnPropertyChanged("ColorizeRights");
            }
        }

        public ObservableCollection<string> DfsTargets
        {
            get { return _dfsTargets; }
            private set
            {
                _dfsTargets = value ?? new ObservableCollection<string>();
                OnPropertyChanged("DfsTargets");
                OnPropertyChanged("HasDfsTargets");
                OnPropertyChanged("DfsTargetBackground");
            }
        }

        public bool HasDfsTargets
        {
            get { return _dfsTargets != null && _dfsTargets.Count > 0; }
        }

        public string DfsTargetBackground
        {
            get { return HasDfsTargets ? "#FFE3F2FD" : "White"; }
        }

        public string SelectedDfsTarget
        {
            get { return _selectedDfsTarget; }
            set
            {
                _selectedDfsTarget = value;
                OnPropertyChanged("SelectedDfsTarget");
                PersistUiPreferencesIfAllowed();
            }
        }

        public bool ShowAllow
        {
            get { return _showAllow; }
            set
            {
                _showAllow = value;
                if (!_showAllow && !_showDeny)
                {
                    _showDeny = true;
                    OnPropertyChanged("ShowDeny");
                }
                OnPropertyChanged("ShowAllow");
                RefreshAclFilters();
            }
        }

        public bool ShowDeny
        {
            get { return _showDeny; }
            set
            {
                _showDeny = value;
                if (!_showDeny && !_showAllow)
                {
                    _showAllow = true;
                    OnPropertyChanged("ShowAllow");
                }
                OnPropertyChanged("ShowDeny");
                RefreshAclFilters();
            }
        }

        public bool ShowInherited
        {
            get { return _showInherited; }
            set
            {
                _showInherited = value;
                if (!_showInherited && !_showExplicit)
                {
                    _showExplicit = true;
                    OnPropertyChanged("ShowExplicit");
                }
                OnPropertyChanged("ShowInherited");
                RefreshAclFilters();
            }
        }

        public bool ShowExplicit
        {
            get { return _showExplicit; }
            set
            {
                _showExplicit = value;
                if (!_showExplicit && !_showInherited)
                {
                    _showInherited = true;
                    OnPropertyChanged("ShowInherited");
                }
                OnPropertyChanged("ShowExplicit");
                RefreshAclFilters();
            }
        }

        public bool ShowProtected
        {
            get { return _showProtected; }
            set
            {
                _showProtected = value;
                OnPropertyChanged("ShowProtected");
                RefreshAclFilters();
            }
        }

        public bool ShowDisabled
        {
            get { return _showDisabled; }
            set
            {
                _showDisabled = value;
                OnPropertyChanged("ShowDisabled");
                RefreshAclFilters();
            }
        }

        public bool ShowEveryone
        {
            get { return _showEveryone; }
            set
            {
                _showEveryone = value;
                EnsureAtLeastOnePrincipalCategoryEnabled();
                OnPropertyChanged("ShowEveryone");
                RefreshAclFilters();
            }
        }

        public bool ShowAuthenticatedUsers
        {
            get { return _showAuthenticatedUsers; }
            set
            {
                _showAuthenticatedUsers = value;
                EnsureAtLeastOnePrincipalCategoryEnabled();
                OnPropertyChanged("ShowAuthenticatedUsers");
                RefreshAclFilters();
            }
        }

        public bool ShowServiceAccounts
        {
            get { return _showServiceAccounts; }
            set
            {
                _showServiceAccounts = value;
                EnsureAtLeastOnePrincipalCategoryEnabled();
                OnPropertyChanged("ShowServiceAccounts");
                RefreshAclFilters();
            }
        }

        public bool ShowAdminAccounts
        {
            get { return _showAdminAccounts; }
            set
            {
                _showAdminAccounts = value;
                EnsureAtLeastOnePrincipalCategoryEnabled();
                OnPropertyChanged("ShowAdminAccounts");
                RefreshAclFilters();
            }
        }

        public bool ShowOtherPrincipals
        {
            get { return _showOtherPrincipals; }
            set
            {
                _showOtherPrincipals = value;
                EnsureAtLeastOnePrincipalCategoryEnabled();
                OnPropertyChanged("ShowOtherPrincipals");
                RefreshAclFilters();
            }
        }

        public int SummaryTotalEntries
        {
            get { return _summaryTotalEntries; }
            set
            {
                _summaryTotalEntries = value;
                OnPropertyChanged("SummaryTotalEntries");
            }
        }

        public int SummaryHighRisk
        {
            get { return _summaryHighRisk; }
            set
            {
                _summaryHighRisk = value;
                OnPropertyChanged("SummaryHighRisk");
            }
        }

        public int SummaryMediumRisk
        {
            get { return _summaryMediumRisk; }
            set
            {
                _summaryMediumRisk = value;
                OnPropertyChanged("SummaryMediumRisk");
            }
        }

        public int SummaryLowRisk
        {
            get { return _summaryLowRisk; }
            set
            {
                _summaryLowRisk = value;
                OnPropertyChanged("SummaryLowRisk");
            }
        }

        public int SummaryDenyCount
        {
            get { return _summaryDenyCount; }
            set
            {
                _summaryDenyCount = value;
                OnPropertyChanged("SummaryDenyCount");
            }
        }

        public int SummaryEveryoneCount
        {
            get { return _summaryEveryoneCount; }
            set
            {
                _summaryEveryoneCount = value;
                OnPropertyChanged("SummaryEveryoneCount");
            }
        }

        public int SummaryAuthUsersCount
        {
            get { return _summaryAuthUsersCount; }
            set
            {
                _summaryAuthUsersCount = value;
                OnPropertyChanged("SummaryAuthUsersCount");
            }
        }

        public int SummaryFilesCount
        {
            get { return _summaryFilesCount; }
            set
            {
                _summaryFilesCount = value;
                OnPropertyChanged("SummaryFilesCount");
            }
        }

        public int SummaryBaselineAdded
        {
            get { return _summaryBaselineAdded; }
            set
            {
                _summaryBaselineAdded = value;
                OnPropertyChanged("SummaryBaselineAdded");
            }
        }

        public int SummaryBaselineRemoved
        {
            get { return _summaryBaselineRemoved; }
            set
            {
                _summaryBaselineRemoved = value;
                OnPropertyChanged("SummaryBaselineRemoved");
            }
        }

        public bool IsScanning { get { return _isScanning; } }
        public bool IsNotScanning { get { return !_isScanning; } }
        public bool CanImportAnalysis { get { return !_isScanning && !IsBusy; } }
        public bool IsViewerMode { get { return _isViewerMode; } }
        public bool IsNotViewerMode { get { return !_isViewerMode; } }
        public bool IsScanConfigEnabled { get { return !_isViewerMode && !_isScanning; } }
        public bool TreeFilterExplicitOnly
        {
            get { return _treeFilterExplicitOnly; }
            set { _treeFilterExplicitOnly = value; OnPropertyChanged("TreeFilterExplicitOnly"); ReloadTreeWithFilters(); SaveUiPreferences(); }
        }

        public bool TreeFilterInheritanceDisabledOnly
        {
            get { return _treeFilterInheritanceDisabledOnly; }
            set { _treeFilterInheritanceDisabledOnly = value; OnPropertyChanged("TreeFilterInheritanceDisabledOnly"); ReloadTreeWithFilters(); SaveUiPreferences(); }
        }

        public bool TreeFilterDiffOnly
        {
            get { return _treeFilterDiffOnly; }
            set { _treeFilterDiffOnly = value; OnPropertyChanged("TreeFilterDiffOnly"); ReloadTreeWithFilters(); SaveUiPreferences(); }
        }

        public bool TreeFilterExplicitDenyOnly
        {
            get { return _treeFilterExplicitDenyOnly; }
            set { _treeFilterExplicitDenyOnly = value; OnPropertyChanged("TreeFilterExplicitDenyOnly"); ReloadTreeWithFilters(); SaveUiPreferences(); }
        }

        public bool TreeFilterBaselineMismatchOnly
        {
            get { return _treeFilterBaselineMismatchOnly; }
            set { _treeFilterBaselineMismatchOnly = value; OnPropertyChanged("TreeFilterBaselineMismatchOnly"); ReloadTreeWithFilters(); SaveUiPreferences(); }
        }

        public bool TreeFilterFilesOnly
        {
            get { return _treeFilterFilesOnly; }
            set
            {
                _treeFilterFilesOnly = value;
                OnPropertyChanged("TreeFilterFilesOnly");
                if (!value && !_treeFilterFoldersOnly)
                {
                    _treeFilterFoldersOnly = true;
                    OnPropertyChanged("TreeFilterFoldersOnly");
                }
                ReloadTreeWithFilters();
                SaveUiPreferences();
            }
        }

        public bool TreeFilterFoldersOnly
        {
            get { return _treeFilterFoldersOnly; }
            set
            {
                _treeFilterFoldersOnly = value;
                OnPropertyChanged("TreeFilterFoldersOnly");
                if (!value && !_treeFilterFilesOnly)
                {
                    _treeFilterFilesOnly = true;
                    OnPropertyChanged("TreeFilterFilesOnly");
                }
                ReloadTreeWithFilters();
                SaveUiPreferences();
            }
        }

        public string SelectedPathKind { get { return _selectedPathKind; } private set { _selectedPathKind = value; OnPropertyChanged("SelectedPathKind"); } }
        public string SelectedOwnerSummary { get { return _selectedOwnerSummary; } private set { _selectedOwnerSummary = value; OnPropertyChanged("SelectedOwnerSummary"); } }
        public string SelectedInheritanceSummary { get { return _selectedInheritanceSummary; } private set { _selectedInheritanceSummary = value; OnPropertyChanged("SelectedInheritanceSummary"); } }
        public bool SelectedInheritanceDisabled { get { return _selectedInheritanceDisabled; } private set { _selectedInheritanceDisabled = value; OnPropertyChanged("SelectedInheritanceDisabled"); } }
        public int SelectedTotalAceCount { get { return _selectedTotalAceCount; } private set { _selectedTotalAceCount = value; OnPropertyChanged("SelectedTotalAceCount"); } }
        public int SelectedExplicitAceCount { get { return _selectedExplicitAceCount; } private set { _selectedExplicitAceCount = value; OnPropertyChanged("SelectedExplicitAceCount"); } }
        public int SelectedInheritedAceCount { get { return _selectedInheritedAceCount; } private set { _selectedInheritedAceCount = value; OnPropertyChanged("SelectedInheritedAceCount"); } }
        public int SelectedDenyAceCount { get { return _selectedDenyAceCount; } private set { _selectedDenyAceCount = value; OnPropertyChanged("SelectedDenyAceCount"); } }
        public string SelectedPermissionLayers { get { return _selectedPermissionLayers; } private set { _selectedPermissionLayers = value; OnPropertyChanged("SelectedPermissionLayers"); } }
        public string SelectedRiskSummary { get { return _selectedRiskSummary; } private set { _selectedRiskSummary = value; OnPropertyChanged("SelectedRiskSummary"); } }
        public string SelectedAcquisitionWarnings { get { return _selectedAcquisitionWarnings; } private set { _selectedAcquisitionWarnings = value; OnPropertyChanged("SelectedAcquisitionWarnings"); } }
        public string SelectedScannedAtText { get { return _selectedScannedAtText; } private set { _selectedScannedAtText = value; OnPropertyChanged("SelectedScannedAtText"); } }

        public bool HasScanResult { get { return _scanResult != null; } }
        public bool HasNoScanResult { get { return _scanResult == null; } }
        public bool HasSelectedFolder { get { return _scanResult != null && !string.IsNullOrWhiteSpace(SelectedFolderPath); } }
        public bool HasNoSelectedFolder { get { return _scanResult != null && string.IsNullOrWhiteSpace(SelectedFolderPath); } }
        public bool ShouldShowStartHint { get { return !_isViewerMode && !_isScanning && !IsBusy && ScanRoots.Count == 0 && string.IsNullOrWhiteSpace(RootPath); } }
        public bool IsBusy { get { return _isBusy; } }
        public bool IsNotBusy { get { return !_isBusy; } }
        public string StatusText
        {
            get
            {
                if (_isScanning || IsServiceRuntimeRunning) return LocalizationManager.Text("Common.Running");
                if (_scanResult == null) return LocalizationManager.Text("Common.Idle");
                return LocalizationManager.Text("Common.Finished");
            }
        }
        public string StatusBrush
        {
            get
            {
                if (_isScanning || IsServiceRuntimeRunning) return "#FF2E7D32";
                if (_scanResult == null) return "#FFFFB300";
                return "#FF1565C0";
            }
        }

        public bool CanStart { get { return !_isViewerMode && !_isScanning && !IsBusy && (ScanRoots.Count > 0 || !string.IsNullOrWhiteSpace(RootPath)); } }
        public bool CanStop { get { return !_isViewerMode && !IsBusy && (_isScanning || IsServiceRuntimeRunning); } }
        public bool CanExport { get { return !_isViewerMode && !_isScanning && !IsBusy && _scanResult != null; } }
        public bool HasUnexportedData { get { return !_isViewerMode && _scanResult != null && !_hasExported; } }

        public event PropertyChangedEventHandler PropertyChanged;

        public void SelectFolder(string path)
        {
            if (_scanResult == null || string.IsNullOrWhiteSpace(path)) return;
            var detail = GetFolderDetail(path);
            if (detail == null) return;

            SelectedFolderPath = path;
            GroupEntries.Clear();
            UserEntries.Clear();
            AllEntries.Clear();
            ShareEntries.Clear();
            EffectiveEntries.Clear();

            foreach (var entry in detail.AllEntries)
            {
                AllEntries.Add(entry);
                if (entry.PermissionLayer != PermissionLayer.Ntfs)
                {
                    continue;
                }

                if (string.Equals(entry.PrincipalType, "Group", StringComparison.OrdinalIgnoreCase))
                {
                    GroupEntries.Add(entry);
                }
                else
                {
                    UserEntries.Add(entry);
                }
            }
            foreach (var entry in detail.ShareEntries) ShareEntries.Add(entry);
            foreach (var entry in detail.EffectiveEntries) EffectiveEntries.Add(entry);
            UpdateSummary(detail);
            UpdateSelectedFolderInfo(path, detail);
        }

        private void UpdateSummary(FolderDetail detail)
        {
            if (detail == null || detail.AllEntries == null)
            {
                SummaryTotalEntries = 0;
                SummaryHighRisk = 0;
                SummaryMediumRisk = 0;
                SummaryLowRisk = 0;
                SummaryDenyCount = 0;
                SummaryEveryoneCount = 0;
                SummaryAuthUsersCount = 0;
                SummaryFilesCount = 0;
                SummaryBaselineAdded = 0;
                SummaryBaselineRemoved = 0;
                return;
            }

            var entries = detail.AllEntries;
            SummaryTotalEntries = entries.Count;
            SummaryHighRisk = entries.Count(entry => string.Equals(NormalizeRiskLevel(entry.RiskLevel), "high", StringComparison.Ordinal));
            SummaryMediumRisk = entries.Count(entry => string.Equals(NormalizeRiskLevel(entry.RiskLevel), "medium", StringComparison.Ordinal));
            SummaryLowRisk = entries.Count(entry => string.Equals(NormalizeRiskLevel(entry.RiskLevel), "low", StringComparison.Ordinal));
            SummaryDenyCount = entries.Count(entry => string.Equals(entry.AllowDeny, "Deny", StringComparison.OrdinalIgnoreCase));
            SummaryEveryoneCount = entries.Count(entry => IsEveryone(entry.PrincipalSid, entry.PrincipalName));
            SummaryAuthUsersCount = entries.Count(entry => IsAuthenticatedUsers(entry.PrincipalSid, entry.PrincipalName));
            SummaryFilesCount = detail.HasFileEntries
                ? entries.Count(entry => string.Equals(entry.ResourceType, "File", StringComparison.OrdinalIgnoreCase))
                : 0;
            SummaryBaselineAdded = detail.BaselineSummary == null ? 0 : detail.BaselineSummary.Added.Count;
            SummaryBaselineRemoved = detail.BaselineSummary == null ? 0 : detail.BaselineSummary.Removed.Count;
        }

        public Task<ResolvedPrincipal[]> GetGroupMembersAsync(string groupSid)
        {
            if (string.IsNullOrWhiteSpace(groupSid)) return Task.FromResult(new ResolvedPrincipal[0]);
            return Task.Run(() =>
            {
                var resolver = CreateResolver(UsePowerShell, ResolveScanCredentialForRoot(SelectedScanRoot));
                var members = resolver.GetGroupMembers(groupSid);
                return (members ?? new List<ResolvedPrincipal>()).ToArray();
            });
        }

        public Task<ResolvedPrincipal[]> GetUserGroupsAsync(string userSid)
        {
            if (string.IsNullOrWhiteSpace(userSid)) return Task.FromResult(new ResolvedPrincipal[0]);
            return Task.Run(() =>
            {
                var resolver = CreateResolver(UsePowerShell, ResolveScanCredentialForRoot(SelectedScanRoot));
                var groups = resolver.GetUserGroups(userSid);
                return (groups ?? new List<ResolvedPrincipal>()).ToArray();
            });
        }
    }
}
