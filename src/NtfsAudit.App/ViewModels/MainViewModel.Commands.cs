using NtfsAudit.Core.Logging;
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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Threading;
using Win32 = Microsoft.Win32;
using Newtonsoft.Json;
using WpfMessageBox = System.Windows.MessageBox;
using NtfsAudit.Core.Cache;
using NtfsAudit.Core.Export;
using NtfsAudit.Core.Models;
using NtfsAudit.App.Services;
using NtfsAudit.Core.Services;

namespace NtfsAudit.App.ViewModels
{
    public partial class MainViewModel
    {
        private void Browse()
        {
            if (_isViewerMode) return;
            if (TryPickFolder(out var selectedPath))
            {
                RootPath = selectedPath;
            }
        }

        private void BrowseOutputDirectory()
        {
            if (_isViewerMode) return;
            var previousRoot = RootPath;
            _suspendUiPreferencePersistence = true;
            try
            {
                RootPath = AuditOutputDirectory;
                if (TryPickFolder(out var selectedPath))
                {
                    AuditOutputDirectory = selectedPath;
                }
                RootPath = previousRoot;
            }
            finally
            {
                _suspendUiPreferencePersistence = false;
            }
        }

        private void OnScanRootsCollectionChanged()
        {
            RefreshCompatibilityState();
            OnPropertyChanged("CanStart");
            OnPropertyChanged("ShouldShowStartHint");
            StartCommand.RaiseCanExecuteChanged();
            SaveScanRootSetCommand.RaiseCanExecuteChanged();
            if (!_suspendUiPreferencePersistence)
            {
                SaveUiPreferences();
            }
        }

