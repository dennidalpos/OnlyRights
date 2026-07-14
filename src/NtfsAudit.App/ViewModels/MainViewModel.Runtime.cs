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
using System.Linq;
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
            return ScanExportPathBuilder.BuildExportFileName(rootPath, extension);
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
            ServiceNextRunText = status != null && status.NextScheduledRunLocal.HasValue
                ? status.NextScheduledRunLocal.Value.ToString("g")
                : LocalizationManager.Text("Settings.NoNextRun");
            if (viewState.IsServiceRuntimeRunning && !_isScanning)
            {
                ProgressText = ServiceRuntimeStatusText;
            }

            RefreshServiceSchedules();
        }

        private void OnLocaleChanged(object sender, EventArgs e)
        {
            RefreshLocalizedState();
        }

        private void RefreshLocalizedState()
        {
            OnPropertyChanged("StatusText");
            OnPropertyChanged("GlobalCredentialStatusText");
            OnPropertyChanged("SelectedScanRootCredentialStatusText");
            OnPropertyChanged("SelectedScanRootEffectiveCredentialSource");
            OnPropertyChanged("AvailableScheduleFrequencyOptions");
            OnPropertyChanged("SelectedScheduleFrequencyOption");
            OnPropertyChanged("AvailableScheduleDayOptions");
            OnPropertyChanged("SelectedScheduleDayOption");
            OnPropertyChanged("ServiceSchedulingAvailabilityText");
            OnPropertyChanged("HasSelectedAcquisitionWarnings");

            if (ScanRoots.Count > 0)
            {
                var selectedRoot = SelectedScanRoot;
                var roots = ScanRoots.ToList();
                _suspendUiPreferencePersistence = true;
                try
                {
                    ScanRoots.Clear();
                    foreach (var root in roots)
                    {
                        ScanRoots.Add(root);
                    }

                    if (!string.IsNullOrWhiteSpace(selectedRoot))
                    {
                        SelectedScanRoot = ScanRoots.FirstOrDefault(root => string.Equals(root, selectedRoot, StringComparison.OrdinalIgnoreCase));
                    }
                }
                finally
                {
                    _suspendUiPreferencePersistence = false;
                }
            }

            RefreshServiceRuntimeStatus();
            if (_scanResult != null && !string.IsNullOrWhiteSpace(SelectedFolderPath))
            {
                var detail = GetFolderDetail(SelectedFolderPath);
                UpdateSelectedFolderInfo(SelectedFolderPath, detail);
            }
        }

        internal static Func<string, bool> ServiceInstalledQueryHook { get; set; }
        internal static Func<string, bool> ServiceRunningQueryHook { get; set; }

        private ServiceStateSnapshot QueryServiceState()
        {
            if (ServiceInstalledQueryHook != null || ServiceRunningQueryHook != null)
            {
                return new ServiceStateSnapshot
                {
                    IsInstalled = ServiceInstalledQueryHook != null && ServiceInstalledQueryHook(ServiceName),
                    IsRunning = ServiceRunningQueryHook != null && ServiceRunningQueryHook(ServiceName)
                };
            }

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

            ProgressText = LocalizationManager.Text("Service.NotInstalledStatus");
            WpfMessageBox.Show(
                ProgressText,
                LocalizationManager.Text("Service.NotInstalledBadge"),
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return false;
        }

        private bool TryValidateScanInputs(IReadOnlyCollection<string> roots, out string message)
        {
            var nonBlockingWarnings = new List<string>();
            if (roots == null || roots.Count == 0)
            {
                message = LocalizationManager.Text("Validation.AddFolder");
                return false;
            }

            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    message = LocalizationManager.Text("Validation.EmptyFolder");
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
                message = LocalizationManager.Format("Validation.OutputInvalid", AuditOutputDirectory);
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

            return LocalizationManager.Text("Validation.SomePathsNotPreverified");
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
                message = LocalizationManager.Text("Validation.ImportInvalidMissingData");
                return false;
            }
            if (string.IsNullOrWhiteSpace(result.TempDataPath))
            {
                message = LocalizationManager.Text("Validation.ImportInvalidMissingDataFile");
                return false;
            }
            var dataPath = PathResolver.ToExtendedPath(result.TempDataPath);
            if (!File.Exists(dataPath))
            {
                message = LocalizationManager.Text("Validation.ImportInvalidDataFileNotFound");
                return false;
            }
            if (new FileInfo(dataPath).Length == 0)
            {
                message = LocalizationManager.Text("Validation.ImportInvalidDataFileEmpty");
                return false;
            }
            if (result.Details == null || result.TreeMap == null)
            {
                message = LocalizationManager.Text("Validation.ImportInvalidStructure");
                return false;
            }
            if (result.TreeMap.Count == 0)
            {
                message = LocalizationManager.Text("Validation.ImportEmptyTree");
                return false;
            }
            if (result.Details.Count == 0)
            {
                message = LocalizationManager.Text("Validation.ImportMissingAclDetails");
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
