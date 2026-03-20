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
using NtfsAudit.App.Cache;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class IdentityResolutionFallbackTests
    {
        [Fact]
        public void IdentityResolver_RefreshesCachedNonGroupFromAdResolver()
        {
            var cache = new SidNameCache();
            const string sid = "S-1-5-21-123";
            cache.Set(sid, "cached-user", false, false);
            var adResolver = new FakeAdResolver
            {
                ResolvePrincipalHandler = value => new ResolvedPrincipal
                {
                    Sid = value,
                    Name = "fresh-user",
                    IsGroup = false,
                    IsDisabled = true,
                    IsServiceAccount = true,
                    IsAdminAccount = true
                }
            };

            var resolver = new IdentityResolver(cache, adResolver);

            var resolved = resolver.Resolve(sid);

            Assert.Equal("cached-user", resolved.Name);
            Assert.False(resolved.IsGroup);
            Assert.True(resolved.IsDisabled);
            Assert.True(resolved.IsServiceAccount);
            Assert.True(resolved.IsAdminAccount);
            Assert.Single(adResolver.ResolvedSids);
            Assert.True(cache.TryGet(sid, out var refreshedCache));
            Assert.Equal("cached-user", refreshedCache.Name);
            Assert.True(refreshedCache.IsDisabled);
        }

        [Fact]
        public void DirectoryServicesResolver_FallsBackFromDomainToMachineAcrossLookups()
        {
            var principalContexts = new List<ContextType>();
            var groupContexts = new List<ContextType>();
            var userContexts = new List<ContextType>();
            var resolver = new DirectoryServicesResolver(
                null,
                (context, sid) =>
                {
                    principalContexts.Add(context);
                    return context == ContextType.Domain
                        ? null
                        : new ResolvedPrincipal { Sid = sid, Name = "machine-user" };
                },
                (context, sid) =>
                {
                    groupContexts.Add(context);
                    return context == ContextType.Domain
                        ? new List<ResolvedPrincipal>()
                        : new List<ResolvedPrincipal> { new ResolvedPrincipal { Sid = sid, Name = "machine-member" } };
                },
                (context, sid) =>
                {
                    userContexts.Add(context);
                    return context == ContextType.Domain
                        ? new List<ResolvedPrincipal>()
                        : new List<ResolvedPrincipal> { new ResolvedPrincipal { Sid = sid, Name = "machine-group", IsGroup = true } };
                });

            var principal = resolver.ResolvePrincipal("S-1-user");
            var members = resolver.GetGroupMembers("S-1-group");
            var groups = resolver.GetUserGroups("S-1-user");

            Assert.Equal("machine-user", principal.Name);
            Assert.Equal(new[] { ContextType.Domain, ContextType.Machine }, principalContexts);
            Assert.Equal(new[] { ContextType.Domain, ContextType.Machine }, groupContexts);
            Assert.Equal(new[] { ContextType.Domain, ContextType.Machine }, userContexts);
            Assert.Single(members);
            Assert.Equal("machine-member", members[0].Name);
            Assert.Single(groups);
            Assert.Equal("machine-group", groups[0].Name);
        }

        [Fact]
        public void PowerShellAdResolver_DisablesItselfWhenAdModuleIsUnavailable()
        {
            var resolver = new PowerShellAdResolver(
                "powershell.exe",
                null,
                _ => null,
                _ => true);

            Assert.False(resolver.IsAvailable);
            Assert.Null(resolver.ResolvePrincipal("S-1-5-21-123"));
            Assert.Empty(resolver.GetGroupMembers("S-1-5-21-123"));
        }

        [Fact]
        public void PowerShellAdResolver_ParsesJsonResponsesAndInjectsCredentialParameter()
        {
            var scripts = new List<string>();
            var credential = new ScanCredential
            {
                UserName = @"DOMAIN\svc-reader",
                Password = "p'ass"
            };
            var resolver = new PowerShellAdResolver(
                "powershell.exe",
                credential,
                script =>
                {
                    scripts.Add(script);
                    if (script.IndexOf("Get-Module -ListAvailable ActiveDirectory", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return "{}";
                    }

                    if (script.IndexOf("Get-ADObject", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return "{\"Sid\":\"S-1-5-21-200\",\"Name\":\"svc-reader\",\"Class\":\"user\",\"Enabled\":false}";
                    }

                    if (script.IndexOf("Get-ADGroupMember", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return "[{\"Sid\":\"S-1-5-21-201\",\"Name\":\"group-member\",\"Class\":\"group\",\"Enabled\":true}]";
                    }

                    return null;
                },
                _ => true);

            var principal = resolver.ResolvePrincipal("S-1-5-21-200");
            var members = resolver.GetGroupMembers("S-1-5-21-300");

            Assert.True(resolver.IsAvailable);
            Assert.NotNull(principal);
            Assert.Equal("svc-reader", principal.Name);
            Assert.False(principal.IsGroup);
            Assert.True(principal.IsDisabled);
            Assert.Single(members);
            Assert.True(members[0].IsGroup);
            Assert.Contains(scripts, script => script.Contains("-Credential $credential", StringComparison.Ordinal));
            Assert.Contains(scripts, script => script.Contains("ConvertTo-SecureString 'p''ass' -AsPlainText -Force", StringComparison.Ordinal));
            Assert.Contains(scripts, script => script.Contains("PSCredential('DOMAIN\\svc-reader'", StringComparison.Ordinal));
        }

        private sealed class FakeAdResolver : IAdResolver
        {
            public Func<string, ResolvedPrincipal> ResolvePrincipalHandler { get; set; }
            public List<string> ResolvedSids { get; } = new List<string>();

            public bool IsAvailable { get { return true; } }

            public ResolvedPrincipal ResolvePrincipal(string sid)
            {
                ResolvedSids.Add(sid);
                return ResolvePrincipalHandler == null ? null : ResolvePrincipalHandler(sid);
            }

            public List<ResolvedPrincipal> GetGroupMembers(string groupSid)
            {
                return new List<ResolvedPrincipal>();
            }

            public List<ResolvedPrincipal> GetUserGroups(string userSid)
            {
                return new List<ResolvedPrincipal>();
            }
        }
    }
}
