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
    public sealed class ServiceScheduledScanTemplate
    {
        public List<string> Roots { get; set; } = new List<string>();
        public string? OutputDirectory { get; set; }
        public int MaxDepth { get; set; }
        public bool ScanAllDepths { get; set; }
        public bool IncludeInherited { get; set; }
        public bool ResolveIdentities { get; set; }
        public bool ExcludeServiceAccounts { get; set; }
        public bool ExcludeAdminAccounts { get; set; }
        public bool ExpandGroups { get; set; }
        public bool UsePowerShell { get; set; }
        public bool EnableAdvancedAudit { get; set; }
        public bool ComputeEffectiveAccess { get; set; }
        public bool IncludeSharePermissions { get; set; }
        public bool IncludeFiles { get; set; }
        public bool ReadOwnerAndSacl { get; set; }
        public bool CompareBaseline { get; set; }
        public bool AnonymizeIdentities { get; set; }

        public ServiceScheduledScanTemplate Clone()
        {
            return new ServiceScheduledScanTemplate
            {
                Roots = new List<string>(Roots ?? new List<string>()),
                OutputDirectory = OutputDirectory,
                MaxDepth = MaxDepth,
                ScanAllDepths = ScanAllDepths,
                IncludeInherited = IncludeInherited,
                ResolveIdentities = ResolveIdentities,
                ExcludeServiceAccounts = ExcludeServiceAccounts,
                ExcludeAdminAccounts = ExcludeAdminAccounts,
                ExpandGroups = ExpandGroups,
                UsePowerShell = UsePowerShell,
                EnableAdvancedAudit = EnableAdvancedAudit,
                ComputeEffectiveAccess = ComputeEffectiveAccess,
                IncludeSharePermissions = IncludeSharePermissions,
                IncludeFiles = IncludeFiles,
                ReadOwnerAndSacl = ReadOwnerAndSacl,
                CompareBaseline = CompareBaseline,
                AnonymizeIdentities = AnonymizeIdentities
            };
        }
    }
}
