using System.Collections.Generic;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
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
            }

            if (_fallback != null && _fallback.IsAvailable)
            {
                return _fallback.GetUserGroups(userSid);
            }

            return new List<ResolvedPrincipal>();
        }
    }
}
