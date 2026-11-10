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
using System.Collections.Generic;

#nullable enable

namespace NtfsAudit.Core.Models
{
    public class FolderDetail
    {
        public FolderDetail()
        {
            AllEntries = new List<AceEntry>();
            GroupEntries = new List<AceEntry>();
            UserEntries = new List<AceEntry>();
            ShareEntries = new List<AceEntry>();
            EffectiveEntries = new List<AceEntry>();
        }

        public List<AceEntry> AllEntries { get; private set; }
        public List<AceEntry> GroupEntries { get; private set; }
        public List<AceEntry> UserEntries { get; private set; }
        public List<AceEntry> ShareEntries { get; private set; }
        public List<AceEntry> EffectiveEntries { get; private set; }
        public bool HasExplicitPermissions { get; set; }
        public bool HasExplicitNtfs { get; set; }
        public bool HasExplicitShare { get; set; }
        public bool IsInheritanceDisabled { get; set; }
        public bool HasFileEntries { get; set; }
        public bool HasFolderEntries { get; set; }
        public bool HasHighRiskEntries { get; set; }
        public bool HasMediumRiskEntries { get; set; }
        public bool HasLowRiskEntries { get; set; }
        public bool HasShareEntries { get; set; }
        public bool HasEffectiveEntries { get; set; }
        public bool EntriesLoaded { get; set; } = true;
        public AclDiffSummary? DiffSummary { get; set; }
        public AclDiffSummary? BaselineSummary { get; set; }
    }
}
