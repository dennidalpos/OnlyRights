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
                    throw new InvalidDataException("Il file set cartelle non Ã¨ valido.");
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

        private void InstallService()
        {
            try
            {
                var serviceState = QueryServiceState();
                if (serviceState.IsInstalled && serviceState.IsRunning)
                {
                    ProgressText = "Servizio Windows giÃ  installato e attivo.";
                    RefreshServiceRuntimeStatus();
                    return;
                }

                var serviceCommand = ResolveServiceInstallCommand();
                if (string.IsNullOrWhiteSpace(serviceCommand))
                {
                    WpfMessageBox.Show("NtfsAudit.Service.exe (o NtfsAudit.Service.dll) non trovato. Compila/publisha il progetto service e copia l'output vicino all'app, oppure usa una build che includa il service.", "Installazione servizio", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                var createArguments = string.Format("create {0} binPath= {1} start= auto", ServiceName, BuildServiceBinPathForSc(serviceCommand));
                var createResult = ExecuteScCommand(createArguments, "create", false);
                if (createResult.ExitCode == 1073)
                {
                    ExecuteScCommand(string.Format("config {0} binPath= {1} start= auto", ServiceName, BuildServiceBinPathForSc(serviceCommand)), "config");
                }
                else if (createResult.ExitCode != 0)
                {
                    ThrowScOperationFailed("create", createResult);
                }

                ExecuteScCommand(string.Format("description {0} \"Servizio scansione NTFS Audit\"", ServiceName), "description");
                ExecuteScCommand(string.Format("start {0}", ServiceName), "start", false);
                ProgressText = "Servizio Windows installato.";
                RefreshServiceRuntimeStatus();
                WpfMessageBox.Show("Servizio Windows installato correttamente.", "Installazione servizio", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(string.Format("Errore installazione servizio: {0}", ex.Message), "Installazione servizio", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void UninstallService()
        {
            try
            {
                TerminateServiceProcesses();

                var stopResult = ExecuteScCommand(string.Format("stop {0}", ServiceName), "stop", false);
                if (stopResult.ExitCode != 0 && stopResult.ExitCode != 1060 && stopResult.ExitCode != 1062)
                {
                    ThrowScOperationFailed("stop", stopResult);
                }

                CleanupResidualFiles(false, true);

                var deleteResult = ExecuteScCommand(string.Format("delete {0}", ServiceName), "delete", false);
                if (deleteResult.ExitCode != 0 && deleteResult.ExitCode != 1060)
                {
                    ThrowScOperationFailed("delete", deleteResult);
                }

                ProgressText = "Servizio Windows disinstallato.";
                RefreshServiceRuntimeStatus();
                WpfMessageBox.Show("Servizio Windows disinstallato correttamente.", "Disinstallazione servizio", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(string.Format("Errore disinstallazione servizio: {0}", ex.Message), "Disinstallazione servizio", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                RefreshServiceRuntimeStatus();
                UpdateCommands();
            }
        }

        private void TerminateServiceProcesses()
        {
            var serviceProcessNames = new[] { "NtfsAudit.Service", "NtfsAuditWorker" };
            foreach (var processName in serviceProcessNames)
            {
                Process[] processes;
                try
                {
                    processes = Process.GetProcessesByName(processName);
                }
                catch
                {
                    continue;
                }

                foreach (var process in processes)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                            process.WaitForExit(5000);
                        }
                    }
                    catch
                    {
                        // Ignora processi non terminabili e prosegue con la disinstallazione.
                    }
                }
            }
        }

        private static ScCommandResult ExecuteScCommand(string arguments, string operation = null, bool throwOnError = true)
        {
            var result = RunScCommand(arguments, false);
            if (result.ExitCode == 0)
            {
                return result;
            }

            var initialErrorText = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
            if (result.ExitCode == 5 || (!string.IsNullOrWhiteSpace(initialErrorText) && initialErrorText.IndexOf("accesso negato", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var elevated = RunScCommand(arguments, true);
                if (elevated.ExitCode == 0)
                {
                    return elevated;
                }

                if (string.IsNullOrWhiteSpace(elevated.Error) && string.IsNullOrWhiteSpace(elevated.Output) && !string.IsNullOrWhiteSpace(initialErrorText))
                {
                    elevated.Error = initialErrorText;
                }

                result = elevated;
            }

            if (throwOnError)
            {
                ThrowScOperationFailed(operation, result);
            }

            return result;
        }

        private static void ThrowScOperationFailed(string operation, ScCommandResult result)
        {
            var errorText = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
            if (string.IsNullOrWhiteSpace(errorText))
            {
                errorText = BuildScFallbackError(result.ExitCode);
            }

            var op = string.IsNullOrWhiteSpace(operation) ? "sc" : operation;
            throw new InvalidOperationException(string.Format("Operazione servizio '{0}' non riuscita (exit code {1}): {2}", op, result.ExitCode, errorText));
        }

        private static string BuildScFallbackError(int exitCode)
        {
            switch (exitCode)
            {
                case 5:
                    return "Accesso negato. Esegui NtfsAudit.App come amministratore e conferma il prompt UAC.";
                case 1060:
                    return "Il servizio specificato non esiste come servizio installato.";
                case 1073:
                    return "Il servizio esiste giÃ .";
                case -1:
                    return "Impossibile avviare sc.exe o richiesta UAC annullata.";
                default:
                    return "Errore sconosciuto durante esecuzione di sc.exe";
            }
        }

        private static ScCommandResult RunScCommand(string arguments, bool runAsAdmin)
        {
            var scPath = ResolveScExecutablePath();
            if (runAsAdmin)
            {
                var elevated = RunScCommandElevatedWithPowerShell(scPath, arguments);
                if (elevated != null)
                {
                    return elevated;
                }

                return RunScCommandElevatedDirect(scPath, arguments);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = scPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return new ScCommandResult { ExitCode = -1, Error = "Impossibile avviare sc.exe" };
                    }

                    process.WaitForExit();
                    return new ScCommandResult
                    {
                        ExitCode = process.ExitCode,
                        Output = process.StandardOutput.ReadToEnd(),
                        Error = process.StandardError.ReadToEnd()
                    };
                }
            }
            catch (Exception ex)
            {
                return new ScCommandResult { ExitCode = -1, Error = ex.Message };
            }
        }

        private static ScCommandResult RunScCommandElevatedWithPowerShell(string scPath, string arguments)
        {
            var powerShellPath = ResolvePowerShellExecutablePath();
            if (string.IsNullOrWhiteSpace(powerShellPath))
            {
                return null;
            }

            var escapedScPath = EscapePowerShellSingleQuoted(scPath);
            var escapedArguments = EscapePowerShellSingleQuoted(arguments);
            var psArguments = string.Format(
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"& {{ $p = Start-Process -FilePath '{0}' -ArgumentList '{1}' -Verb RunAs -Wait -PassThru; exit $p.ExitCode }}\"",
                escapedScPath,
                escapedArguments);

            var startInfo = new ProcessStartInfo
            {
                FileName = powerShellPath,
                Arguments = psArguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return new ScCommandResult { ExitCode = -1, Error = "Impossibile avviare PowerShell per elevare sc.exe" };
                    }

                    process.WaitForExit();
                    return new ScCommandResult
                    {
                        ExitCode = process.ExitCode,
                        Output = process.StandardOutput.ReadToEnd(),
                        Error = process.StandardError.ReadToEnd()
                    };
                }
            }
            catch
            {
                return null;
            }
        }

        private static ScCommandResult RunScCommandElevatedDirect(string scPath, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = scPath,
                Arguments = arguments,
                UseShellExecute = true,
                CreateNoWindow = false,
                Verb = "runas"
            };

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return new ScCommandResult { ExitCode = -1, Error = "Impossibile avviare sc.exe in elevazione" };
                    }

                    process.WaitForExit();
                    return new ScCommandResult
                    {
                        ExitCode = process.ExitCode,
                        Output = string.Empty,
                        Error = string.Empty
                    };
                }
            }
            catch (Exception ex)
            {
                return new ScCommandResult { ExitCode = -1, Error = ex.Message };
            }
        }

        private static string EscapePowerShellSingleQuoted(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("'", "''");
        }

        private static string ResolveScExecutablePath()
        {
            var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (!string.IsNullOrWhiteSpace(systemDir))
            {
                var scPath = Path.Combine(systemDir, "sc.exe");
                if (File.Exists(scPath))
                {
                    return scPath;
                }
            }

            var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(windir))
            {
                var sysnative = Path.Combine(windir, "sysnative", "sc.exe");
                if (File.Exists(sysnative))
                {
                    return sysnative;
                }
            }

            return "sc.exe";
        }

        private static string ResolvePowerShellExecutablePath()
        {
            var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (!string.IsNullOrWhiteSpace(systemDir))
            {
                var powershellPath = Path.Combine(systemDir, "WindowsPowerShell", "v1.0", "powershell.exe");
                if (File.Exists(powershellPath))
                {
                    return powershellPath;
                }
            }

            var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(windir))
            {
                return Path.Combine(windir, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            }

            return "powershell.exe";
        }

        private sealed class ServiceStateSnapshot
        {
            public bool IsInstalled { get; set; }
            public bool IsRunning { get; set; }
        }

        private sealed class ScCommandResult
        {
            public int ExitCode { get; set; }
            public string Output { get; set; }
            public string Error { get; set; }
        }

        private string ResolveServiceInstallCommand()
        {
            var appBase = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new List<string>
            {
                Path.Combine(appBase, "NtfsAudit.Service.exe"),
                Path.Combine(appBase, "NtfsAudit.Service", "NtfsAudit.Service.exe"),
                Path.Combine(appBase, "Service", "NtfsAudit.Service.exe"),
                Path.Combine(appBase, "NtfsAudit.Service.dll"),
                Path.Combine(appBase, "NtfsAudit.Service", "NtfsAudit.Service.dll"),
                Path.Combine(appBase, "Service", "NtfsAudit.Service.dll")
            };

            var parent = Directory.GetParent(appBase);
            for (var i = 0; i < 4 && parent != null; i++)
            {
                candidates.Add(Path.Combine(parent.FullName, "NtfsAudit.Service", "bin", "Release", "net8.0-windows", "NtfsAudit.Service.exe"));
                candidates.Add(Path.Combine(parent.FullName, "NtfsAudit.Service", "bin", "Debug", "net8.0-windows", "NtfsAudit.Service.exe"));
                candidates.Add(Path.Combine(parent.FullName, "NtfsAudit.Service", "bin", "Release", "net8.0-windows", "NtfsAudit.Service.dll"));
                candidates.Add(Path.Combine(parent.FullName, "NtfsAudit.Service", "bin", "Debug", "net8.0-windows", "NtfsAudit.Service.dll"));
                parent = parent.Parent;
            }

            var serviceBinary = candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
            if (string.IsNullOrWhiteSpace(serviceBinary)) return null;
            return serviceBinary;
        }

        private string BuildServiceBinPathForSc(string serviceCommand)
        {
            if (string.IsNullOrWhiteSpace(serviceCommand))
            {
                throw new InvalidOperationException("Percorso servizio non valido.");
            }

            if (serviceCommand.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return QuoteForSc(serviceCommand);
            }

            var dotnetHost = ResolveDotnetHostPath();
            // Per sc.exe il valore binPath con due token (host + dll) deve essere una singola stringa,
            // con virgolette interne escaped e valore esterno quotato.
            return string.Format("\"\\\"{0}\\\" \\\"{1}\\\"\"",
                SanitizeForSc(dotnetHost),
                SanitizeForSc(serviceCommand));
        }

        private static string SanitizeForSc(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return value.Replace("\"", string.Empty);
        }

        private static string QuoteForSc(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "\"\"";
            return string.Format("\"{0}\"", value.Replace("\"", string.Empty));
        }

        private static string ResolveDotnetHostPath()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var preferred = string.IsNullOrWhiteSpace(programFiles)
                ? null
                : Path.Combine(programFiles, "dotnet", "dotnet.exe");
            if (!string.IsNullOrWhiteSpace(preferred) && File.Exists(preferred))
            {
                return preferred;
            }

            return "dotnet.exe";
        }

        private bool TryPickFolder(out string selectedPath)
        {
            selectedPath = null;
            try
            {
                var dialog = (IFileDialog)new FileOpenDialog();
                dialog.SetTitle("Seleziona cartella");
                dialog.GetOptions(out var options);
                options |= (uint)(FileDialogOptions.PickFolders
                    | FileDialogOptions.ForceFileSystem
                    | FileDialogOptions.PathMustExist
                    | FileDialogOptions.NoChangeDirectory);
                dialog.SetOptions(options);
                AddNetworkPlaces(dialog);

                if (!string.IsNullOrWhiteSpace(RootPath))
                {
                    if (SHCreateItemFromParsingName(RootPath, IntPtr.Zero, typeof(IShellItem).GUID, out var folder) == 0)
                    {
                        dialog.SetFolder(folder);
                    }
                }

                var result = dialog.Show(IntPtr.Zero);
                if (result == HResultCanceled)
                {
                    return false;
                }
                if (result != 0)
                {
                    return false;
                }
                dialog.GetResult(out var item);
                item.GetDisplayName(ShellItemDisplayName.FileSystemPath, out var pszString);
                selectedPath = Marshal.PtrToStringUni(pszString);
                Marshal.FreeCoTaskMem(pszString);
                return !string.IsNullOrWhiteSpace(selectedPath);
            }
            catch
            {
                return false;
            }
        }

        private void AddNetworkPlaces(IFileDialog dialog)
        {
            return;
        }

        private bool TryAddPlace(IFileDialog dialog, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItem).GUID, out var place) == 0)
            {
                dialog.AddPlace(place, (int)FileDialogAddPlace.Bottom);
                return true;
            }

            return false;
        }

        private const int HResultCanceled = unchecked((int)0x800704C7);

        [Flags]
        private enum FileDialogOptions : uint
        {
            PickFolders = 0x00000020,
            ForceFileSystem = 0x00000040,
            NoChangeDirectory = 0x00000008,
            PathMustExist = 0x00000800
        }

        private enum FileDialogAddPlace
        {
            Top = 0,
            Bottom = 1
        }

        private enum ShellItemDisplayName : uint
        {
            FileSystemPath = 0x80058000
        }

        [ComImport]
        [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialog
        {
        }

        [ComImport]
        [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig]
            int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(ShellItemDisplayName sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            out IShellItem ppv);
    }
}
