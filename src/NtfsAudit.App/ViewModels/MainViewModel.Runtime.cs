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
using System.ComponentModel;
using System.IO;
using System.Windows.Threading;
using Newtonsoft.Json;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;
using WpfMessageBox = System.Windows.MessageBox;

namespace NtfsAudit.App.ViewModels
{
    public partial class MainViewModel
    {
        private string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalDays >= 1)
            {
                return elapsed.ToString(@"d\.hh\:mm\:ss");
            }

            return elapsed.ToString(@"hh\:mm\:ss");
        }

        private string BuildExportPath(string folder, string rootPath, string extension)
        {
            var fileName = BuildExportFileName(rootPath, extension);
            return Path.Combine(folder, fileName);
        }

        private string BuildExportFileName(string rootPath, string extension)
        {
            var safeRoot = rootPath ?? string.Empty;
            var baseName = BuildScanNameFromRoot(safeRoot);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "Root";
            var timestamp = DateTime.Now.ToString("yyyy_MM_dd_HH_mm");
            return string.Format("{0}_{1}.{2}", baseName, timestamp, extension);
        }

        private string ResolveInitialDirectory(string preferredDirectory, string rootPath)
        {
            if (!string.IsNullOrWhiteSpace(preferredDirectory) && Directory.Exists(preferredDirectory))
            {
                return preferredDirectory;
            }

            if (!string.IsNullOrWhiteSpace(rootPath))
            {
                var candidate = rootPath;
                if (!Directory.Exists(candidate))
                {
                    candidate = Path.GetDirectoryName(rootPath);
                }

                if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        private void UpdateLastDirectory(ref string targetDirectory, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                targetDirectory = directory;
            }
        }

        private int ClampMaxDepth(int value)
        {
            return value < 1 ? 1 : value;
        }

        private void InitializeScanTimer()
        {
            _scanTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _scanTimer.Tick += (_, __) =>
            {
                if (!_isScanning) return;
                ElapsedText = FormatElapsed(DateTime.Now - _scanStart);
            };
        }

        private void InitializeServiceStatusMonitor()
        {
            _serviceStatusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _serviceStatusTimer.Tick += (_, __) => RefreshServiceRuntimeStatus();
            _serviceStatusTimer.Start();
            RefreshServiceRuntimeStatus();
        }

        private void RefreshServiceRuntimeStatus()
        {
            var serviceState = QueryServiceState();
            IsServiceInstalled = serviceState.IsInstalled;
            var status = TryReadServiceRuntimeStatus(RuntimePaths.GetServiceStatusPath());
            var viewState = _serviceRuntimeStatusPresenter.Build(serviceState.IsInstalled, serviceState.IsRunning, status);
            ServiceBadgeText = viewState.BadgeText;
            ServiceBadgeBackground = viewState.BadgeBackground;
            ServiceRuntimeStatusText = viewState.StatusText;
            IsServiceRuntimeRunning = viewState.IsServiceRuntimeRunning;
            if (viewState.IsServiceRuntimeRunning && !_isScanning)
            {
                ProgressText = ServiceRuntimeStatusText;
            }
        }

        private ServiceStateSnapshot QueryServiceState()
        {
            var queryResult = ExecuteScCommand(string.Format("query {0}", ServiceName), "query", false);
            if (queryResult.ExitCode == 1060)
            {
                return new ServiceStateSnapshot { IsInstalled = false, IsRunning = false };
            }

            var combinedOutput = string.Format("{0} {1}", queryResult.Output ?? string.Empty, queryResult.Error ?? string.Empty);
            if (queryResult.ExitCode != 0)
            {
                var isNotInstalled = combinedOutput.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0
                    || combinedOutput.IndexOf("non esiste", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isNotInstalled)
                {
                    return new ServiceStateSnapshot { IsInstalled = false, IsRunning = false };
                }

                return new ServiceStateSnapshot { IsInstalled = true, IsRunning = false };
            }

            var running = combinedOutput.IndexOf("RUNNING", StringComparison.OrdinalIgnoreCase) >= 0;
            return new ServiceStateSnapshot { IsInstalled = true, IsRunning = running };
        }

        private bool TryEnsureServiceInstalledForScan()
        {
            var serviceState = QueryServiceState();
            if (serviceState.IsInstalled)
            {
                return true;
            }

            ProgressText = "Servizio Windows non installato: installa NtfsAuditWorker o disattiva 'Esegui tramite servizio Windows'.";
            WpfMessageBox.Show(
                ProgressText,
                "Servizio non installato",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return false;
        }

        private bool TryValidateScanInputs(IReadOnlyCollection<string> roots, out string message)
        {
            var nonBlockingWarnings = new List<string>();
            if (roots == null || roots.Count == 0)
            {
                message = "Aggiungi almeno una cartella da analizzare.";
                return false;
            }

            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    message = "È presente una cartella vuota nell'elenco scansione.";
                    return false;
                }

                var validationResult = ScanPathAccessValidator.ValidateDirectoryRootDetailed(root, ResolveScanCredentialForRoot(root));
                if (validationResult.IsBlocking)
                {
                    message = validationResult.Message;
                    return false;
                }
                if (!string.IsNullOrWhiteSpace(validationResult.Message))
                {
                    nonBlockingWarnings.Add(validationResult.Message);
                }
            }

            if (string.IsNullOrWhiteSpace(AuditOutputDirectory))
            {
                message = BuildValidationWarningMessage(nonBlockingWarnings);
                return true;
            }

            try
            {
                Directory.CreateDirectory(PathResolver.ToExtendedPath(AuditOutputDirectory));
            }
            catch (Exception ex) when (
                ex is UnauthorizedAccessException
                || ex is IOException
                || ex is NotSupportedException
                || ex is ArgumentException)
            {
                message = string.Format("Directory output non valida o non accessibile: {0}", AuditOutputDirectory);
                return false;
            }

            message = BuildValidationWarningMessage(nonBlockingWarnings);
            return true;
        }

        private static string BuildValidationWarningMessage(IReadOnlyCollection<string> warnings)
        {
            if (warnings == null || warnings.Count == 0)
            {
                return null;
            }

            if (warnings.Count == 1)
            {
                foreach (var warning in warnings)
                {
                    return warning;
                }
            }

            return "Alcuni percorsi non sono verificabili in anticipo; la scansione continuera registrando automaticamente i problemi di accesso o compatibilita.";
        }

        private static ServiceRuntimeStatus TryReadServiceRuntimeStatus(string statusPath)
        {
            if (string.IsNullOrWhiteSpace(statusPath) || !File.Exists(statusPath))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<ServiceRuntimeStatus>(File.ReadAllText(statusPath));
            }
            catch
            {
                return null;
            }
        }

        private void StartElapsedTimer()
        {
            _scanStart = DateTime.Now;
            if (_scanTimer != null)
            {
                _scanTimer.Stop();
                _scanTimer.Start();
            }
        }

        private void StopElapsedTimer()
        {
            if (_scanTimer != null)
            {
                _scanTimer.Stop();
            }
        }

        private void SetBusy(bool isBusy)
        {
            if (_isBusy == isBusy) return;
            _isBusy = isBusy;
            UpdateCommands();
        }

        private bool ValidateImportedResult(ScanResult result, out string message)
        {
            if (result == null)
            {
                message = "Analisi importata non valida: dati mancanti.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(result.TempDataPath))
            {
                message = "Analisi importata non valida: file dati non presente.";
                return false;
            }
            var dataPath = PathResolver.ToExtendedPath(result.TempDataPath);
            if (!File.Exists(dataPath))
            {
                message = "Analisi importata non valida: file dati non trovato.";
                return false;
            }
            if (new FileInfo(dataPath).Length == 0)
            {
                message = "Analisi importata non valida: file dati vuoto.";
                return false;
            }
            if (result.Details == null || result.TreeMap == null)
            {
                message = "Analisi importata non valida: struttura dati incompleta (TreeMap/Details mancanti).";
                return false;
            }
            if (result.TreeMap.Count == 0)
            {
                message = "Analisi importata con albero cartelle vuoto.";
                return false;
            }
            if (result.Details.Count == 0)
            {
                message = "Analisi importata senza dettagli ACL.";
                return false;
            }
            message = null;
            return true;
        }

        private void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private void RunOnUi(Action action)
        {
            var dispatcher = System.Windows.Application.Current == null ? null : System.Windows.Application.Current.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished || dispatcher.CheckAccess())
            {
                try
                {
                    action();
                }
                catch
                {
                }
                return;
            }
            try
            {
                dispatcher.Invoke(action);
            }
            catch
            {
            }
        }
    }
}
