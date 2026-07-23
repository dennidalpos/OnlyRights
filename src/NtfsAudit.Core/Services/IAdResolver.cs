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
using NtfsAudit.Core.Models;

namespace NtfsAudit.Core.Services
{
    public interface IAdResolver
    {
        bool IsAvailable { get; }
        ResolvedPrincipal ResolvePrincipal(string sid);
        List<ResolvedPrincipal> GetGroupMembers(string groupSid);
        List<ResolvedPrincipal> GetUserGroups(string userSid);
    }
}
