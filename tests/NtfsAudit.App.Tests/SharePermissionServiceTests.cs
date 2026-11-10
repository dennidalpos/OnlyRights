using NtfsAudit.App.Services;
using NtfsAudit.Core.Logging;
using NtfsAudit.Core.Export;
using NtfsAudit.Core.Cache;
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
using System.IO;
using System.Security.AccessControl;
using NtfsAudit.Core.Models;
using NtfsAudit.Core.Services;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class SharePermissionServiceTests
    {
        [Fact]
        public void TryGetSharePermissions_LeavesDiagnosticEmpty_ForNonSharePaths()
        {
            var service = new SharePermissionService(
                null,
                _ => null,
                (server, share, options) => null);

            var result = service.TryGetSharePermissions(@"C:\Data");

            Assert.Null(result);
            Assert.Null(service.LastDiagnostic);
        }

        [Fact]
        public void TryGetSharePermissions_ExposesAccessDeniedDiagnostic()
        {
            var service = new SharePermissionService(
                null,
                _ => Tuple.Create("nas01", "public"),
                (server, share, options) => throw new UnauthorizedAccessException("Access denied"));

            var result = service.TryGetSharePermissions(@"\\nas01\public");

            Assert.Null(result);
            Assert.NotNull(service.LastDiagnostic);
            Assert.Equal("SharePermissionsAccessDenied", service.LastDiagnostic.ErrorType);
            Assert.Contains(@"\\nas01\public", service.LastDiagnostic.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void TryGetSharePermissions_ExposesReachabilityDiagnostic()
        {
            var service = new SharePermissionService(
                null,
                _ => Tuple.Create("nas01", "public"),
                (server, share, options) => throw new IOException("The RPC server is unavailable."));

            var result = service.TryGetSharePermissions(@"\\nas01\public");

            Assert.Null(result);
            Assert.NotNull(service.LastDiagnostic);
            Assert.Equal("SharePermissionsUnavailable", service.LastDiagnostic.ErrorType);
            Assert.Contains("WMI", service.LastDiagnostic.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void TryGetSharePermissions_MapsMissingSecurityPrivilege_ToAccessDeniedDiagnostic()
        {
            var service = new SharePermissionService(
                null,
                _ => Tuple.Create("nas01", "public"),
                (server, share, options) => throw new PrivilegeNotHeldException("SeSecurityPrivilege"));

            var result = service.TryGetSharePermissions(@"\\nas01\public");

            Assert.Null(result);
            Assert.NotNull(service.LastDiagnostic);
            Assert.Equal("SharePermissionsAccessDenied", service.LastDiagnostic.ErrorType);
        }

        [Fact]
        public void TryGetSharePermissions_ReturnsLoadedPermissions_ForSharePaths()
        {
            var expected = new SharePermissionContext(
                "nas01",
                "public",
                new System.Collections.Generic.List<SharePermission>
                {
                    new SharePermission
                    {
                        ShareName = "public",
                        ShareServer = "nas01",
                        PrincipalName = "Everyone",
                        PrincipalSid = "S-1-1-0",
                        PrincipalType = "Group",
                        AccessType = PermissionDecision.Allow,
                        RightsMask = (int)FileSystemRights.ReadAndExecute,
                        RightsSummary = "Read"
                    }
                });
            var service = new SharePermissionService(
                null,
                _ => Tuple.Create("nas01", "public"),
                (server, share, options) => expected);

            var result = service.TryGetSharePermissions(@"\\nas01\public\folder");

            Assert.Same(expected, result);
            Assert.Null(service.LastDiagnostic);
            Assert.Equal("nas01", result.Server);
            Assert.Equal("public", result.ShareName);
            Assert.Single(result.Permissions);
        }

        [Fact]
        public void TryGetSharePermissions_MapsWin32AccessDeniedFallback_ToAccessDeniedDiagnostic()
        {
            var wmiEx = new IOException("The RPC server is unavailable.");
            var win32Ex = new System.ComponentModel.Win32Exception(5, "NetShareGetInfo failed with error code 5"); // 5 is Access Denied
            var combinedEx = new AggregateException("WMI connection failed, and Win32 fallback failed.", wmiEx, win32Ex);

            var service = new SharePermissionService(
                null,
                _ => Tuple.Create("nas01", "public"),
                (server, share, options) => throw combinedEx);

            var result = service.TryGetSharePermissions(@"\\nas01\public");

            Assert.Null(result);
            Assert.NotNull(service.LastDiagnostic);
            Assert.Equal("SharePermissionsAccessDenied", service.LastDiagnostic.ErrorType);
        }
    }
}
