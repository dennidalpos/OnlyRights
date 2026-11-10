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
using System.Security.Principal;
using NtfsAudit.Core.Cache;
using NtfsAudit.Core.Models;

namespace NtfsAudit.Core.Services
{
    public class IdentityResolver
    {
        private readonly SidNameCache _sidNameCache;
        private readonly IAdResolver _adResolver;

        public bool AnonymizeIdentities { get; set; }
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ResolvedPrincipal> _anonymizedCache = new System.Collections.Concurrent.ConcurrentDictionary<string, ResolvedPrincipal>(StringComparer.OrdinalIgnoreCase);
        private int _anonymizedUserCount = 0;
        private int _anonymizedGroupCount = 0;

        public IdentityResolver(SidNameCache sidNameCache, IAdResolver adResolver)
        {
            _sidNameCache = sidNameCache;
            _adResolver = adResolver;
        }

        public ResolvedPrincipal Resolve(string sid)
        {
            if (AnonymizeIdentities && ShouldAnonymizeSid(sid))
            {
                return _anonymizedCache.GetOrAdd(sid, s => AnonymizePrincipal(s));
            }

            return ResolveCore(sid);
        }

        private bool ShouldAnonymizeSid(string sid)
        {
            if (string.IsNullOrWhiteSpace(sid)) return false;
            if (string.Equals(sid, "SCAN_OPTIONS", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(sid, "S-1-0-0", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(sid, "S-1-1-0", StringComparison.OrdinalIgnoreCase)) return false; // Everyone
            if (string.Equals(sid, "S-1-3-0", StringComparison.OrdinalIgnoreCase)) return false; // Creator Owner
            if (string.Equals(sid, "S-1-3-1", StringComparison.OrdinalIgnoreCase)) return false; // Creator Group
            if (string.Equals(sid, "S-1-5-1", StringComparison.OrdinalIgnoreCase)) return false; // Dialup
            if (string.Equals(sid, "S-1-5-2", StringComparison.OrdinalIgnoreCase)) return false; // Network
            if (string.Equals(sid, "S-1-5-3", StringComparison.OrdinalIgnoreCase)) return false; // Batch
            if (string.Equals(sid, "S-1-5-4", StringComparison.OrdinalIgnoreCase)) return false; // Interactive
            if (string.Equals(sid, "S-1-5-6", StringComparison.OrdinalIgnoreCase)) return false; // Service
            if (string.Equals(sid, "S-1-5-7", StringComparison.OrdinalIgnoreCase)) return false; // Anonymous
            if (string.Equals(sid, "S-1-5-9", StringComparison.OrdinalIgnoreCase)) return false; // Enterprise Domain Controllers
            if (string.Equals(sid, "S-1-5-11", StringComparison.OrdinalIgnoreCase)) return false; // Authenticated Users
            if (string.Equals(sid, "S-1-5-18", StringComparison.OrdinalIgnoreCase)) return false; // Local System
            if (string.Equals(sid, "S-1-5-19", StringComparison.OrdinalIgnoreCase)) return false; // Local Service
            if (string.Equals(sid, "S-1-5-20", StringComparison.OrdinalIgnoreCase)) return false; // Network Service

            if (sid.StartsWith("S-1-5-32-", StringComparison.OrdinalIgnoreCase)) return false; // BUILTIN groups
            if (sid.StartsWith("S-1-5-80-", StringComparison.OrdinalIgnoreCase)) return false; // Virtual service accounts

            return true;
        }

        private ResolvedPrincipal AnonymizePrincipal(string sid)
        {
            var resolved = ResolveCore(sid);
            if (resolved.IsGroup)
            {
                var idx = System.Threading.Interlocked.Increment(ref _anonymizedGroupCount);
                return new ResolvedPrincipal
                {
                    Sid = "S-1-5-21-0-0-2-" + idx,
                    Name = "Group_" + idx,
                    IsGroup = true,
                    IsDisabled = resolved.IsDisabled,
                    IsServiceAccount = resolved.IsServiceAccount,
                    IsAdminAccount = resolved.IsAdminAccount
                };
            }
            else
            {
                var idx = System.Threading.Interlocked.Increment(ref _anonymizedUserCount);
                return new ResolvedPrincipal
                {
                    Sid = "S-1-5-21-0-0-1-" + idx,
                    Name = "User_" + idx,
                    IsGroup = false,
                    IsDisabled = resolved.IsDisabled,
                    IsServiceAccount = resolved.IsServiceAccount,
                    IsAdminAccount = resolved.IsAdminAccount
                };
            }
        }

        private ResolvedPrincipal ResolveCore(string sid)
        {
            if (string.IsNullOrWhiteSpace(sid))
            {
                return new ResolvedPrincipal
                {
                    Sid = sid,
                    Name = sid ?? string.Empty,
                    IsGroup = false,
                    IsDisabled = false,
                    IsServiceAccount = false,
                    IsAdminAccount = false
                };
            }
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
                IsGroup = isGroup || SidClassifier.IsGroupSid(sid),
                IsDisabled = isDisabled,
                IsServiceAccount = SidClassifier.IsServiceAccountSid(sid),
                IsAdminAccount = SidClassifier.IsPrivilegedGroupSid(sid)
            };
        }
    }
}
