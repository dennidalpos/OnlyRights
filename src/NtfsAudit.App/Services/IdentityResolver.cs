using System;
using System.Security.Principal;
using NtfsAudit.App.Cache;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public class IdentityResolver
    {
        private readonly SidNameCache _sidNameCache;
        private readonly IAdResolver _adResolver;

        public IdentityResolver(SidNameCache sidNameCache, IAdResolver adResolver)
        {
            _sidNameCache = sidNameCache;
            _adResolver = adResolver;
        }

        public ResolvedPrincipal Resolve(string sid)
        {
            SidCacheEntry cached;
            if (_sidNameCache.TryGet(sid, out cached) && !string.IsNullOrWhiteSpace(cached.Name))
            {
                if (!cached.IsGroup && _adResolver != null)
                {
                    var refreshed = _adResolver.ResolvePrincipal(sid);
                    if (refreshed != null)
                    {
                        var refreshedName = string.IsNullOrWhiteSpace(cached.Name) ? refreshed.Name : cached.Name;
                        _sidNameCache.Set(sid, refreshedName ?? sid, refreshed.IsGroup, refreshed.IsDisabled);
                        return new ResolvedPrincipal
                        {
                            Sid = sid,
                            Name = refreshedName ?? sid,
                            IsGroup = refreshed.IsGroup,
                            IsDisabled = refreshed.IsDisabled,
                            IsServiceAccount = refreshed.IsServiceAccount,
                            IsAdminAccount = refreshed.IsAdminAccount
                        };
                    }
                }

                return BuildResolvedPrincipal(sid, cached.Name, cached.IsGroup, cached.IsDisabled);
            }

            string name = null;
            try
            {
                var sidObj = new SecurityIdentifier(sid);
                var account = sidObj.Translate(typeof(NTAccount)) as NTAccount;
                if (account != null)
                {
                    name = account.Value;
                }
            }
            catch
            {
            }

            var adPrincipal = _adResolver != null ? _adResolver.ResolvePrincipal(sid) : null;
            if (adPrincipal != null)
            {
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = adPrincipal.Name;
                }

                _sidNameCache.Set(sid, name ?? sid, adPrincipal.IsGroup, adPrincipal.IsDisabled);
                return BuildResolvedPrincipal(sid, name ?? sid, adPrincipal.IsGroup, adPrincipal.IsDisabled);
            }

            var resolved = BuildResolvedPrincipal(sid, string.IsNullOrWhiteSpace(name) ? sid : name, false, false);

            _sidNameCache.Set(sid, resolved.Name, resolved.IsGroup, resolved.IsDisabled);
            return resolved;
        }

        private ResolvedPrincipal BuildResolvedPrincipal(string sid, string name, bool isGroup, bool isDisabled)
        {
            var resolvedName = string.IsNullOrWhiteSpace(name) ? sid : name;
            return new ResolvedPrincipal
            {
                Sid = sid,
                Name = resolvedName,
                IsGroup = isGroup,
                IsDisabled = isDisabled,
                IsServiceAccount = IsServiceAccountName(resolvedName),
                IsAdminAccount = IsAdminAccountName(resolvedName)
            };
        }

        private bool IsServiceAccountName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var normalized = name.ToLowerInvariant();
            if (normalized == "nt authority\\system")
            {
                return true;
            }
            if (normalized.Contains("svc") || normalized.Contains("service"))
            {
                return true;
            }
            return normalized.EndsWith("$", StringComparison.Ordinal);
        }

        private bool IsAdminAccountName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            var normalized = name.ToLowerInvariant();
            return normalized.Contains("admin");
        }
    }
}
