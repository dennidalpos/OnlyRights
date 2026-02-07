using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using NtfsAudit.App.Cache;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public class GroupExpansionService
    {
        private static readonly WellKnownSidType[] PrivilegedGroupSids =
        {
            WellKnownSidType.BuiltinAdministratorsSid,
            WellKnownSidType.AccountAdministratorSid,
            WellKnownSidType.AccountDomainAdminsSid,
            WellKnownSidType.AccountEnterpriseAdminsSid,
            WellKnownSidType.AccountSchemaAdminsSid,
            WellKnownSidType.AccountOperatorsSid,
            WellKnownSidType.ServerOperatorsSid,
            WellKnownSidType.BackupOperatorsSid,
            WellKnownSidType.PrintOperatorsSid
        };

        private readonly IAdResolver _adResolver;
        private readonly GroupMembershipCache _cache;

        public GroupExpansionService(IAdResolver adResolver, GroupMembershipCache cache)
        {
            _adResolver = adResolver;
            _cache = cache;
        }

        public List<ResolvedPrincipal> ExpandGroup(string groupSid, CancellationToken token)
        {
            var result = new List<ResolvedPrincipal>();
            if (string.IsNullOrWhiteSpace(groupSid))
            {
                return result;
            }
            var queue = new Queue<string>();
            var visited = new HashSet<string>();
            queue.Enqueue(groupSid);
            visited.Add(groupSid);

            while (queue.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var current = queue.Dequeue();
                var members = GetMembers(current);
                foreach (var member in members)
                {
                    if (member.IsGroup)
                    {
                        if (string.IsNullOrWhiteSpace(member.Sid)) continue;
                        if (visited.Add(member.Sid))
                        {
                            queue.Enqueue(member.Sid);
                        }
                    }
                    else
                    {
                        result.Add(member);
                    }
                }
            }

            return result
                .GroupBy(m => string.IsNullOrWhiteSpace(m.Sid) ? m.Name : m.Sid)
                .Select(g => g.First())
                .ToList();
        }

        public bool IsPrivilegedUser(string userSid)
        {
            if (string.IsNullOrWhiteSpace(userSid)) return false;
            if (_adResolver == null || !_adResolver.IsAvailable) return false;
            try
            {
                var groups = _adResolver.GetUserGroups(userSid);
                if (groups == null || groups.Count == 0) return false;
                foreach (var group in groups)
                {
                    if (group == null || string.IsNullOrWhiteSpace(group.Sid)) continue;
                    if (IsPrivilegedSid(group.Sid)) return true;
                }
            }
            catch
            {
            }
            return false;
        }

        private List<ResolvedPrincipal> GetMembers(string groupSid)
        {
            if (string.IsNullOrWhiteSpace(groupSid))
            {
                return new List<ResolvedPrincipal>();
            }
            if (_adResolver == null || !_adResolver.IsAvailable)
            {
                return new List<ResolvedPrincipal>();
            }

            List<ResolvedPrincipal> cached;
            if (_cache.TryGet(groupSid, out cached))
            {
                var cachedMembers = cached
                    .Select(member => new ResolvedPrincipal
                    {
                        Sid = member.Sid,
                        Name = member.Name,
                        IsGroup = member.IsGroup,
                        IsDisabled = member.IsDisabled,
                        IsServiceAccount = member.IsServiceAccount,
                        IsAdminAccount = member.IsAdminAccount
                    })
                    .ToList();
                if (!cachedMembers.Any(member => member.IsGroup && string.IsNullOrWhiteSpace(member.Sid)))
                {
                    return cachedMembers;
                }
            }

            var members = _adResolver.GetGroupMembers(groupSid);
            var membersToCache = new List<ResolvedPrincipal>();
            foreach (var member in members)
            {
                EnsureSid(member, member.Sid);
                var isServiceAccount = IsServiceAccountSid(member.Sid);
                var isAdminAccount = IsPrivilegedSid(member.Sid);
                member.IsServiceAccount = isServiceAccount;
                member.IsAdminAccount = isAdminAccount;
                membersToCache.Add(new ResolvedPrincipal
                {
                    Sid = member.Sid,
                    Name = member.Name,
                    IsGroup = member.IsGroup,
                    IsDisabled = member.IsDisabled,
                    IsServiceAccount = isServiceAccount,
                    IsAdminAccount = isAdminAccount
                });
            }
            _cache.Set(groupSid, membersToCache);
            return members;
        }

        private ResolvedPrincipal EnsureSid(ResolvedPrincipal principal, string fallbackSid)
        {
            if (principal == null) return null;
            if (!string.IsNullOrWhiteSpace(principal.Sid)) return principal;

            if (!string.IsNullOrWhiteSpace(fallbackSid))
            {
                principal.Sid = fallbackSid;
                return principal;
            }

            return principal;
        }

        private bool IsServiceAccountSid(string sid)
        {
            if (string.IsNullOrWhiteSpace(sid)) return false;
            if (sid.StartsWith("S-1-5-80-", System.StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            try
            {
                var sidObj = new System.Security.Principal.SecurityIdentifier(sid);
                return sidObj.IsWellKnown(System.Security.Principal.WellKnownSidType.LocalSystemSid)
                    || sidObj.IsWellKnown(System.Security.Principal.WellKnownSidType.LocalServiceSid)
                    || sidObj.IsWellKnown(System.Security.Principal.WellKnownSidType.NetworkServiceSid);
            }
            catch
            {
                return false;
            }
        }

        private bool IsPrivilegedSid(string sid)
        {
            if (string.IsNullOrWhiteSpace(sid)) return false;
            try
            {
                var sidObj = new System.Security.Principal.SecurityIdentifier(sid);
                foreach (var privilegedSid in PrivilegedGroupSids)
                {
                    if (sidObj.IsWellKnown(privilegedSid))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }
            return false;
        }
    }
}
