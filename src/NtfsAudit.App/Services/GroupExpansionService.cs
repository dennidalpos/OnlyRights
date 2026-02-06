using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NtfsAudit.App.Cache;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public class GroupExpansionService
    {
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

        private List<ResolvedPrincipal> GetMembers(string groupSid)
        {
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
                        IsGroup = member.IsGroup
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
                membersToCache.Add(new ResolvedPrincipal
                {
                    Sid = member.Sid,
                    Name = member.Name,
                    IsGroup = member.IsGroup
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
    }
}