        private void AddScanRoot()
        {
            if (string.IsNullOrWhiteSpace(RootPath)) return;
            var selection = ScanRootSelectionResolver.Resolve(RootPath, PathResolver.GetDfsTargets(RootPath), SelectedDfsTarget);
            if (selection.RequiresExplicitSelection)
            {
                WpfMessageBox.Show(
                    LocalizationManager.Text("Validation.SelectDfsTarget"),
                    LocalizationManager.Text("Dialog.DfsTarget"),
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
                return;
            }

            var normalizedRoot = GetScanRootKey(selection.RootPath);
            if (ScanRoots.Any(path => string.Equals(GetScanRootKey(path), normalizedRoot, StringComparison.OrdinalIgnoreCase))) return;

            ScanRoots.Add(selection.RootPath);
            SelectedScanRoot = selection.RootPath;
            if (!string.IsNullOrWhiteSpace(selection.NamespacePath))
            {
                _scanRootNamespacePaths[normalizedRoot] = selection.NamespacePath;
                _scanRootDfsTargets[normalizedRoot] = selection.RootPath;
            }
            OnPropertyChanged("CanStart");
            StartCommand.RaiseCanExecuteChanged();
            SaveUiPreferences();
        }

        private void RemoveScanRoot()
        {
            if (string.IsNullOrWhiteSpace(SelectedScanRoot)) return;
            var key = GetScanRootKey(SelectedScanRoot);
            _scanRootDfsTargets.Remove(key);
            _scanRootNamespacePaths.Remove(key);
            ScanRoots.Remove(SelectedScanRoot);
            SelectedScanRoot = ScanRoots.Count > 0 ? ScanRoots[0] : null;
            OnPropertyChanged("CanStart");
            StartCommand.RaiseCanExecuteChanged();
            SaveUiPreferences();
        }

        private void SaveGlobalCredential()
        {
            try
            {
                var inputState = ClassifyGlobalCredentialInput(GlobalCredentialUserName, GlobalCredentialPassword);
                if (inputState == GlobalCredentialInputState.Empty)
                {
                    _scanCredentialStore.SaveGlobal(null);
                    GlobalCredentialUserName = string.Empty;
                    GlobalCredentialPassword = string.Empty;
                    ProgressText = LocalizationManager.Text("Progress.CredentialsGlobalCurrentUser");
                    OnPropertyChanged("SelectedScanRootEffectiveCredentialSource");
                    ClearGlobalCredentialCommand.RaiseCanExecuteChanged();
                    return;
                }

                if (inputState == GlobalCredentialInputState.Partial)
                {
                    WpfMessageBox.Show(
                        LocalizationManager.Text("Validation.CredentialsGlobalPartial"),
                        LocalizationManager.Text("Dialog.ScanCredentials"),
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                var credential = new ScanCredential
                {
                    UserName = GlobalCredentialUserName.Trim(),
                    Password = GlobalCredentialPassword
                };
                _scanCredentialStore.SaveGlobal(credential);
                GlobalCredentialUserName = credential.UserName;
                GlobalCredentialPassword = credential.Password;
                ProgressText = LocalizationManager.Text("Progress.CredentialsGlobalSaved");
                OnPropertyChanged("SelectedScanRootEffectiveCredentialSource");
                ClearGlobalCredentialCommand.RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                ProgressText = LocalizationManager.Format("Progress.CredentialsGlobalSaveError", ex.Message);
                WpfMessageBox.Show(ProgressText, LocalizationManager.Text("Dialog.ScanCredentials"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void ClearGlobalCredential()
        {
            try
            {
                _scanCredentialStore.SaveGlobal(null);
                GlobalCredentialUserName = string.Empty;
                GlobalCredentialPassword = string.Empty;
                ProgressText = LocalizationManager.Text("Progress.CredentialsGlobalRemoved");
                OnPropertyChanged("SelectedScanRootEffectiveCredentialSource");
                ClearGlobalCredentialCommand.RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                ProgressText = LocalizationManager.Format("Progress.CredentialsGlobalRemoveError", ex.Message);
                WpfMessageBox.Show(ProgressText, LocalizationManager.Text("Dialog.ScanCredentials"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        internal static GlobalCredentialInputState ClassifyGlobalCredentialInput(string userName, string password)
        {
            var hasUserName = !string.IsNullOrWhiteSpace(userName);
            var hasPassword = !string.IsNullOrWhiteSpace(password);
            if (!hasUserName && !hasPassword)
            {
                return GlobalCredentialInputState.Empty;
            }

            return hasUserName && hasPassword
                ? GlobalCredentialInputState.Complete
                : GlobalCredentialInputState.Partial;
        }

        private void ApplyCompatibleScanOptions()
        {
            RefreshCompatibilityState();
            ProgressText = PathCompatibilitySummaryText;
        }

        private void SaveScanRootSet()
        {
            var dialog = new Win32.SaveFileDialog
            {
                Filter = LocalizationManager.Text("FileDialog.ScanRootSetSaveFilter"),
                DefaultExt = ".scanroots",
                AddExtension = true,
                FileName = string.Format("scanroots_{0}", DateTime.Now.ToString("yyyy_MM_dd_HH_mm"))
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var payload = new ScanRootSet
                {
                    CreatedAtUtc = DateTime.UtcNow,
                    RootPath = RootPath,
                    AuditOutputDirectory = AuditOutputDirectory,
                    ScanRoots = ScanRoots.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    ScanRootTargets = _scanRootDfsTargets.Select(item => new ScanRootTargetPreference
                    {
                        RootPath = GetScanRootKey(item.Key),
                        DfsTarget = item.Value,
                        NamespacePath = _scanRootNamespacePaths.ContainsKey(item.Key) ? _scanRootNamespacePaths[item.Key] : null
                    }).ToList()
                };
                File.WriteAllText(dialog.FileName, JsonConvert.SerializeObject(payload, Formatting.Indented));
                ProgressText = LocalizationManager.Format("Progress.ScanRootSetSaved", dialog.FileName);
            }
            catch (Exception ex)
            {
                ProgressText = LocalizationManager.Format("Progress.ScanRootSetSaveError", ex.Message);
                WpfMessageBox.Show(ProgressText, LocalizationManager.Text("Dialog.FolderSet"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void LoadScanRootSet()
        {
            var dialog = new Win32.OpenFileDialog
            {
                Filter = LocalizationManager.Text("FileDialog.ScanRootSetOpenFilter")
            };
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                var json = File.ReadAllText(dialog.FileName);
                var payload = JsonConvert.DeserializeObject<ScanRootSet>(json);
                if (payload == null)
                {
                    throw new InvalidDataException(LocalizationManager.Text("FileDialog.ScanRootSetInvalid"));
                }

                ScanRoots.Clear();
                if (payload.ScanRoots != null)
                {
                    foreach (var root in payload.ScanRoots.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        ScanRoots.Add(root);
                    }
                }

                _scanRootDfsTargets.Clear();
                _scanRootNamespacePaths.Clear();
                if (payload.ScanRootTargets != null)
                {
                    foreach (var target in payload.ScanRootTargets.Where(item => item != null && !string.IsNullOrWhiteSpace(item.RootPath)))
                    {
                        var key = GetScanRootKey(target.RootPath);
                        if (!string.IsNullOrWhiteSpace(target.DfsTarget))
                        {
                            _scanRootDfsTargets[key] = target.DfsTarget;
                        }
                        if (!string.IsNullOrWhiteSpace(target.NamespacePath))
                        {
                            _scanRootNamespacePaths[key] = target.NamespacePath;
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(payload.RootPath))
                {
                    RootPath = payload.RootPath;
                }
                if (!string.IsNullOrWhiteSpace(payload.AuditOutputDirectory))
                {
                    AuditOutputDirectory = payload.AuditOutputDirectory;
                }

                SelectedScanRoot = ScanRoots.Count > 0 ? ScanRoots[0] : null;
                OnPropertyChanged("CanStart");
                StartCommand.RaiseCanExecuteChanged();
                SaveScanRootSetCommand.RaiseCanExecuteChanged();
                SaveUiPreferences();
                ProgressText = LocalizationManager.Format("Progress.ScanRootSetLoaded", ScanRoots.Count);
            }
            catch (Exception ex)
            {
                ProgressText = LocalizationManager.Format("Progress.ScanRootSetLoadError", ex.Message);
                WpfMessageBox.Show(ProgressText, LocalizationManager.Text("Dialog.FolderSet"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void LoadSelectedScanRootDfsTargets()
        {
            if (string.IsNullOrWhiteSpace(SelectedScanRoot))
            {
                SelectedScanRootDfsTargets = new ObservableCollection<string>();
                SelectedScanRootDfsTarget = string.Empty;
                return;
            }

            var selectedKey = GetScanRootKey(SelectedScanRoot);
            string namespacePath;
            var sourcePath = _scanRootNamespacePaths.TryGetValue(selectedKey, out namespacePath) && !string.IsNullOrWhiteSpace(namespacePath)
                ? namespacePath
                : SelectedScanRoot;
            var targets = PathResolver.GetDfsTargets(sourcePath) ?? new List<string>();
            SelectedScanRootDfsTargets = new ObservableCollection<string>(targets);
            if (SelectedScanRootDfsTargets.Count == 0)
            {
                SelectedScanRootDfsTarget = string.Empty;
                return;
            }

            string target;
            if (_scanRootDfsTargets.TryGetValue(selectedKey, out target)
                && SelectedScanRootDfsTargets.Any(item => string.Equals(item, target, StringComparison.OrdinalIgnoreCase)))
            {
                SelectedScanRootDfsTarget = target;
                return;
            }

            if (SelectedScanRootDfsTargets.Any(item => string.Equals(item, SelectedScanRoot, StringComparison.OrdinalIgnoreCase)))
            {
                SelectedScanRootDfsTarget = SelectedScanRoot;
                return;
            }

            SelectedScanRootDfsTarget = SelectedScanRootDfsTargets[0];
        }

        private string GetEffectiveScanRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return root;
            string target;
            return _scanRootDfsTargets.TryGetValue(GetScanRootKey(root), out target) && !string.IsNullOrWhiteSpace(target)
                ? target
                : root;
        }

        private static string GetScanRootKey(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return string.Empty;
            return root.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

    }

    internal enum GlobalCredentialInputState
    {
        Empty,
        Partial,
        Complete
    }
}
