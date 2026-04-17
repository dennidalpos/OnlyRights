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
using System.IO;
using System.Linq;

namespace NtfsAudit.App.Services
{
    internal static class WindowsServiceInstallCommandResolver
    {
        internal static string ResolveServiceCommand(string appBaseDirectory, Func<string, bool> fileExists = null)
        {
            fileExists = fileExists ?? File.Exists;
            return EnumerateCandidatePaths(appBaseDirectory)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(fileExists);
        }

        internal static IEnumerable<string> EnumerateCandidatePaths(string appBaseDirectory)
        {
            if (string.IsNullOrWhiteSpace(appBaseDirectory))
            {
                yield break;
            }

            var baseDirectory = Path.GetFullPath(appBaseDirectory);
            foreach (var candidate in EnumerateAppAdjacentCandidates(baseDirectory))
            {
                yield return candidate;
            }

            var repoRoot = TryFindRepositoryRoot(baseDirectory);
            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                yield break;
            }

            foreach (var configuration in new[] { "Release", "Debug" })
            {
                foreach (var basePath in new[]
                {
                    Path.Combine(repoRoot, "artifacts", "packages", configuration, "net8.0-windows"),
                    Path.Combine(repoRoot, "artifacts", "publish", configuration, "net8.0-windows"),
                    Path.Combine(repoRoot, "artifacts", "build", "NtfsAudit.Service", configuration, "net8.0-windows"),
                    Path.Combine(repoRoot, "src", "NtfsAudit.Service", "bin", configuration, "net8.0-windows")
                })
                {
                    foreach (var relativePath in new[]
                    {
                        Path.Combine("Service", "NtfsAudit.Service.exe"),
                        Path.Combine("Service", "NtfsAudit.Service.dll"),
                        "NtfsAudit.Service.exe",
                        "NtfsAudit.Service.dll"
                    })
                    {
                        yield return Path.Combine(basePath, relativePath);
                    }
                }
            }
        }

        internal static string FormatBinPathForSc(string serviceCommand, Func<string> dotnetHostResolver = null)
        {
            if (string.IsNullOrWhiteSpace(serviceCommand))
            {
                throw new InvalidOperationException("Service path is invalid.");
            }

            var sanitizedCommand = serviceCommand.Replace("\"", string.Empty);
            if (sanitizedCommand.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return string.Format("\"{0}\"", sanitizedCommand);
            }

            var dotnetHost = (dotnetHostResolver == null ? ResolveDotnetHostPath() : dotnetHostResolver()) ?? "dotnet.exe";
            dotnetHost = dotnetHost.Replace("\"", string.Empty);
            return string.Format("\"\\\"{0}\\\" \\\"{1}\\\"\"", dotnetHost, sanitizedCommand);
        }

        internal static string ResolveDotnetHostPath(Func<string, bool> fileExists = null)
        {
            fileExists = fileExists ?? File.Exists;
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            if (!string.IsNullOrWhiteSpace(programFiles))
            {
                var preferred = Path.Combine(programFiles, "dotnet", "dotnet.exe");
                if (fileExists(preferred))
                {
                    return preferred;
                }
            }

            return "dotnet.exe";
        }

        private static IEnumerable<string> EnumerateAppAdjacentCandidates(string appBaseDirectory)
        {
            foreach (var relativePath in new[]
            {
                "NtfsAudit.Service.exe",
                Path.Combine("NtfsAudit.Service", "NtfsAudit.Service.exe"),
                Path.Combine("Service", "NtfsAudit.Service.exe"),
                "NtfsAudit.Service.dll",
                Path.Combine("NtfsAudit.Service", "NtfsAudit.Service.dll"),
                Path.Combine("Service", "NtfsAudit.Service.dll")
            })
            {
                yield return Path.Combine(appBaseDirectory, relativePath);
            }
        }

        private static string TryFindRepositoryRoot(string appBaseDirectory)
        {
            var current = new DirectoryInfo(appBaseDirectory);
            for (var i = 0; i < 10 && current != null; i++)
            {
                if (File.Exists(Path.Combine(current.FullName, "NtfsAudit.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            return null;
        }
    }
}
