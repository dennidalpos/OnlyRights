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
using System.Diagnostics;
using NtfsAudit.Core.Models;

namespace NtfsAudit.Core.Services
{
    public class CompositeAdResolver : IAdResolver
    {
        private readonly IAdResolver _primary;
        private readonly IAdResolver _fallback;

        public CompositeAdResolver(IAdResolver primary, IAdResolver fallback)
        {
            _primary = primary;
            _fallback = fallback;
        }

        public bool IsAvailable
        {
            get { return (_primary != null && _primary.IsAvailable) || (_fallback != null && _fallback.IsAvailable); }
        }

        public ResolvedPrincipal ResolvePrincipal(string sid)
        {
            if (_primary != null && _primary.IsAvailable)
            {
                var resolved = _primary.ResolvePrincipal(sid);
                if (resolved != null) return resolved;
                WriteFallbackDiagnostic(_primary, "ResolvePrincipal", sid);
            }

            if (_fallback != null && _fallback.IsAvailable)
            {
                return _fallback.ResolvePrincipal(sid);
            }

            return null;
        }

        public List<ResolvedPrincipal> GetGroupMembers(string groupSid)
        {
            if (_primary != null && _primary.IsAvailable)
            {
                var members = _primary.GetGroupMembers(groupSid);
                if (members != null && members.Count > 0) return members;
                WriteFallbackDiagnostic(_primary, "GetGroupMembers", groupSid);
            }

            if (_fallback != null && _fallback.IsAvailable)
            {
                return _fallback.GetGroupMembers(groupSid);
            }

            return new List<ResolvedPrincipal>();
        }

        public List<ResolvedPrincipal> GetUserGroups(string userSid)
        {
            if (_primary != null && _primary.IsAvailable)
            {
                var members = _primary.GetUserGroups(userSid);
                if (members != null && members.Count > 0) return members;
                WriteFallbackDiagnostic(_primary, "GetUserGroups", userSid);
            }

            if (_fallback != null && _fallback.IsAvailable)
            {
                return _fallback.GetUserGroups(userSid);
            }

            return new List<ResolvedPrincipal>();
        }

        private static void WriteFallbackDiagnostic(IAdResolver resolver, string operation, string subject)
        {
            var diagnostics = resolver as IResolverDiagnostics;
            if (diagnostics == null || string.IsNullOrWhiteSpace(diagnostics.LastDiagnostic))
            {
                return;
            }

            Debug.WriteLine(string.Format(
                "[CompositeAdResolver] fallback after {0}({1}): {2}",
                operation,
                subject,
                diagnostics.LastDiagnostic));
        }
    }
}
