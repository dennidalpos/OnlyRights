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
using System.Linq;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public static class FolderDetailMerger
    {
        public static void Merge(FolderDetail target, FolderDetail source)
        {
            if (target == null || source == null)
            {
                return;
            }

            target.AllEntries.AddRange(source.AllEntries);
            target.GroupEntries.AddRange(source.GroupEntries);
            target.UserEntries.AddRange(source.UserEntries);
            target.ShareEntries.AddRange(source.ShareEntries);
            target.EffectiveEntries.AddRange(source.EffectiveEntries);

            target.HasExplicitPermissions = target.HasExplicitPermissions || source.HasExplicitPermissions;
            target.HasExplicitNtfs = target.HasExplicitNtfs || source.HasExplicitNtfs;
            target.HasExplicitShare = target.HasExplicitShare || source.HasExplicitShare;
            target.IsInheritanceDisabled = target.IsInheritanceDisabled || source.IsInheritanceDisabled;
            target.EntriesLoaded = target.EntriesLoaded || source.EntriesLoaded;

            RecomputeDerivedFlags(target);

            if (source.DiffSummary != null) target.DiffSummary = source.DiffSummary;
            if (source.BaselineSummary != null) target.BaselineSummary = source.BaselineSummary;
        }

        private static void RecomputeDerivedFlags(FolderDetail detail)
        {
            var allEntries = detail.AllEntries;

            detail.HasFileEntries = detail.HasFileEntries || allEntries.Any(entry =>
                string.Equals(entry.ResourceType, "File", StringComparison.OrdinalIgnoreCase));
            detail.HasFolderEntries = detail.HasFolderEntries || allEntries.Any(entry =>
                !string.Equals(entry.ResourceType, "File", StringComparison.OrdinalIgnoreCase));
            detail.HasHighRiskEntries = detail.HasHighRiskEntries || allEntries.Any(entry =>
                string.Equals(entry.RiskLevel, "High", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.RiskLevel, "Alto", StringComparison.OrdinalIgnoreCase));
            detail.HasMediumRiskEntries = detail.HasMediumRiskEntries || allEntries.Any(entry =>
                string.Equals(entry.RiskLevel, "Medium", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.RiskLevel, "Medio", StringComparison.OrdinalIgnoreCase));
            detail.HasLowRiskEntries = detail.HasLowRiskEntries || allEntries.Any(entry =>
                string.Equals(entry.RiskLevel, "Low", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.RiskLevel, "Basso", StringComparison.OrdinalIgnoreCase));
            detail.HasShareEntries = detail.HasShareEntries
                || detail.ShareEntries.Count > 0
                || allEntries.Any(entry => entry.PermissionLayer == PermissionLayer.Share);
            detail.HasEffectiveEntries = detail.HasEffectiveEntries
                || detail.EffectiveEntries.Count > 0
                || allEntries.Any(entry => entry.PermissionLayer == PermissionLayer.Effective);
        }
    }
}
