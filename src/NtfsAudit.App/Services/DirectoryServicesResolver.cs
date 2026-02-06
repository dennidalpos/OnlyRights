using System.Collections.Generic;
using System.DirectoryServices.AccountManagement;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public class DirectoryServicesResolver : IAdResolver
    {
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
                using (var ctx = new PrincipalContext(type))
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
                using (var ctx = new PrincipalContext(type))
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
                using (var ctx = new PrincipalContext(type))
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

        private ResolvedPrincipal MapPrincipal(Principal principal)
        {
            return new ResolvedPrincipal
            {
                Sid = principal.Sid == null ? null : principal.Sid.ToString(),
                Name = principal.SamAccountName ?? principal.Name,
                IsGroup = principal is GroupPrincipal
            };
        }
    }
}
