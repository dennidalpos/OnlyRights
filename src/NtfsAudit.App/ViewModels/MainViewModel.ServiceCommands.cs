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
using System.Windows.Forms;
using WinForms = System.Windows.Forms;
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
        private void InstallService()
        {
            try
            {
                var serviceState = QueryServiceState();
                if (serviceState.IsInstalled && serviceState.IsRunning)
                {
                    ProgressText = LocalizationManager.Text("Service.StartedNoDetails");
                    RefreshServiceRuntimeStatus();
                    return;
                }

                var serviceCommand = ResolveServiceInstallCommand();
                if (string.IsNullOrWhiteSpace(serviceCommand))
                {
                    WpfMessageBox.Show(LocalizationManager.Text("Service.CommandMissing"), LocalizationManager.Text("Dialog.ServiceInstall"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
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

                ExecuteScCommand(string.Format("description {0} \"{1}\"", ServiceName, LocalizationManager.Text("Service.Description")), "description");
                var startResult = ExecuteScCommand(string.Format("start {0}", ServiceName), "start", false);
                if (startResult.ExitCode != 0 && startResult.ExitCode != 1056)
                {
                    ThrowScOperationFailed("start", startResult);
                }
                ProgressText = LocalizationManager.Text("Service.InstalledBadge");
                RefreshServiceRuntimeStatus();
                WpfMessageBox.Show(LocalizationManager.Text("Service.InstalledBadge"), LocalizationManager.Text("Dialog.ServiceInstall"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(LocalizationManager.Format("Service.InstallError", ex.Message), LocalizationManager.Text("Dialog.ServiceInstall"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
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

                ProgressText = LocalizationManager.Text("Service.NotInstalledStatus");
                RefreshServiceRuntimeStatus();
                WpfMessageBox.Show(LocalizationManager.Text("Service.NotInstalledStatus"), LocalizationManager.Text("Dialog.ServiceUninstall"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(LocalizationManager.Format("Service.UninstallError", ex.Message), LocalizationManager.Text("Dialog.ServiceUninstall"), System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
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
            throw new InvalidOperationException(LocalizationManager.Format("Service.OperationFailed", op, result.ExitCode, errorText));
        }

        private static string BuildScFallbackError(int exitCode)
        {
            switch (exitCode)
            {
                case 5:
                    return LocalizationManager.Text("Service.Error.AccessDenied");
                case 1060:
                    return LocalizationManager.Text("Service.Error.NotInstalled");
                case 1073:
                    return LocalizationManager.Text("Service.Error.AlreadyExists");
                case -1:
                    return LocalizationManager.Text("Service.Error.ScOrUac");
                default:
                    return LocalizationManager.Text("Service.Error.UnknownSc");
            }
        }

        private static ScCommandResult RunScCommand(string arguments, bool runAsAdmin)
        {
            var scPath = ResolveScExecutablePath();
            if (runAsAdmin)
            {
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
                        return new ScCommandResult { ExitCode = -1, Error = LocalizationManager.Text("Service.ScExecutableStartError") };
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
                        return new ScCommandResult { ExitCode = -1, Error = LocalizationManager.Text("Service.ElevatedScStartError") };
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
            return WindowsServiceInstallCommandResolver.ResolveServiceCommand(AppDomain.CurrentDomain.BaseDirectory);
        }

        private string BuildServiceBinPathForSc(string serviceCommand)
        {
            return WindowsServiceInstallCommandResolver.FormatBinPathForSc(serviceCommand);
        }

        private void StartServiceRuntime()
        {
            try
            {
                var startResult = ExecuteScCommand(string.Format("start {0}", ServiceName), "start", false);
                if (startResult.ExitCode != 0 && startResult.ExitCode != 1056)
                {
                    ThrowScOperationFailed("start", startResult);
                }
                RefreshServiceRuntimeStatus();
                ProgressText = LocalizationManager.Text("Service.StartedNoDetails");
            }
            catch (Exception ex)
            {
                ProgressText = LocalizationManager.Format("Progress.ServiceStartError", ex.Message);
            }
        }

        private void StopServiceRuntime()
        {
            try
            {
                var stopResult = ExecuteScCommand(string.Format("stop {0}", ServiceName), "stop", false);
                if (stopResult.ExitCode != 0 && stopResult.ExitCode != 1060 && stopResult.ExitCode != 1062)
                {
                    ThrowScOperationFailed("stop", stopResult);
                }

                RefreshServiceRuntimeStatus();
                ProgressText = LocalizationManager.Text("Service.InstalledStopped");
            }
            catch (Exception ex)
            {
                ProgressText = LocalizationManager.Format("Progress.ServiceStopRuntimeError", ex.Message);
            }
        }

    }
}
