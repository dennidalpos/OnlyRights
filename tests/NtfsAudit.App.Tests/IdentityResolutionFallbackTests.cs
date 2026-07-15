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
                (Func<string, string>)(_ => null),
                _ => true);

            Assert.False(resolver.IsAvailable);
            Assert.False(string.IsNullOrWhiteSpace(resolver.AvailabilityDiagnostic));
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

        [Fact]
        public void PowerShellAdResolver_CapturesExecutionDiagnostics_ForCommandFailures()
        {
            var moduleProbeDone = false;
            var resolver = new PowerShellAdResolver(
                "powershell.exe",
                null,
                script =>
                {
                    if (!moduleProbeDone)
                    {
                        moduleProbeDone = true;
                        return new PowerShellAdResolver.PowerShellExecutionResult
                        {
                            ExitCode = 0,
                            Output = "{}"
                        };
                    }

                    return new PowerShellAdResolver.PowerShellExecutionResult
                    {
                        ExitCode = 17,
                        Error = "Access denied"
                    };
                },
                _ => true);

            var resolved = resolver.ResolvePrincipal("S-1-5-21-500");

            Assert.True(resolver.IsAvailable);
            Assert.Null(resolved);
            Assert.Contains("exit code 17", resolver.LastDiagnostic, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ResolvePrincipal", resolver.LastDiagnostic, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void PowerShellAdResolver_CapturesJsonDiagnostics_ForInvalidPayloads()
        {
            var moduleProbeDone = false;
            var resolver = new PowerShellAdResolver(
                "powershell.exe",
                null,
                script =>
                {
                    if (!moduleProbeDone)
                    {
                        moduleProbeDone = true;
                        return new PowerShellAdResolver.PowerShellExecutionResult
                        {
                            ExitCode = 0,
                            Output = "{}"
                        };
                    }

                    return new PowerShellAdResolver.PowerShellExecutionResult
                    {
                        ExitCode = 0,
                        Output = "not-json"
                    };
                },
                _ => true);

            var resolved = resolver.ResolvePrincipal("S-1-5-21-500");

            Assert.True(resolver.IsAvailable);
            Assert.Null(resolved);
            Assert.Contains("JSON", resolver.LastDiagnostic, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void IdentityResolver_ClassifiesWellKnownGroupsAndDomainGroupsOffline()
        {
            var cache = new SidNameCache();
            var resolver = new IdentityResolver(cache, null);

            var everyone = resolver.Resolve("S-1-1-0");
            var admins = resolver.Resolve("S-1-5-32-544");
            var domainAdmins = resolver.Resolve("S-1-5-21-100-200-300-512");
            var system = resolver.Resolve("S-1-5-18");
            var regularUser = resolver.Resolve("S-1-5-21-100-200-300-1001");

            Assert.True(everyone.IsGroup);
            Assert.Equal("Group", everyone.Type);

            Assert.True(admins.IsGroup);
            Assert.Equal("Group", admins.Type);

            Assert.True(domainAdmins.IsGroup);
            Assert.Equal("Group", domainAdmins.Type);

            Assert.False(system.IsGroup);
            Assert.Equal("User", system.Type);

            Assert.False(regularUser.IsGroup);
            Assert.Equal("User", regularUser.Type);
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
