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
            var installScript = LoadScript("scripts", "maintenance", "test-installer-install.ps1");
            var uninstallScript = LoadScript("scripts", "maintenance", "test-installer-uninstall.ps1");
            var upgradeScript = LoadScript("scripts", "maintenance", "test-installer-upgrade.ps1");

            Assert.Contains("Resolve-MsiInstallRoot", installScript, StringComparison.Ordinal);
            Assert.Contains("Resolve-MsiInstallRoot", uninstallScript, StringComparison.Ordinal);
            Assert.Contains("Resolve-MsiInstallRoot", upgradeScript, StringComparison.Ordinal);
            Assert.Contains("Format-MsiPropertyArgument", installScript, StringComparison.Ordinal);
            Assert.Contains("Format-MsiPropertyArgument", upgradeScript, StringComparison.Ordinal);
            Assert.DoesNotContain("LOCALAPPDATA", installScript, StringComparison.Ordinal);
            Assert.DoesNotContain("LOCALAPPDATA", uninstallScript, StringComparison.Ordinal);
            Assert.DoesNotContain("LOCALAPPDATA", upgradeScript, StringComparison.Ordinal);
        }

        [Fact]
        public void MsiInstallAndUpgradeScripts_FormatInstallFolderProperty()
        {
            var installScript = LoadScript("scripts", "maintenance", "test-installer-install.ps1");
            var upgradeScript = LoadScript("scripts", "maintenance", "test-installer-upgrade.ps1");

            Assert.Contains("$installFolderArgument = Format-MsiPropertyArgument -Name \"INSTALLFOLDER\" -Value $resolvedInstallRoot", installScript, StringComparison.Ordinal);
            Assert.Contains("$installFolderArgument = Format-MsiPropertyArgument -Name \"INSTALLFOLDER\" -Value $resolvedInstallRoot", upgradeScript, StringComparison.Ordinal);
            Assert.DoesNotContain("INSTALLFOLDER=$resolvedInstallRoot", installScript, StringComparison.Ordinal);
            Assert.DoesNotContain("INSTALLFOLDER=$resolvedInstallRoot", upgradeScript, StringComparison.Ordinal);
        }

        [Fact]
        public void MsiUninstallScript_VerifiesServiceAndInstallRootWithoutCleanupHelper()
        {
            var uninstallScript = LoadScript("scripts", "maintenance", "test-installer-uninstall.ps1");

            Assert.Contains("Get-ServiceScriptContext", uninstallScript, StringComparison.Ordinal);
            Assert.Contains("Get-ServiceState", uninstallScript, StringComparison.Ordinal);
            Assert.Contains("MSI uninstall left service", uninstallScript, StringComparison.Ordinal);
            Assert.Contains("MSI uninstall left install root on disk", uninstallScript, StringComparison.Ordinal);
            Assert.DoesNotContain("services-cleanup.ps1", uninstallScript, StringComparison.Ordinal);
            Assert.DoesNotContain("Remove-Item -Path $resolvedInstallRoot", uninstallScript, StringComparison.Ordinal);
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
