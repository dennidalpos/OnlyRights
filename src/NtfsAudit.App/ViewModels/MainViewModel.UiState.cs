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
using System.IO;
using System.Linq;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;

namespace NtfsAudit.App.ViewModels
{
    public partial class MainViewModel
    {
        private void LoadErrors(string path)
        {
            Errors.Clear();
            _errorsTruncated = false;
            if (string.IsNullOrWhiteSpace(path)) return;
            var ioPath = PathResolver.ToExtendedPath(path);
            if (!File.Exists(ioPath)) return;
            var loaded = 0;
            foreach (var line in File.ReadLines(ioPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var error = Newtonsoft.Json.JsonConvert.DeserializeObject<ErrorEntry>(line);
                    if (error != null)
                    {
                        if (loaded < MaxErrorsToLoad)
                        {
                            Errors.Add(error);
                            loaded++;
                        }
                        else
                        {
                            _errorsTruncated = true;
                            break;
                        }
                    }
                }
                catch
                {
                }
            }

            if (_errorsTruncated)
            {
                ProgressText = string.Format("Errori caricati parzialmente ({0}): limita uso RAM. Esporta il report completo per analisi totale.", MaxErrorsToLoad);
            }
        }

        private void RefreshAclFilters()
        {
            if (!_suspendUiPreferencePersistence)
            {
                SaveUiPreferences();
            }
            FilteredGroupEntries.Refresh();
            FilteredUserEntries.Refresh();
            FilteredAllEntries.Refresh();
            FilteredShareEntries.Refresh();
            FilteredEffectiveEntries.Refresh();
        }

        private void EnsureAtLeastOnePrincipalCategoryEnabled()
        {
            if (_showEveryone || _showAuthenticatedUsers || _showServiceAccounts || _showAdminAccounts || _showOtherPrincipals)
            {
                return;
            }

            _showOtherPrincipals = true;
            OnPropertyChanged("ShowOtherPrincipals");
        }

        private bool FilterErrors(object item)
        {
            var error = item as ErrorEntry;
            return error != null;
        }

        private bool FilterAclEntries(object item)
        {
            var entry = item as AceEntry;
            if (entry == null) return false;
            if (!ShowAllow && !ShowDeny)
            {
                return false;
            }
            if (!ShowInherited && !ShowExplicit)
            {
                return false;
            }
            if (!ShowEveryone && !ShowAuthenticatedUsers && !ShowServiceAccounts && !ShowAdminAccounts && !ShowOtherPrincipals)
            {
                return false;
            }
            var isDeny = IsDenyEntry(entry);
            var isAllow = IsAllowEntry(entry);
            if (!ShowAllow && isAllow)
            {
                return false;
            }
            if (!ShowDeny && isDeny)
            {
                return false;
            }
            if (!ShowInherited && entry.IsInherited)
            {
                return false;
            }
            if (!ShowExplicit && !entry.IsInherited)
            {
                return false;
            }
            if (!ShowProtected && entry.IsInheritanceDisabled)
            {
                return false;
            }
            if (!ShowDisabled && entry.IsDisabled)
            {
                return false;
            }

            var isEveryone = IsEveryone(entry.PrincipalSid, entry.PrincipalName);
            var isAuthUsers = IsAuthenticatedUsers(entry.PrincipalSid, entry.PrincipalName);
            var isService = entry.IsServiceAccount || SidClassifier.IsServiceAccountSid(entry.PrincipalSid);
            var isAdmin = entry.IsAdminAccount || SidClassifier.IsPrivilegedGroupSid(entry.PrincipalSid);
            var isOther = !(isEveryone || isAuthUsers || isService || isAdmin);
            if (isEveryone && !ShowEveryone)
            {
                return false;
            }
            if (isAuthUsers && !ShowAuthenticatedUsers)
            {
                return false;
            }
            if (isService && !ShowServiceAccounts)
            {
                return false;
            }
            if (isAdmin && !ShowAdminAccounts)
            {
                return false;
            }
            if (isOther && !ShowOtherPrincipals)
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(AclFilter)) return true;
            var term = AclFilter.Trim();
            if (string.IsNullOrWhiteSpace(term)) return true;
            return MatchesFilter(entry.PrincipalName, term)
                || MatchesFilter(entry.PrincipalSid, term)
                || MatchesFilter(entry.PermissionLayer.ToString(), term)
                || MatchesFilter(entry.AllowDeny, term)
                || MatchesFilter(entry.RightsSummary, term)
                || MatchesFilter(entry.EffectiveRightsSummary, term)
                || MatchesFilter(entry.FolderPath, term)
                || MatchesFilter(GetFolderName(entry.FolderPath), term)
                || MatchesFilter(entry.AuditSummary, term)
                || MatchesFilter(entry.ResourceType, term)
                || MatchesFilter(entry.TargetPath, term)
                || MatchesFilter(entry.Owner, term)
                || MatchesFilter(entry.ShareName, term)
                || MatchesFilter(entry.ShareServer, term)
                || MatchesFilter(entry.RiskLevel, term)
                || MatchesFilter(entry.Source, term)
                || MatchesFilter(entry.PathKind.ToString(), term)
                || MatchesMemberFilter(entry.MemberNames, term);
        }

