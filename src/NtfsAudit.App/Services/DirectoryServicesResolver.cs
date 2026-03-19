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
using System.DirectoryServices.AccountManagement;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public class DirectoryServicesResolver : IAdResolver
    {
        private readonly ScanCredential _credential;

        public DirectoryServicesResolver()
            : this(null)
        {
        }

        public DirectoryServicesResolver(ScanCredential credential)
        {
            _credential = credential;
        }

        public bool IsAvailable { get { return true; } }

        public ResolvedPrincipal ResolvePrincipal(string sid)
        {
            var resolved = ResolveInContext(ContextType.Domain, sid);
            return resolved ?? ResolveInContext(ContextType.Machine, sid);
        }

        public List<ResolvedPrincipal> GetGroupMembers(string groupSid)
        {
            var resolved = ResolveGroupMembers(ContextType.Domain, groupSid);
            if (resolved.Count > 0) return resolved;
            return ResolveGroupMembers(ContextType.Machine, groupSid);
        }

        public List<ResolvedPrincipal> GetUserGroups(string userSid)
        {
            var resolved = ResolveUserGroups(ContextType.Domain, userSid);
            if (resolved.Count > 0) return resolved;
            return ResolveUserGroups(ContextType.Machine, userSid);
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
