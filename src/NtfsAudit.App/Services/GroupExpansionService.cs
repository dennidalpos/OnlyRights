using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NtfsAudit.App.Cache;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public class GroupExpansionService
    {
        private static readonly HashSet<string> PrivilegedGroupNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
        {
            "domain admins",
            "enterprise admins",
            "schema admins",
            "administrators",
            "account operators",
            "server operators",
            "backup operators",
            "print operators"
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
                    var normalized = NormalizeGroupName(group == null ? null : group.Name);
                    if (string.IsNullOrWhiteSpace(normalized)) continue;
                    if (PrivilegedGroupNames.Contains(normalized)) return true;
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
                var isServiceAccount = IsServiceAccountName(member.Name);
                var isAdminAccount = IsAdminAccountName(member.Name);
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

        private bool IsServiceAccountName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var normalized = name.ToLowerInvariant();
            if (normalized.Contains("svc") || normalized.Contains("service"))
            {
                return true;
            }
            return normalized.EndsWith("$");
        }

        private bool IsAdminAccountName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            return name.ToLowerInvariant().Contains("admin");
        }

        private static string NormalizeGroupName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var trimmed = name.Trim();
            var separatorIndex = trimmed.LastIndexOf('\\');
            if (separatorIndex >= 0 && separatorIndex + 1 < trimmed.Length)
            {
                trimmed = trimmed.Substring(separatorIndex + 1);
            }
            return trimmed.Trim();
        }
    }
}