        private bool IsAllowEntry(AceEntry entry)
        {
            return entry != null && string.Equals(entry.AllowDeny, "Allow", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsDenyEntry(AceEntry entry)
        {
            return entry != null && string.Equals(entry.AllowDeny, "Deny", StringComparison.OrdinalIgnoreCase);
        }

        private string GetFolderName(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) return string.Empty;
            var trimmed = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? folderPath : name;
        }

        private bool MatchesMemberFilter(IEnumerable<string> members, string filter)
        {
            if (members == null) return false;
            return members.Any(member => MatchesFilter(member, filter));
        }

        private bool MatchesFilter(string value, string filter)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsEveryone(string sid, string name)
        {
            return string.Equals(sid, "S-1-1-0", StringComparison.OrdinalIgnoreCase)
                || MatchesFilter(name, "Everyone")
                || MatchesFilter(name, "Tutti");
        }

        private bool IsAuthenticatedUsers(string sid, string name)
        {
            return string.Equals(sid, "S-1-5-11", StringComparison.OrdinalIgnoreCase)
                || MatchesFilter(name, "Authenticated Users")
                || MatchesFilter(name, "Utenti autenticati");
        }

        private void ClearResults()
        {
            _suspendUiPreferencePersistence = true;
            try
            {
                FolderTree.Clear();
                _fullTreeMap = null;
                _currentFilteredTreeMap = null;
                GroupEntries.Clear();
                UserEntries.Clear();
                AllEntries.Clear();
                ShareEntries.Clear();
                EffectiveEntries.Clear();
                Errors.Clear();
                SelectedFolderPath = string.Empty;
                ProcessedCount = 0;
                ProcessedFilesCount = 0;
                ErrorCount = 0;
                ElapsedText = "00:00:00";
                CurrentPathText = string.Empty;
                CurrentPathBackground = "Transparent";
                AclFilter = string.Empty;
                ShowAllow = true;
                ShowDeny = true;
                ShowInherited = true;
                ShowExplicit = true;
                ShowProtected = true;
                ShowDisabled = true;
                ShowEveryone = true;
                ShowAuthenticatedUsers = true;
                ShowServiceAccounts = true;
                ShowAdminAccounts = true;
                ShowOtherPrincipals = true;
                ResetTreeFilters(false);
                UpdateSummary(null);
            }
            finally
            {
                _suspendUiPreferencePersistence = false;
            }

            SaveUiPreferences();
        }

        private void ResetTreeFilters()
        {
            ResetTreeFilters(true);
        }

        private void ResetTreeFilters(bool reloadTree)
        {
            _treeFilterExplicitOnly = false;
            _treeFilterInheritanceDisabledOnly = false;
            _treeFilterDiffOnly = false;
            _treeFilterExplicitDenyOnly = false;
            _treeFilterBaselineMismatchOnly = false;
            _treeFilterFilesOnly = true;
            _treeFilterFoldersOnly = true;
            OnPropertyChanged("TreeFilterExplicitOnly");
            OnPropertyChanged("TreeFilterInheritanceDisabledOnly");
            OnPropertyChanged("TreeFilterDiffOnly");
            OnPropertyChanged("TreeFilterExplicitDenyOnly");
            OnPropertyChanged("TreeFilterBaselineMismatchOnly");
            OnPropertyChanged("TreeFilterFilesOnly");
            OnPropertyChanged("TreeFilterFoldersOnly");

            if (_scanResult != null && _fullTreeMap != null && _fullTreeMap.Count > 0)
            {
                var root = ResolveTreeRoot(_fullTreeMap, _scanResult.RootPath);
                if (!string.IsNullOrWhiteSpace(root)
                    && !string.Equals(RootPath, root, StringComparison.OrdinalIgnoreCase))
                {
                    RootPath = root;
                }
            }

            if (reloadTree)
            {
                ReloadTreeWithFilters();
            }
        }

        private void UpdateDfsTargets()
        {
            var targets = PathResolver.GetDfsTargets(RootPath);
            var nextTargets = new ObservableCollection<string>(targets ?? new List<string>());
            var previousSelection = SelectedDfsTarget;
            DfsTargets = nextTargets;
            if (DfsTargets.Count == 0)
            {
                SelectedDfsTarget = string.Empty;
                return;
            }
            var selected = string.IsNullOrWhiteSpace(previousSelection)
                ? null
                : DfsTargets.FirstOrDefault(target => string.Equals(target, previousSelection, StringComparison.OrdinalIgnoreCase));
            SelectedDfsTarget = selected ?? DfsTargets[0];
            OnPropertyChanged("HasDfsTargets");
            OnPropertyChanged("DfsTargetBackground");
        }

        private void ApplyIdentityDependencies()
        {
            if (_resolveIdentities) return;
            if (_expandGroups)
            {
                _expandGroups = false;
                OnPropertyChanged("ExpandGroups");
            }
            if (_usePowerShell)
            {
                _usePowerShell = false;
                OnPropertyChanged("UsePowerShell");
            }
            if (_excludeServiceAccounts)
            {
                _excludeServiceAccounts = false;
                OnPropertyChanged("ExcludeServiceAccounts");
            }
            if (_excludeAdminAccounts)
            {
                _excludeAdminAccounts = false;
                OnPropertyChanged("ExcludeAdminAccounts");
            }
        }

        private void ApplyAdvancedAuditDependencies()
        {
            if (_enableAdvancedAudit) return;
            if (_computeEffectiveAccess)
            {
                _computeEffectiveAccess = false;
                OnPropertyChanged("ComputeEffectiveAccess");
            }
            if (_includeSharePermissions)
            {
                _includeSharePermissions = false;
                OnPropertyChanged("IncludeSharePermissions");
            }
            if (_includeFiles)
            {
                _includeFiles = false;
                OnPropertyChanged("IncludeFiles");
            }
            if (_readOwnerAndSacl)
            {
                _readOwnerAndSacl = false;
                OnPropertyChanged("ReadOwnerAndSacl");
            }
            if (_compareBaseline)
            {
                _compareBaseline = false;
                OnPropertyChanged("CompareBaseline");
            }
        }

        private void ApplyImportedOptions(ScanOptions options)
        {
            if (options == null) return;
            ScanAllDepths = options.ScanAllDepths;
            if (!options.ScanAllDepths && options.MaxDepth > 0)
            {
                MaxDepth = ClampMaxDepth(options.MaxDepth);
            }
            IncludeInherited = options.IncludeInherited;
            ResolveIdentities = options.ResolveIdentities;
            ExcludeServiceAccounts = options.ResolveIdentities && options.ExcludeServiceAccounts;
            ExcludeAdminAccounts = options.ResolveIdentities && options.ExcludeAdminAccounts;
            ExpandGroups = options.ResolveIdentities && options.ExpandGroups;
            UsePowerShell = options.ResolveIdentities && options.UsePowerShell;
            EnableAdvancedAudit = options.EnableAdvancedAudit;
            ComputeEffectiveAccess = options.EnableAdvancedAudit && options.ComputeEffectiveAccess;
            IncludeSharePermissions = options.EnableAdvancedAudit && options.IncludeSharePermissions;
            IncludeFiles = options.EnableAdvancedAudit && options.IncludeFiles;
            ReadOwnerAndSacl = options.EnableAdvancedAudit && options.ReadOwnerAndSacl;
            CompareBaseline = options.EnableAdvancedAudit && options.CompareBaseline;
        }

        private void UpdateCommands()
        {
            OnPropertyChanged("IsScanning");
            OnPropertyChanged("IsNotScanning");
            OnPropertyChanged("IsScanConfigEnabled");
            OnPropertyChanged("HasScanResult");
            OnPropertyChanged("HasNoScanResult");
            OnPropertyChanged("HasSelectedFolder");
            OnPropertyChanged("HasNoSelectedFolder");
            OnPropertyChanged("HasUnexportedData");
            OnPropertyChanged("StatusText");
            OnPropertyChanged("StatusBrush");
            OnPropertyChanged("CanStart");
            OnPropertyChanged("ShouldShowStartHint");
            OnPropertyChanged("CanStop");
            OnPropertyChanged("CanExport");
            OnPropertyChanged("CanImportAnalysis");
            OnPropertyChanged("IsBusy");
            OnPropertyChanged("IsNotBusy");
            StartCommand.RaiseCanExecuteChanged();
            AddScanRootCommand.RaiseCanExecuteChanged();
            RemoveScanRootCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
            ExportCommand.RaiseCanExecuteChanged();
            ImportAnalysisCommand.RaiseCanExecuteChanged();
            InstallServiceCommand.RaiseCanExecuteChanged();
            UninstallServiceCommand.RaiseCanExecuteChanged();
            ResetTreeFiltersCommand.RaiseCanExecuteChanged();
            CleanupResidualFilesCommand.RaiseCanExecuteChanged();
            SaveScanRootSetCommand.RaiseCanExecuteChanged();
            LoadScanRootSetCommand.RaiseCanExecuteChanged();
            SaveGlobalCredentialCommand.RaiseCanExecuteChanged();
            ClearGlobalCredentialCommand.RaiseCanExecuteChanged();
            SaveScanRootCredentialCommand.RaiseCanExecuteChanged();
            ClearScanRootCredentialCommand.RaiseCanExecuteChanged();
        }
    }
}
