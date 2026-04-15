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
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    internal static class ScanCredentialPathPolicy
    {
        internal static bool ShouldUseConfiguredCredential(string rootPath)
        {
            var kind = PathResolver.DetectPathKind(rootPath);
            return kind == PathKind.Unc || kind == PathKind.Dfs;
        }

        internal static ScanCredential ResolveRuntimeCredential(string rootPath, ScanCredential configuredCredential)
        {
            if (configuredCredential == null || !configuredCredential.IsConfigured)
            {
                return null;
            }

            return ShouldUseConfiguredCredential(rootPath)
                ? configuredCredential.Clone()
                : null;
        }

        internal static string ResolveEffectiveSource(string rootPath, string configuredSource)
        {
            return ShouldUseConfiguredCredential(rootPath) && !string.IsNullOrWhiteSpace(configuredSource)
                ? configuredSource
                : "CurrentUser";
        }

        internal static string DescribeEffectiveSource(string rootPath, string configuredSource)
        {
            var effectiveSource = ResolveEffectiveSource(rootPath, configuredSource);
            if (effectiveSource == "RootOverride")
            {
                return LocalizationManager.Text("Credential.SourceRootOverride");
            }

            if (effectiveSource == "Global")
            {
                return LocalizationManager.Text("Credential.SourceGlobal");
            }

            return LocalizationManager.Text("Credential.SourceCurrentUser");
        }
    }
}
