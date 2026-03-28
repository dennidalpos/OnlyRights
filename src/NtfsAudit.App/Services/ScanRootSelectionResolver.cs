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
using System.Linq;

namespace NtfsAudit.App.Services
{
    internal static class ScanRootSelectionResolver
    {
        internal static ScanRootSelection Resolve(string rootPath, IList<string> dfsTargets, string selectedDfsTarget)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return ScanRootSelection.Empty;
            }

            if (dfsTargets == null || dfsTargets.Count == 0)
            {
                return new ScanRootSelection(rootPath, null, false);
            }

            var resolvedTarget = string.IsNullOrWhiteSpace(selectedDfsTarget)
                ? (dfsTargets.Count == 1 ? dfsTargets[0] : null)
                : dfsTargets.FirstOrDefault(target => string.Equals(target, selectedDfsTarget, StringComparison.OrdinalIgnoreCase));

            return new ScanRootSelection(resolvedTarget, rootPath, string.IsNullOrWhiteSpace(resolvedTarget));
        }

        internal sealed class ScanRootSelection
        {
            internal static readonly ScanRootSelection Empty = new ScanRootSelection(null, null, true);

            internal ScanRootSelection(string rootPath, string namespacePath, bool requiresExplicitSelection)
            {
                RootPath = rootPath;
                NamespacePath = namespacePath;
                RequiresExplicitSelection = requiresExplicitSelection;
            }

            internal string RootPath { get; private set; }

            internal string NamespacePath { get; private set; }

            internal bool RequiresExplicitSelection { get; private set; }
        }
    }
}
