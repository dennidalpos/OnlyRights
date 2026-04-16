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
namespace NtfsAudit.App.Models
{
    public enum PathKind
    {
        Unknown = 0,
        Local = 1,
        Unc = 2,
        Dfs = 3
    }
}
