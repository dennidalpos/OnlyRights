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
using NtfsAudit.App.Services;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class WindowsServiceInstallCommandResolverTests
    {
        [Fact]
        public void ResolveServiceCommand_FindsServiceUnderSrcBinLayout()
        {
            var root = Path.Combine(Path.GetTempPath(), "NtfsAudit.Tests", Guid.NewGuid().ToString("N"));
            var appBase = Path.Combine(root, "src", "NtfsAudit.App", "bin", "Debug", "net8.0-windows");
            var expected = Path.Combine(root, "src", "NtfsAudit.Service", "bin", "Release", "net8.0-windows", "NtfsAudit.Service.exe");

            Directory.CreateDirectory(appBase);
            Directory.CreateDirectory(Path.GetDirectoryName(expected));
            File.WriteAllText(Path.Combine(root, "NtfsAudit.sln"), string.Empty);
            File.WriteAllText(expected, string.Empty);

            try
            {
                var resolved = WindowsServiceInstallCommandResolver.ResolveServiceCommand(appBase);

                Assert.Equal(expected, resolved);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        [Fact]
        public void FormatBinPathForSc_QuotesExeCommand()
        {
            var formatted = WindowsServiceInstallCommandResolver.FormatBinPathForSc(@"C:\Program Files\OnlyRights\NtfsAudit.Service.exe");

            Assert.Equal(@"""C:\Program Files\OnlyRights\NtfsAudit.Service.exe""", formatted);
        }

        [Fact]
        public void FormatBinPathForSc_QuotesDotnetAndDllCommand()
        {
            var formatted = WindowsServiceInstallCommandResolver.FormatBinPathForSc(
                @"C:\Program Files\OnlyRights\NtfsAudit.Service.dll",
                () => @"C:\Program Files\dotnet\dotnet.exe");

            Assert.StartsWith("\"\\\"", formatted, StringComparison.Ordinal);
            Assert.EndsWith("\\\"\"", formatted, StringComparison.Ordinal);
            Assert.Contains(@"C:\Program Files\dotnet\dotnet.exe", formatted, StringComparison.Ordinal);
            Assert.Contains(@"C:\Program Files\OnlyRights\NtfsAudit.Service.dll", formatted, StringComparison.Ordinal);
        }
    }
}
