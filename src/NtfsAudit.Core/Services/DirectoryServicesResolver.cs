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
using System.DirectoryServices.AccountManagement;
using NtfsAudit.Core.Models;

namespace NtfsAudit.Core.Services
{
    public class DirectoryServicesResolver : IAdResolver
    {
        private readonly ScanCredential _credential;
        private readonly Func<ContextType, string, ResolvedPrincipal> _resolvePrincipalOverride;
        private readonly Func<ContextType, string, List<ResolvedPrincipal>> _resolveGroupMembersOverride;
        private readonly Func<ContextType, string, List<ResolvedPrincipal>> _resolveUserGroupsOverride;

        public DirectoryServicesResolver()
            : this(null)
        {
        }

        public DirectoryServicesResolver(ScanCredential credential)
            : this(credential, null, null, null)
        {
        }

        internal DirectoryServicesResolver(
            ScanCredential credential,
            Func<ContextType, string, ResolvedPrincipal> resolvePrincipalOverride,
            Func<ContextType, string, List<ResolvedPrincipal>> resolveGroupMembersOverride,
            Func<ContextType, string, List<ResolvedPrincipal>> resolveUserGroupsOverride)
        {
            _credential = credential;
            _resolvePrincipalOverride = resolvePrincipalOverride;
            _resolveGroupMembersOverride = resolveGroupMembersOverride;
            _resolveUserGroupsOverride = resolveUserGroupsOverride;
        }

        public bool IsAvailable { get { return true; } }

        public ResolvedPrincipal ResolvePrincipal(string sid)
        {
            var resolved = ResolvePrincipalInContext(ContextType.Domain, sid);
            return resolved ?? ResolvePrincipalInContext(ContextType.Machine, sid);
        }

        public List<ResolvedPrincipal> GetGroupMembers(string groupSid)
        {
            var resolved = ResolveGroupMembersInContext(ContextType.Domain, groupSid);
            if (resolved.Count > 0) return resolved;
            return ResolveGroupMembersInContext(ContextType.Machine, groupSid);
        }

        public List<ResolvedPrincipal> GetUserGroups(string userSid)
        {
            var resolved = ResolveUserGroupsInContext(ContextType.Domain, userSid);
            if (resolved.Count > 0) return resolved;
            return ResolveUserGroupsInContext(ContextType.Machine, userSid);
        }

        private ResolvedPrincipal ResolvePrincipalInContext(ContextType type, string sid)
        {
            return _resolvePrincipalOverride != null
                ? _resolvePrincipalOverride(type, sid)
                : ResolveInContext(type, sid);
        }

        private List<ResolvedPrincipal> ResolveGroupMembersInContext(ContextType type, string groupSid)
        {
            return _resolveGroupMembersOverride != null
                ? _resolveGroupMembersOverride(type, groupSid) ?? new List<ResolvedPrincipal>()
                : ResolveGroupMembers(type, groupSid);
        }

        private List<ResolvedPrincipal> ResolveUserGroupsInContext(ContextType type, string userSid)
        {
            return _resolveUserGroupsOverride != null
                ? _resolveUserGroupsOverride(type, userSid) ?? new List<ResolvedPrincipal>()
                : ResolveUserGroups(type, userSid);
        }

        private ResolvedPrincipal ResolveInContext(ContextType type, string sid)
        {
            try
            {
                using (var ctx = CreateContext(type))
                {
                    using (var principal = Principal.FindByIdentity(ctx, IdentityType.Sid, sid))
                    {
                        return principal == null ? null : MapPrincipal(principal);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private List<ResolvedPrincipal> ResolveGroupMembers(ContextType type, string groupSid)
        {
            var result = new List<ResolvedPrincipal>();
            try
            {
                using (var ctx = CreateContext(type))
                {
                    using (var group = GroupPrincipal.FindByIdentity(ctx, IdentityType.Sid, groupSid))
                    {
                        if (group == null) return result;
                        foreach (var member in group.GetMembers())
                        {
                            using (member)
                            {
                                result.Add(MapPrincipal(member));
                            }
                        }
                    }
                }
            }
            catch
            {
                return new List<ResolvedPrincipal>();
            }

            return result;
        }

        private List<ResolvedPrincipal> ResolveUserGroups(ContextType type, string userSid)
        {
            var result = new List<ResolvedPrincipal>();
            try
            {
                using (var ctx = CreateContext(type))
                {
                    using (var principal = Principal.FindByIdentity(ctx, IdentityType.Sid, userSid))
                    {
                        if (principal == null) return result;
                        foreach (var group in principal.GetGroups())
                        {
                            using (group)
                            {
                                result.Add(MapPrincipal(group));
                            }
                        }
                    }
                }
            }
            catch
            {
                return new List<ResolvedPrincipal>();
            }

            return result;
        }

        private PrincipalContext CreateContext(ContextType type)
        {
            if (_credential == null || !_credential.IsConfigured)
            {
                return new PrincipalContext(type);
            }

            return new PrincipalContext(type, null, _credential.UserName, _credential.Password);
        }

        private ResolvedPrincipal MapPrincipal(Principal principal)
        {
            var authenticable = principal as AuthenticablePrincipal;
            var isDisabled = authenticable != null && authenticable.Enabled.HasValue && !authenticable.Enabled.Value;
            return new ResolvedPrincipal
            {
                Sid = principal.Sid == null ? null : principal.Sid.ToString(),
                Name = principal.SamAccountName ?? principal.Name,
                IsGroup = principal is GroupPrincipal,
                IsDisabled = isDisabled
            };
        }
    }
}
