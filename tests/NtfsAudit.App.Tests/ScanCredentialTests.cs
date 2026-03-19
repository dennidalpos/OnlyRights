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
using System.IO;
using Newtonsoft.Json;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;
using NtfsAudit.Service;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class ScanCredentialTests
    {
        [Fact]
        public void ScanCredentialProtector_RoundTripsCurrentUserAndLocalMachine()
        {
            var original = new ScanCredential
            {
                UserName = @"CONTOSO\scanner",
                Password = "Secret!123"
            };

            var currentUserProtected = ScanCredentialProtector.ProtectForCurrentUser(original);
            var localMachineProtected = ScanCredentialProtector.ProtectForLocalMachine(original);

            var currentUserRuntime = ScanCredentialProtector.ResolveForRuntime(currentUserProtected);
            var localMachineRuntime = ScanCredentialProtector.ResolveForRuntime(localMachineProtected);

            Assert.NotNull(currentUserProtected);
            Assert.NotNull(localMachineProtected);
            Assert.True(string.IsNullOrWhiteSpace(currentUserProtected.Password));
            Assert.True(string.IsNullOrWhiteSpace(localMachineProtected.Password));
            Assert.False(string.IsNullOrWhiteSpace(currentUserProtected.ProtectedPassword));
            Assert.False(string.IsNullOrWhiteSpace(localMachineProtected.ProtectedPassword));
            Assert.Equal(original.UserName, currentUserRuntime.UserName);
            Assert.Equal(original.UserName, localMachineRuntime.UserName);
            Assert.Equal(original.Password, currentUserRuntime.Password);
            Assert.Equal(original.Password, localMachineRuntime.Password);
        }

        [Fact]
        public void ScanOptions_CreateArchiveSafeCopy_RemovesCredentialPayload()
        {
            var options = new ScanOptions
            {
                RootPath = @"\\server\share",
                CredentialSource = "RootOverride",
                Credential = new ScanCredential
                {
                    UserName = @"CONTOSO\scanner",
                    ProtectedPassword = "encrypted",
                    ProtectionScope = "LocalMachine"
                }
            };

            var safeCopy = options.CreateArchiveSafeCopy();

            Assert.NotSame(options, safeCopy);
            Assert.Equal(options.RootPath, safeCopy.RootPath);
            Assert.Null(safeCopy.Credential);
            Assert.Null(safeCopy.CredentialSource);
        }

        [Fact]
        public void ServiceJobFileHandler_LoadsProtectedCredentialsWithoutDiscardingThem()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "NtfsAudit.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var protectedCredential = ScanCredentialProtector.ProtectForLocalMachine(new ScanCredential
                {
                    UserName = @"CONTOSO\scanner",
                    Password = "Secret!123"
                });

                var jobPath = Path.Combine(tempRoot, "job_with_credentials.json");
                var job = new ServiceScanJob
                {
                    JobId = Guid.NewGuid().ToString("N"),
                    CreatedAtUtc = DateTime.UtcNow,
                    ScanOptions = new List<ScanOptions>
                    {
                        new ScanOptions
                        {
                            RootPath = @"\\server\share",
                            OutputDirectory = tempRoot,
                            CredentialSource = "RootOverride",
                            Credential = protectedCredential
                        }
                    }
                };
                File.WriteAllText(jobPath, JsonConvert.SerializeObject(job, Formatting.Indented));

                var handler = new ServiceJobFileHandler();
                var loaded = handler.TryLoad(jobPath, out var loadedJob, out var runnableOptions, out var failureReason);

                Assert.True(loaded);
                Assert.NotNull(loadedJob);
                Assert.Null(failureReason);
                Assert.Single(runnableOptions);
                Assert.NotNull(runnableOptions[0].Credential);
                Assert.Equal("RootOverride", runnableOptions[0].CredentialSource);
                Assert.Equal(protectedCredential.UserName, runnableOptions[0].Credential.UserName);
                Assert.Equal(protectedCredential.ProtectedPassword, runnableOptions[0].Credential.ProtectedPassword);
                Assert.True(string.IsNullOrWhiteSpace(runnableOptions[0].Credential.Password));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }
    }
}
