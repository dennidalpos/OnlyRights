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
using System.IO;
using System.Linq;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class MsiPackagingScriptTests
    {
        [Fact]
        public void MsiHelper_GeneratesPerMachineServiceRegistration()
        {
            var script = LoadScript("scripts", "internal", "installer.ps1");

            Assert.Contains("InstallScope=\"perMachine\"", script, StringComparison.Ordinal);
            Assert.Contains("InstallPrivileges=\"elevated\"", script, StringComparison.Ordinal);
            Assert.Contains("<ServiceInstall", script, StringComparison.Ordinal);
            Assert.Contains("<ServiceControl", script, StringComparison.Ordinal);
            Assert.Contains("ProgramFiles64Folder", script, StringComparison.Ordinal);
            Assert.Contains("function Resolve-MsiInstallRoot", script, StringComparison.Ordinal);
            Assert.Contains("function Format-MsiPropertyArgument", script, StringComparison.Ordinal);
        }

        [Fact]
        public void MsiSmokeScripts_UsePerMachineInstallRootResolver()
        {
            var testScript = LoadScript("scripts", "maintenance", "test-installer.ps1");

            Assert.Contains("Resolve-MsiInstallRoot", testScript, StringComparison.Ordinal);
            Assert.Contains("Format-MsiPropertyArgument", testScript, StringComparison.Ordinal);
            Assert.DoesNotContain("LOCALAPPDATA", testScript, StringComparison.Ordinal);
        }

        [Fact]
        public void MsiInstallAndUpgradeScripts_FormatInstallFolderProperty()
        {
            var testScript = LoadScript("scripts", "maintenance", "test-installer.ps1");

            Assert.Contains("$installFolderArgument = Format-MsiPropertyArgument -Name \"INSTALLFOLDER\" -Value $resolvedInstallRoot", testScript, StringComparison.Ordinal);
            Assert.DoesNotContain("INSTALLFOLDER=$resolvedInstallRoot", testScript, StringComparison.Ordinal);
        }

        [Fact]
        public void MsiUninstallScript_VerifiesServiceAndInstallRootWithoutCleanupHelper()
        {
            var testScript = LoadScript("scripts", "maintenance", "test-installer.ps1");

            Assert.Contains("Get-ServiceScriptContext", testScript, StringComparison.Ordinal);
            Assert.Contains("Get-ServiceState", testScript, StringComparison.Ordinal);
            Assert.Contains("MSI uninstall left service", testScript, StringComparison.Ordinal);
            Assert.Contains("MSI uninstall left install root on disk", testScript, StringComparison.Ordinal);
            Assert.DoesNotContain("services-cleanup.ps1", testScript, StringComparison.Ordinal);
            Assert.DoesNotContain("Remove-Item -Path $resolvedInstallRoot", testScript, StringComparison.Ordinal);
        }

        private static string LoadScript(params string[] pathSegments)
        {
            var root = FindRepositoryRoot();
            return File.ReadAllText(Path.Combine(new[] { root }.Concat(pathSegments).ToArray()));
        }

        private static string FindRepositoryRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "NtfsAudit.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Repository root not found.");
        }
    }
}
