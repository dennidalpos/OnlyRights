using NtfsAudit.App.Services;
using NtfsAudit.Core.Logging;
using NtfsAudit.Core.Cache;
using NtfsAudit.Core.Services;
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
using Newtonsoft.Json;
using NtfsAudit.Core.Export;
using NtfsAudit.Core.Models;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class CredentialPrivacyTests
    {
        [Fact]
        public void ErrorEntrySerialization_DoesNotExposeCredentialSecretFields()
        {
            var json = JsonConvert.SerializeObject(new ErrorEntry
            {
                Path = @"\\server\share",
                ErrorType = "AccessDenied",
                Message = "Access denied for configured scan account."
            });

            Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ProtectedPassword", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Credential", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Secret!123", json, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void ExportRecordSerialization_DoesNotExposeCredentialPayload()
        {
            var json = JsonConvert.SerializeObject(new ExportRecord
            {
                FolderPath = @"\\server\share",
                PrincipalName = @"CONTOSO\Reader",
                PrincipalSid = "S-1-5-21-1",
                PrincipalType = "User",
                PermissionLayer = PermissionLayer.Ntfs,
                RightsSummary = "Read"
            });

            Assert.DoesNotContain("Password", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ProtectedPassword", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Credential", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Secret!123", json, StringComparison.OrdinalIgnoreCase);
        }
    }
}
