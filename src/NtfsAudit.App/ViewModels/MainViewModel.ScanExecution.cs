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
        private void StartScan()
        {
            if (_isViewerMode) return;
            var roots = ScanRoots.Count > 0
                ? ScanRoots.Select(GetEffectiveScanRoot).ToList()
                : new List<string> { string.IsNullOrWhiteSpace(SelectedDfsTarget) ? RootPath : SelectedDfsTarget };
            roots = roots
                .Select(path => string.IsNullOrWhiteSpace(path) ? string.Empty : PathResolver.FromExtendedPath(path).Trim())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!TryValidateScanInputs(roots, out var validationMessage))
            {
                ProgressText = validationMessage;
                WpfMessageBox.Show(validationMessage, LocalizationManager.Text("Dialog.ScanValidation"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return;
            }

            if (!string.IsNullOrWhiteSpace(validationMessage))
            {
                ProgressText = validationMessage;
            }

            if (UseWindowsServiceMode)
            {
                EnqueueServiceScan(roots);
                return;
            }

            _isScanning = true;
            CurrentPathText = string.Empty;
            ElapsedText = "00:00:00";
            StartElapsedTimer();
            UpdateCommands();
            ClearResults();

            _cts = new CancellationTokenSource();
            var optionsTemplate = new ScanOptions
            {
                MaxDepth = ScanAllDepths ? int.MaxValue : MaxDepth,
                ScanAllDepths = ScanAllDepths,
                IncludeInherited = IncludeInherited,
                ResolveIdentities = ResolveIdentities,
                ExcludeServiceAccounts = ResolveIdentities && ExcludeServiceAccounts,
                ExcludeAdminAccounts = ResolveIdentities && ExcludeAdminAccounts,
                ExpandGroups = ResolveIdentities && ExpandGroups,
                UsePowerShell = ResolveIdentities && UsePowerShell,
                EnableAdvancedAudit = EnableAdvancedAudit,
                ComputeEffectiveAccess = EnableAdvancedAudit && ComputeEffectiveAccess,
                IncludeSharePermissions = EnableAdvancedAudit && IncludeSharePermissions,
                IncludeFiles = EnableAdvancedAudit && IncludeFiles,
                ReadOwnerAndSacl = EnableAdvancedAudit && ReadOwnerAndSacl,
                CompareBaseline = EnableAdvancedAudit && CompareBaseline,
                OutputDirectory = AuditOutputDirectory
            };

            Task.Run(() => ExecuteBatchScan(roots, optionsTemplate, _cts.Token));
        }

        private void EnqueueServiceScan(List<string> roots)
        {
            try
            {
                var jobsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NtfsAudit", "jobs");
                Directory.CreateDirectory(jobsRoot);
                var optionsTemplate = new ScanOptions
                {
                    MaxDepth = ScanAllDepths ? int.MaxValue : MaxDepth,
                    ScanAllDepths = ScanAllDepths,
                    IncludeInherited = IncludeInherited,
                    ResolveIdentities = ResolveIdentities,
                    ExcludeServiceAccounts = ResolveIdentities && ExcludeServiceAccounts,
                    ExcludeAdminAccounts = ResolveIdentities && ExcludeAdminAccounts,
                    ExpandGroups = ResolveIdentities && ExpandGroups,
                    UsePowerShell = ResolveIdentities && UsePowerShell,
                    EnableAdvancedAudit = EnableAdvancedAudit,
                    ComputeEffectiveAccess = EnableAdvancedAudit && ComputeEffectiveAccess,
                    IncludeSharePermissions = EnableAdvancedAudit && IncludeSharePermissions,
                    IncludeFiles = EnableAdvancedAudit && IncludeFiles,
                    ReadOwnerAndSacl = EnableAdvancedAudit && ReadOwnerAndSacl,
                    CompareBaseline = EnableAdvancedAudit && CompareBaseline,
                    OutputDirectory = AuditOutputDirectory
                };
                var job = new ServiceScanJob
                {
                    JobId = Guid.NewGuid().ToString("N"),
                    CreatedAtUtc = DateTime.UtcNow,
                    ScanOptions = roots.Select(root => BuildOptionsForRoot(optionsTemplate, root, true)).ToList()
                };

                if (!TryEnsureServiceInstalledForScan())
                {
                    return;
                }

                var jobFile = Path.Combine(jobsRoot, string.Format("job_{0}.json", job.JobId));
                File.WriteAllText(jobFile, JsonConvert.SerializeObject(job, Formatting.Indented));
                ExecuteScCommand(string.Format("start {0}", ServiceName), "start", false);
                ProgressText = LocalizationManager.Text("Progress.ServiceJobSent");
                RefreshServiceRuntimeStatus();
            }
            catch (Exception ex)
            {
                ProgressText = string.Format("Errore invio job al servizio: {0}", ex.Message);
                WpfMessageBox.Show(ProgressText, LocalizationManager.Text("Dialog.WindowsService"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void ExecuteBatchScan(List<string> roots, ScanOptions optionsTemplate, CancellationToken token)
        {
            var aggregateResult = new ScanResult
            {
                RootPath = roots == null || roots.Count == 0 ? RootPath : roots[0],
                Details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase),
                TreeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                ScanOptions = CloneOptions(optionsTemplate, roots == null || roots.Count == 0 ? RootPath : roots[0]),
                ScannedAtUtc = DateTime.UtcNow
            };

            try
            {
                foreach (var root in roots)
                {
                    token.ThrowIfCancellationRequested();
                    var options = BuildOptionsForRoot(optionsTemplate, root, false);
                    var result = ExecuteScan(options, token);
                    if (result == null)
                    {
                        continue;
                    }

                    if (result.TreeMap == null || result.TreeMap.Count == 0)
                    {
                        result.TreeMap = BuildTreeMapFromDetails(result.Details, result.RootPath);
                    }

                    var previousTempDataPath = aggregateResult.TempDataPath;
                    var previousErrorPath = aggregateResult.ErrorPath;
                    MergeScanResult(aggregateResult, result);
                    if (!string.IsNullOrWhiteSpace(previousTempDataPath)
                        && !string.Equals(previousTempDataPath, aggregateResult.TempDataPath, StringComparison.OrdinalIgnoreCase))
                    {
                        RuntimeCleanupService.TryDeleteFile(previousTempDataPath);
                    }
                    if (!string.IsNullOrWhiteSpace(previousErrorPath)
                        && !string.Equals(previousErrorPath, aggregateResult.ErrorPath, StringComparison.OrdinalIgnoreCase))
                    {
                        RuntimeCleanupService.TryDeleteFile(previousErrorPath);
                    }

                    if (!string.IsNullOrWhiteSpace(options.OutputDirectory))
                    {
                        var outputDirectory = PathResolver.FromExtendedPath(options.OutputDirectory).Trim();
                        Directory.CreateDirectory(PathResolver.ToExtendedPath(outputDirectory));
                        var outputFile = BuildExportPath(outputDirectory, root, "ntaudit");
                        _analysisArchive.Export(result, root, outputFile);
                    }
                }
            }
            finally
            {
                RunOnUi(() =>
                {
                    if (aggregateResult.Details != null && aggregateResult.Details.Count > 0)
                    {
                        _scanResult = aggregateResult;
                        _hasExported = false;
                        _fullTreeMap = null;
                        LoadTree(_scanResult);
                        var rootToSelect = ResolveTreeRoot(_scanResult.TreeMap, _scanResult.RootPath);
                        if (!string.IsNullOrWhiteSpace(rootToSelect))
                        {
                            RootPath = rootToSelect;
                            SelectFolder(rootToSelect);
                        }
                    }

                    _isScanning = false;
                    StopElapsedTimer();
                    UpdateCommands();
                });
            }
        }

        private static void MergeScanResult(ScanResult aggregate, ScanResult current)
        {
            if (aggregate == null || current == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(current.TempDataPath)) aggregate.TempDataPath = current.TempDataPath;
            if (!string.IsNullOrWhiteSpace(current.ErrorPath)) aggregate.ErrorPath = current.ErrorPath;
            if (!string.IsNullOrWhiteSpace(current.SqliteDatabasePath)) aggregate.SqliteDatabasePath = current.SqliteDatabasePath;
            aggregate.UsesSqliteBackend = aggregate.UsesSqliteBackend || current.UsesSqliteBackend;
            if (!string.IsNullOrWhiteSpace(current.RootPath) && string.IsNullOrWhiteSpace(aggregate.RootPath)) aggregate.RootPath = current.RootPath;
            if (aggregate.RootPathKind == PathKind.Unknown && current.RootPathKind != PathKind.Unknown) aggregate.RootPathKind = current.RootPathKind;
            if (current.ScannedAtUtc != default(DateTime)) aggregate.ScannedAtUtc = current.ScannedAtUtc;

            if (aggregate.Details == null) aggregate.Details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase);
            if (current.Details != null)
            {
                foreach (var detailPair in current.Details)
                {
                    if (detailPair.Value == null) continue;
                    FolderDetail existing;
                    if (!aggregate.Details.TryGetValue(detailPair.Key, out existing))
                    {
                        aggregate.Details[detailPair.Key] = detailPair.Value;
                        continue;
                    }

                    MergeFolderDetail(existing, detailPair.Value);
                }
            }

            if (aggregate.TreeMap == null) aggregate.TreeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (current.TreeMap != null)
            {
                MergeTreeMap(aggregate.TreeMap, current.TreeMap);
            }
        }

        private static void MergeFolderDetail(FolderDetail target, FolderDetail source)
        {
            FolderDetailMerger.Merge(target, source);
        }

        private static void MergeTreeMap(Dictionary<string, List<string>> target, Dictionary<string, List<string>> source)
        {
            if (target == null || source == null)
            {
                return;
            }

            foreach (var node in source)
            {
                List<string> children;
                if (!target.TryGetValue(node.Key, out children) || children == null)
                {
                    target[node.Key] = node.Value == null ? new List<string>() : new List<string>(node.Value);
                    continue;
                }

                if (node.Value == null)
                {
                    continue;
                }

                foreach (var child in node.Value)
                {
                    if (!children.Contains(child, StringComparer.OrdinalIgnoreCase))
                    {
                        children.Add(child);
                    }
                }
            }
        }

        private static ScanOptions CloneOptions(ScanOptions template, string root)
        {
            return new ScanOptions
            {
                RootPath = root,
                OutputDirectory = template.OutputDirectory,
                CredentialSource = template.CredentialSource,
                Credential = template.Credential == null ? null : template.Credential.Clone(),
                MaxDepth = template.MaxDepth,
                ScanAllDepths = template.ScanAllDepths,
                IncludeInherited = template.IncludeInherited,
                ResolveIdentities = template.ResolveIdentities,
                ExcludeServiceAccounts = template.ExcludeServiceAccounts,
                ExcludeAdminAccounts = template.ExcludeAdminAccounts,
                ExpandGroups = template.ExpandGroups,
                UsePowerShell = template.UsePowerShell,
                EnableAdvancedAudit = template.EnableAdvancedAudit,
                ComputeEffectiveAccess = template.ComputeEffectiveAccess,
                IncludeSharePermissions = template.IncludeSharePermissions,
                IncludeFiles = template.IncludeFiles,
                ReadOwnerAndSacl = template.ReadOwnerAndSacl,
                CompareBaseline = template.CompareBaseline
            };
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }
            return value;
        }

        private static string BuildScanNameFromRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return "scan";
            var normalized = root.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(normalized)) return "scan";

            string name;
            if (normalized.StartsWith("\\", StringComparison.Ordinal))
            {
                var segments = normalized.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                name = segments.Length > 0 ? segments[segments.Length - 1] : string.Empty;
            }
            else
            {
                name = Path.GetFileName(normalized);
                if (string.IsNullOrWhiteSpace(name) && normalized.Length >= 2 && normalized[1] == ':')
                {
                    name = normalized.Substring(0, 1);
                }
            }

            name = SanitizeFileName(name);
            return string.IsNullOrWhiteSpace(name) ? "scan" : name;
        }

        private void StopScan()
        {
            if (_isViewerMode) return;

            var hasRunningLocalScan = _cts != null;
            if (hasRunningLocalScan)
            {
                _cts.Cancel();
            }

            if (IsServiceRuntimeRunning)
            {
                StopServiceRuntimeAndClearQueue();
            }

            CleanupResidualFiles(true);

            if (hasRunningLocalScan)
            {
                ProgressText = "Richiesta di stop inviata. Pulizia cache/residui completata; puoi aggiungere nuove cartelle al job.";
            }
        }

        private void StopServiceRuntimeAndClearQueue()
        {
            try
            {
                TerminateServiceProcesses();

                var stopResult = ExecuteScCommand(string.Format("stop {0}", ServiceName), "stop", false);
                if (stopResult.ExitCode != 0 && stopResult.ExitCode != 1060 && stopResult.ExitCode != 1062)
                {
                    ThrowScOperationFailed("stop", stopResult);
                }

                var jobsRoot = RuntimePaths.GetJobsRoot();
                if (Directory.Exists(jobsRoot))
                {
                    foreach (var file in Directory.GetFiles(jobsRoot, "job_*.json"))
                    {
                        RuntimeCleanupService.TryDeleteFile(file);
                    }
                }

                ProgressText = "Scansione servizio fermata. Puoi aggiornare l'elenco cartelle e rilanciare il job.";
            }
            catch (Exception ex)
            {
                ProgressText = string.Format("Errore stop servizio: {0}", ex.Message);
            }
            finally
            {
                RefreshServiceRuntimeStatus();
                UpdateCommands();
            }
        }
        private void CleanupResidualFiles()
        {
            CleanupResidualFiles(false, false);
        }

        private void CleanupResidualFiles(bool triggeredByStop)
        {
            CleanupResidualFiles(triggeredByStop, false);
        }

        private void CleanupResidualFiles(bool triggeredByStop, bool triggeredByServiceUninstall)
        {
            var cleanupResult = _runtimeCleanupService.CleanupOperationalData();
            var removedEntries = cleanupResult.RemovedEntries;
            var diagnosticsCount = cleanupResult.Diagnostics == null ? 0 : cleanupResult.Diagnostics.Count;
            var diagnosticsSuffix = diagnosticsCount > 0
                ? string.Format(" Alcuni elementi non sono stati rimossi ({0}).", diagnosticsCount)
                : string.Empty;

            if (triggeredByServiceUninstall)
            {
                ProgressText = removedEntries > 0
                    ? string.Format("Disinstallazione servizio: rimossi {0} elementi residui (cache/job/temp).{1}", removedEntries, diagnosticsSuffix)
                    : "Disinstallazione servizio: nessun residuo da pulire." + diagnosticsSuffix;
                return;
            }

            if (triggeredByStop)
            {
                ProgressText = removedEntries > 0
                    ? string.Format("Analisi fermata: rimossi {0} elementi residui (cache/job/temp).{1}", removedEntries, diagnosticsSuffix)
                    : "Analisi fermata: nessun residuo da pulire." + diagnosticsSuffix;
                return;
            }

            try
            {
                OpenFolder(cleanupResult.TempRootPath);
            }
            catch
            {
            }

            ProgressText = removedEntries > 0
                ? string.Format("Pulizia completata: rimossi {0} elementi residui. Cartella temp aperta.{1}", removedEntries, diagnosticsSuffix)
                : "Pulizia completata: nessun file residuo trovato. Cartella temp aperta." + diagnosticsSuffix;
        }

        private static void OpenFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }
        private async void Export()
        {
            if (_isViewerMode) return;
            if (_scanResult == null) return;
            if (!TryEnsureScanDataAvailableForExport("Export non disponibile")) return;
            var dialog = new Win32.SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
                FileName = BuildExportFileName(RootPath, "xlsx"),
                InitialDirectory = ResolveInitialDirectory(_lastExportDirectory, RootPath)
            };
            if (dialog.ShowDialog() != true) return;
            var outputPath = dialog.FileName;
            try
            {
                SetBusy(true);
                var result = await Task.Run(() => _excelExporter.Export(_scanResult.TempDataPath, _scanResult.ErrorPath, outputPath));
                EnsureExportOutput(outputPath);
                _hasExported = true;
                UpdateLastDirectory(ref _lastExportDirectory, outputPath);
                ProgressText = LocalizationManager.Format("Progress.ExcelCompleted", outputPath);
                var warningMessage = BuildExcelWarningMessage(result);
                if (!string.IsNullOrWhiteSpace(warningMessage))
                {
                    WpfMessageBox.Show(
                        string.Format("ATTENZIONE: {0}", warningMessage),
                        LocalizationManager.Text("Dialog.ExportWarnings"),
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                ProgressText = LocalizationManager.Format("Progress.ExportError", ex.Message);
                WpfMessageBox.Show(ProgressText, LocalizationManager.Text("Dialog.ExportError"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }
        private async void ImportAnalysis()
        {
            if (IsBusy) return;
            var dialog = new Win32.OpenFileDialog
            {
                Filter = "Analisi NtfsAudit (*.ntaudit)|*.ntaudit",
                InitialDirectory = ResolveInitialDirectory(_lastImportDirectory, RootPath)
            };
            if (dialog.ShowDialog() != true) return;
            await ImportAnalysisFromPathAsync(dialog.FileName);
        }

        public async Task ImportAnalysisFromPathAsync(string archivePath)
        {
            if (IsBusy || string.IsNullOrWhiteSpace(archivePath))
            {
                return;
            }

            try
            {
                SetBusy(true);
                var imported = await Task.Run(() => _analysisArchive.Import(archivePath));
                var importedResult = imported.ScanResult;
                if (importedResult == null)
                {
                    ProgressText = LocalizationManager.Text("Progress.ImportInvalid");
                    return;
                }
                if (importedResult.Details == null)
                {
                    importedResult.Details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase);
                }
                if (importedResult.TreeMap == null)
                {
                    importedResult.TreeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                }

                ApplyDiffs(importedResult);
                if (!ValidateImportedResult(importedResult, out var validationMessage))
                {
                    var confirm = WpfMessageBox.Show(
                        LocalizationManager.Format("Dialog.ProceedAnyway", validationMessage),
                        LocalizationManager.Text("Dialog.Import"),
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Warning);
                    if (confirm != System.Windows.MessageBoxResult.Yes)
                    {
                        ProgressText = LocalizationManager.Text("Progress.ImportCancelled");
                        return;
                    }
                }
                _scanResult = importedResult;
                _hasExported = false;
                ApplyImportedOptions(imported.ScanOptions);
                if (!string.IsNullOrWhiteSpace(imported.RootPath))
                {
                    RootPath = imported.RootPath;
                }
                ClearResults();
                LoadTree(_scanResult);
                LoadErrors(_scanResult.ErrorPath);
                var root = _fullTreeMap == null || _fullTreeMap.Count == 0
                    ? RootPath
                    : ResolveTreeRoot(_fullTreeMap, _scanResult.RootPath);

                if (string.IsNullOrWhiteSpace(root) && _scanResult.TreeMap != null && _scanResult.TreeMap.Count > 0)
                {
                    root = ResolveTreeRoot(_scanResult.TreeMap, _scanResult.RootPath);
                }

                if (!string.IsNullOrWhiteSpace(root))
                {
                    RootPath = root;
                    SelectFolder(root);
                }
                UpdateLastDirectory(ref _lastImportDirectory, archivePath);
                ProgressText = LocalizationManager.Format("Progress.Imported", _scanResult.Details == null ? 0 : _scanResult.Details.Count, ErrorCount);
                UpdateCommands();
            }
            catch (Exception ex)
            {
                ProgressText = LocalizationManager.Format("Progress.ImportError", ex.Message);
                WpfMessageBox.Show(ProgressText, LocalizationManager.Text("Dialog.ImportError"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private bool TryEnsureScanDataAvailableForExport(string messageTitle)
        {
            if (_scanResult == null)
            {
                return false;
            }

            var ioTempDataPath = PathResolver.ToExtendedPath(_scanResult.TempDataPath);
            if (string.IsNullOrWhiteSpace(_scanResult.TempDataPath) || !File.Exists(ioTempDataPath))
            {
                ProgressText = LocalizationManager.Text("Progress.ExportDataMissing");
                WpfMessageBox.Show(ProgressText, messageTitle, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private async Task RunExportActionAsync(Action exportAction, string errorLabel, string outputPath = null)
        {
            try
            {
                SetBusy(true);
                await Task.Run(exportAction);
                EnsureExportOutput(outputPath);
                _hasExported = true;
                UpdateLastDirectory(ref _lastExportDirectory, outputPath);
            }
            catch (Exception ex)
            {
                ProgressText = string.Format("{0}: {1}", errorLabel, ex.Message);
                WpfMessageBox.Show(ProgressText, errorLabel, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private string BuildExcelWarningMessage(ExcelExportResult result)
        {
            if (result == null)
            {
                return null;
            }

            if (result.WasSplit)
            {
                return string.Format(
                    "Il dataset supera il limite di 1.048.576 righe per foglio. Creati {0} fogli Users e {1} fogli Groups.",
                    result.UserSheetCount,
                    result.GroupSheetCount);
            }

            return null;
        }

        private void EnsureExportOutput(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath)) return;
            var ioPath = PathResolver.ToExtendedPath(outputPath);
            if (!File.Exists(ioPath))
            {
                throw new IOException("Il file export non è stato creato.");
            }
            var info = new FileInfo(ioPath);
            if (info.Length == 0)
            {
                throw new IOException("Il file export risulta vuoto.");
            }
        }

        private ScanResult ExecuteScan(ScanOptions options, CancellationToken token)
        {
            try
            {
                var runtimeOptions = options.Clone();
                if (runtimeOptions.Credential != null)
                {
                    runtimeOptions.Credential = ScanCredentialProtector.ResolveForRuntime(runtimeOptions.Credential);
                }

                var adResolver = CreateResolver(runtimeOptions.UsePowerShell, runtimeOptions.Credential);
                var identityResolver = new IdentityResolver(_sidNameCache, adResolver);
                var groupExpansion = new GroupExpansionService(adResolver, _groupMembershipCache);
                var scanService = new ScanService(identityResolver, groupExpansion, new SharePermissionService(runtimeOptions.Credential));

                var progress = new Progress<ScanProgress>(scanProgress =>
                {
                    RunOnUi(() => UpdateProgress(scanProgress));
                });
                var result = scanService.Run(runtimeOptions, progress, token);

                RunOnUi(() =>
                {
                    ApplyDiffs(result);
                    _scanResult = result;
                    _hasExported = false;
                    SaveCache();
                    LoadTree(result);
                    LoadErrors(result.ErrorPath);
                    ErrorCount = Errors.Count;
                    SelectFolder(runtimeOptions.RootPath);
                });

                return result;
            }
            catch (OperationCanceledException)
            {
                RunOnUi(() =>
                {
                    ProgressText = LocalizationManager.Text("Progress.ScanCancelled");
                    CurrentPathText = string.Empty;
                });
                throw;
            }
            catch (Exception ex)
            {
                RunOnUi(() =>
                {
                    ProgressText = LocalizationManager.Format("Progress.ScanError", ex.Message);
                    CurrentPathText = string.Empty;
                });
                return null;
            }
        }

        private IAdResolver CreateResolver(bool usePowerShell, ScanCredential credential)
        {
            if (usePowerShell)
            {
                var path = FindPowerShell();
                var psResolver = new PowerShellAdResolver(path, credential);
                var dsResolver = new DirectoryServicesResolver(credential);
                if (psResolver.IsAvailable) return new CompositeAdResolver(psResolver, dsResolver);
                if (!string.IsNullOrWhiteSpace(psResolver.AvailabilityDiagnostic))
                {
                    Debug.WriteLine(string.Format(
                        "[MainViewModel] PowerShell AD resolver unavailable, fallback to DirectoryServices: {0}",
                        psResolver.AvailabilityDiagnostic));
                }
                return dsResolver;
            }

            return new DirectoryServicesResolver(credential);
        }

        private string FindPowerShell()
        {
            var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var candidate = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(candidate)) return candidate;
            var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(windir))
            {
                return Path.Combine(windir, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            }

            return "powershell.exe";
        }

        private static bool IsProcessElevated()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                if (identity == null) return false;
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

    }
}
