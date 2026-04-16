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
#nullable enable

namespace NtfsAudit.App.Models
{
    public class ResolvedPrincipal
    {
        public string? Sid { get; set; }
        public string? Name { get; set; }
        public bool IsGroup { get; set; }
        public bool IsDisabled { get; set; }
        public bool IsServiceAccount { get; set; }
        public bool IsAdminAccount { get; set; }
        public string Type
        {
            get { return IsGroup ? "Group" : "User"; }
        }
    }
}
