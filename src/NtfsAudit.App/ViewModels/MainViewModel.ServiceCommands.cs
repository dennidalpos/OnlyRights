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
        private void InstallService()
        {
            try
            {
                var serviceState = QueryServiceState();
                if (serviceState.IsInstalled && serviceState.IsRunning)
                {
                    ProgressText = "Servizio Windows già installato e attivo.";
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
                    return "Il servizio esiste già.";
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

            var current = new DirectoryInfo(appBase);
            for (var i = 0; i < 8 && current != null; i++)
            {
                var repoRoot = current.FullName;
                candidates.Add(Path.Combine(repoRoot, "artifacts", "packages", "Release", "net8.0-windows", "Service", "NtfsAudit.Service.exe"));
                candidates.Add(Path.Combine(repoRoot, "artifacts", "packages", "Release", "net8.0-windows", "Service", "NtfsAudit.Service.dll"));
                candidates.Add(Path.Combine(repoRoot, "artifacts", "packages", "Debug", "net8.0-windows", "Service", "NtfsAudit.Service.exe"));
                candidates.Add(Path.Combine(repoRoot, "artifacts", "packages", "Debug", "net8.0-windows", "Service", "NtfsAudit.Service.dll"));
                candidates.Add(Path.Combine(repoRoot, "artifacts", "publish", "Release", "net8.0-windows", "Service", "NtfsAudit.Service.exe"));
                candidates.Add(Path.Combine(repoRoot, "artifacts", "publish", "Release", "net8.0-windows", "Service", "NtfsAudit.Service.dll"));
                candidates.Add(Path.Combine(repoRoot, "artifacts", "publish", "Debug", "net8.0-windows", "Service", "NtfsAudit.Service.exe"));
                candidates.Add(Path.Combine(repoRoot, "artifacts", "publish", "Debug", "net8.0-windows", "Service", "NtfsAudit.Service.dll"));
                candidates.Add(Path.Combine(repoRoot, "artifacts", "build", "NtfsAudit.Service", "Release", "net8.0-windows", "NtfsAudit.Service.exe"));
                candidates.Add(Path.Combine(repoRoot, "artifacts", "build", "NtfsAudit.Service", "Release", "net8.0-windows", "NtfsAudit.Service.dll"));
                candidates.Add(Path.Combine(repoRoot, "artifacts", "build", "NtfsAudit.Service", "Debug", "net8.0-windows", "NtfsAudit.Service.exe"));
                candidates.Add(Path.Combine(repoRoot, "artifacts", "build", "NtfsAudit.Service", "Debug", "net8.0-windows", "NtfsAudit.Service.dll"));
                current = current.Parent;
            }

            var parent = Directory.GetParent(appBase);
            for (var i = 0; i < 4 && parent != null; i++)
            {
                candidates.Add(Path.Combine(parent.FullName, "NtfsAudit.Service", "bin", "Release", "net8.0-windows", "NtfsAudit.Service.exe"));
                candidates.Add(Path.Combine(parent.FullName, "NtfsAudit.Service", "bin", "Debug", "net8.0-windows", "NtfsAudit.Service.exe"));
                candidates.Add(Path.Combine(parent.FullName, "NtfsAudit.Service", "bin", "Release", "net8.0-windows", "NtfsAudit.Service.dll"));
                candidates.Add(Path.Combine(parent.FullName, "NtfsAudit.Service", "bin", "Debug", "net8.0-windows", "NtfsAudit.Service.dll"));
                parent = parent.Parent;
            }

            var serviceBinary = candidates
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(File.Exists);
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

    }
}
