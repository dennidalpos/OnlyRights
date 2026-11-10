using NtfsAudit.App.Services;
using NtfsAudit.Core.Logging;
using NtfsAudit.Core.Export;
using NtfsAudit.Core.Cache;
using NtfsAudit.Core.Models;
using NtfsAudit.Core.Services;
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
        public void NsisHelper_GeneratesPerMachine64BitServiceRegistration()
        {
            var script = LoadScript("scripts", "internal", "installer.ps1");

            Assert.Contains("RequestExecutionLevel admin", script, StringComparison.Ordinal);
            Assert.Contains("InstallDir \"$PROGRAMFILES64\\", script, StringComparison.Ordinal);
            Assert.Contains("sc.exe create NtfsAuditWorker", script, StringComparison.Ordinal);
            Assert.Contains("function Resolve-NsisInstallRoot", script, StringComparison.Ordinal);
            Assert.Contains("function Resolve-NsisArchitecture", script, StringComparison.Ordinal);
        }

        [Fact]
        public void NsisSmokeScripts_UsePerMachineInstallRootResolver()
        {
            var testScript = LoadScript("scripts", "maintenance", "test-installer.ps1");

            Assert.Contains("Resolve-NsisInstallRoot", testScript, StringComparison.Ordinal);
            Assert.DoesNotContain("LOCALAPPDATA", testScript, StringComparison.Ordinal);
        }

        [Fact]
        public void NsisUninstallScript_VerifiesServiceAndInstallRoot()
        {
            var testScript = LoadScript("scripts", "maintenance", "test-installer.ps1");

            Assert.Contains("Get-ServiceScriptContext", testScript, StringComparison.Ordinal);
            Assert.Contains("Get-ServiceState", testScript, StringComparison.Ordinal);
            Assert.Contains("NSIS uninstall left service", testScript, StringComparison.Ordinal);
            Assert.Contains("NSIS uninstall left install root on disk", testScript, StringComparison.Ordinal);
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
