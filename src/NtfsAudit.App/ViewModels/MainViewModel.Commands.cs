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
using System.Windows.Forms;
using WinForms = System.Windows.Forms;
using System.Windows.Threading;
using Win32 = Microsoft.Win32;
using Newtonsoft.Json;
using WpfMessageBox = System.Windows.MessageBox;
using NtfsAudit.App.Cache;
using NtfsAudit.App.Export;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;

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
            RootPath = AuditOutputDirectory;
            if (TryPickFolder(out var selectedPath))
            {
                AuditOutputDirectory = selectedPath;
            }
            RootPath = previousRoot;
        }

        private void OnScanRootsCollectionChanged()
        {
            OnPropertyChanged("CanStart");
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
            var rootToAdd = RootPath;
            var namespacePath = string.Empty;
            var targets = PathResolver.GetDfsTargets(RootPath);
            if (targets != null && targets.Count > 0)
            {
                namespacePath = RootPath;
                var selectedTarget = PromptDfsTargetSelection(RootPath, targets);
                if (string.IsNullOrWhiteSpace(selectedTarget))
                {
                    return;
                }
                rootToAdd = selectedTarget;
            }

            var normalizedRoot = GetScanRootKey(rootToAdd);
            if (ScanRoots.Any(path => string.Equals(GetScanRootKey(path), normalizedRoot, StringComparison.OrdinalIgnoreCase))) return;

            ScanRoots.Add(rootToAdd);
            SelectedScanRoot = rootToAdd;
            if (!string.IsNullOrWhiteSpace(namespacePath))
            {
                _scanRootNamespacePaths[normalizedRoot] = namespacePath;
                _scanRootDfsTargets[normalizedRoot] = rootToAdd;
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
            _scanRootCredentialOverrides.Remove(key);
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
                if (string.IsNullOrWhiteSpace(GlobalCredentialUserName) || string.IsNullOrWhiteSpace(GlobalCredentialPassword))
                {
                    WpfMessageBox.Show(
                        "Inserisci utente e password per salvare le credenziali globali.",
                        "Credenziali scansione",
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
                ProgressText = "Credenziali globali salvate in locale in forma protetta.";
                OnPropertyChanged("SelectedScanRootEffectiveCredentialSource");
                ClearGlobalCredentialCommand.RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                ProgressText = string.Format("Errore salvataggio credenziali globali: {0}", ex.Message);
                WpfMessageBox.Show(ProgressText, "Credenziali scansione", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void ClearGlobalCredential()
        {
            try
            {
                _scanCredentialStore.SaveGlobal(null);
                GlobalCredentialUserName = string.Empty;
                GlobalCredentialPassword = string.Empty;
                ProgressText = "Credenziali globali rimosse. La risoluzione torna a override root oppure utente corrente.";
                OnPropertyChanged("SelectedScanRootEffectiveCredentialSource");
                ClearGlobalCredentialCommand.RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                ProgressText = string.Format("Errore rimozione credenziali globali: {0}", ex.Message);
                WpfMessageBox.Show(ProgressText, "Credenziali scansione", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void SaveSelectedScanRootCredential()
        {
            if (string.IsNullOrWhiteSpace(SelectedScanRoot))
            {
                return;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(SelectedScanRootCredentialUserName) || string.IsNullOrWhiteSpace(SelectedScanRootCredentialPassword))
                {
                    WpfMessageBox.Show(
                        "Inserisci utente e password per salvare l'override della root selezionata.",
                        "Credenziali scansione",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                    return;
                }

                var key = GetScanRootKey(SelectedScanRoot);
                var credential = new ScanCredential
                {
                    UserName = SelectedScanRootCredentialUserName.Trim(),
                    Password = SelectedScanRootCredentialPassword
                };
                _scanRootCredentialOverrides[key] = credential.Clone();
                _scanCredentialStore.SaveOverride(key, credential);
                SelectedScanRootCredentialUserName = credential.UserName;
                SelectedScanRootCredentialPassword = credential.Password;
                ProgressText = string.Format("Override credenziali salvato per la root selezionata: {0}", SelectedScanRoot);
                ClearScanRootCredentialCommand.RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                ProgressText = string.Format("Errore salvataggio override root: {0}", ex.Message);
                WpfMessageBox.Show(ProgressText, "Credenziali scansione", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void ClearSelectedScanRootCredential()
        {
            if (string.IsNullOrWhiteSpace(SelectedScanRoot))
            {
                return;
            }

            try
            {
                var key = GetScanRootKey(SelectedScanRoot);
                _scanRootCredentialOverrides.Remove(key);
                _scanCredentialStore.SaveOverride(key, null);
                SelectedScanRootCredentialUserName = string.Empty;
                SelectedScanRootCredentialPassword = string.Empty;
                ProgressText = string.Format("Override credenziali rimosso per la root selezionata: {0}", SelectedScanRoot);
                ClearScanRootCredentialCommand.RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                ProgressText = string.Format("Errore rimozione override root: {0}", ex.Message);
                WpfMessageBox.Show(ProgressText, "Credenziali scansione", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void SaveScanRootSet()
        {
            var dialog = new Win32.SaveFileDialog
            {
                Filter = "Set cartelle scansione (*.scanroots)|*.scanroots|JSON (*.json)|*.json",
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
                ProgressText = string.Format("Set cartelle salvato: {0}", dialog.FileName);
            }
            catch (Exception ex)
            {
                ProgressText = string.Format("Errore salvataggio set cartelle: {0}", ex.Message);
                WpfMessageBox.Show(ProgressText, "Set cartelle", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void LoadScanRootSet()
        {
            var dialog = new Win32.OpenFileDialog
            {
                Filter = "Set cartelle scansione (*.scanroots;*.json)|*.scanroots;*.json|Tutti i file (*.*)|*.*"
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
                    throw new InvalidDataException("Il file set cartelle non è valido.");
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
                ProgressText = string.Format("Set cartelle caricato: {0} cartelle.", ScanRoots.Count);
            }
            catch (Exception ex)
            {
                ProgressText = string.Format("Errore caricamento set cartelle: {0}", ex.Message);
                WpfMessageBox.Show(ProgressText, "Set cartelle", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private string PromptDfsTargetSelection(string namespacePath, IList<string> targets)
        {
            if (targets == null || targets.Count == 0) return null;
            if (targets.Count == 1) return targets[0];

            using (var form = new WinForms.Form())
            using (var combo = new WinForms.ComboBox())
            using (var okButton = new WinForms.Button())
            using (var cancelButton = new WinForms.Button())
            using (var label = new WinForms.Label())
            {
                form.Text = "Seleziona target DFS";
                form.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
                form.StartPosition = WinForms.FormStartPosition.CenterScreen;
                form.ClientSize = new System.Drawing.Size(760, 130);
                form.MaximizeBox = false;
                form.MinimizeBox = false;

                label.Text = string.Format("Namespace: {0}", namespacePath);
                label.AutoSize = false;
                label.SetBounds(12, 10, 736, 22);

                combo.DropDownStyle = WinForms.ComboBoxStyle.DropDownList;
                combo.SetBounds(12, 38, 736, 24);
                foreach (var target in targets) combo.Items.Add(target);
                combo.SelectedIndex = 0;

                okButton.Text = "OK";
                okButton.SetBounds(592, 84, 75, 30);
                okButton.DialogResult = WinForms.DialogResult.OK;

                cancelButton.Text = "Annulla";
                cancelButton.SetBounds(673, 84, 75, 30);
                cancelButton.DialogResult = WinForms.DialogResult.Cancel;

                form.Controls.Add(label);
                form.Controls.Add(combo);
                form.Controls.Add(okButton);
                form.Controls.Add(cancelButton);
                form.AcceptButton = okButton;
                form.CancelButton = cancelButton;

                var result = form.ShowDialog();
                if (result != WinForms.DialogResult.OK || combo.SelectedItem == null)
                {
                    return null;
                }

                return combo.SelectedItem.ToString();
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
}
