using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Cache
{
    public class GroupMembershipCache
    {
        private readonly ConcurrentDictionary<string, CacheEntry> _cache = new ConcurrentDictionary<string, CacheEntry>();
        private readonly TimeSpan _ttl;

        public GroupMembershipCache(TimeSpan ttl)
        {
            _ttl = ttl;
        }

        public bool TryGet(string groupSid, out List<ResolvedPrincipal> members)
        {
            members = null;
            CacheEntry entry;
            if (!_cache.TryGetValue(groupSid, out entry)) return false;
            if (DateTime.UtcNow - entry.Timestamp > _ttl)
            {
                CacheEntry removed;
                _cache.TryRemove(groupSid, out removed);
                return false;
            }

            members = entry.Members;
            return true;
        }

        public void Set(string groupSid, List<ResolvedPrincipal> members)
        {
            _cache[groupSid] = new CacheEntry { Members = members, Timestamp = DateTime.UtcNow };
        }

        private class CacheEntry
        {
            public List<ResolvedPrincipal> Members { get; set; }
            public DateTime Timestamp { get; set; }
        }
    }
}
