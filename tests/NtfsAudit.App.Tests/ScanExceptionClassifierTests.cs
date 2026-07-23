using NtfsAudit.App.Services;
using NtfsAudit.Core.Logging;
using NtfsAudit.Core.Export;
using NtfsAudit.Core.Cache;
using NtfsAudit.Core.Models;
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
using NtfsAudit.Core.Services;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class ScanExceptionClassifierTests
    {
        [Fact]
        public void BuildErrorEntry_NormalizesPrivilegeErrors()
        {
            var entry = ScanExceptionClassifier.BuildErrorEntry(
                @"\\server\share",
                new PrivilegeNotHeldException("SeSecurityPrivilege"));

            Assert.Equal("SecurityPrivilegeUnavailable", entry.ErrorType);
            Assert.Contains("The scan continues with the available data.", entry.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void BuildErrorEntry_NormalizesUnsupportedFilesystemErrors()
        {
            var entry = ScanExceptionClassifier.BuildErrorEntry(
                @"\\server\share",
                new NotSupportedException("unsupported filesystem"));

            Assert.Equal("PathUnsupported", entry.ErrorType);
            Assert.Contains(@"Unsupported filesystem or provider for path: \\server\share", entry.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void BuildErrorEntry_NormalizesUnavailablePaths()
        {
            var entry = ScanExceptionClassifier.BuildErrorEntry(
                @"\\server\share",
                new IOException("network path not found"));

            Assert.Equal("PathUnavailable", entry.ErrorType);
            Assert.Contains(@"Path is invalid or unreachable: \\server\share", entry.Message, StringComparison.Ordinal);
        }
    }
}
