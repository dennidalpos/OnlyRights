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
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using NtfsAudit.App.Cache;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class SecurityHardeningTests
    {
        [Fact]
        public void IdentityResolver_WhenAnonymizeIdentitiesIsTrue_PseudonymizesCustomSids()
        {
            var cache = new SidNameCache();
            var resolver = new IdentityResolver(cache, null)
            {
                AnonymizeIdentities = true
            };

            // Custom user SID
            var resolvedUser = resolver.Resolve("S-1-5-21-12345-67890-11111-1001");
            Assert.NotNull(resolvedUser);
            Assert.Equal("User_1", resolvedUser.Name);
            Assert.Equal("S-1-5-21-0-0-1-1", resolvedUser.Sid);
            Assert.False(resolvedUser.IsGroup);

            // Repeat call returns the same pseudonym consistently
            var resolvedUserRepeat = resolver.Resolve("S-1-5-21-12345-67890-11111-1001");
            Assert.Equal("User_1", resolvedUserRepeat.Name);
            Assert.Equal("S-1-5-21-0-0-1-1", resolvedUserRepeat.Sid);

            // A second custom user SID gets the next user index
            var resolvedUser2 = resolver.Resolve("S-1-5-21-12345-67890-11111-1002");
            Assert.Equal("User_2", resolvedUser2.Name);
            Assert.Equal("S-1-5-21-0-0-1-2", resolvedUser2.Sid);

            // Custom group SID (well-known groups might not be anonymized, but S-1-5-21-...-513 is a group under SidClassifier)
            var resolvedGroup = resolver.Resolve("S-1-5-21-12345-67890-11111-512"); // Domain Admins (group)
            Assert.NotNull(resolvedGroup);
            Assert.Equal("Group_1", resolvedGroup.Name);
            Assert.Equal("S-1-5-21-0-0-2-1", resolvedGroup.Sid);
            Assert.True(resolvedGroup.IsGroup);
        }

        [Fact]
        public void IdentityResolver_WhenAnonymizeIdentitiesIsTrue_PreservesWellKnownSystemSids()
        {
            var cache = new SidNameCache();
            var resolver = new IdentityResolver(cache, null)
            {
                AnonymizeIdentities = true
            };

            // Everyone SID S-1-1-0 is critical for audit, so it must not be anonymized
            var everyone = resolver.Resolve("S-1-1-0");
            Assert.Equal("S-1-1-0", everyone.Sid);
            Assert.Contains("Everyone", everyone.Name, StringComparison.OrdinalIgnoreCase);

            // Local System S-1-5-18
            var system = resolver.Resolve("S-1-5-18");
            Assert.Equal("S-1-5-18", system.Sid);
        }

        [Fact]
        public void ScanService_AnonymizePath_MasksUserProfilePathsAndProfiles()
        {
            var service = new ScanService(null, null);

            var path1 = @"C:\Users\JohnDoe\Documents\file.txt";
            var anon1 = service.AnonymizePath(path1);
            Assert.Equal(@"C:\Users\User_1\Documents\file.txt", anon1);

            var path2 = @"C:\Users\JohnDoe\Desktop";
            var anon2 = service.AnonymizePath(path2);
            Assert.Equal(@"C:\Users\User_1\Desktop", anon2);

            var path3 = @"C:\Users\JaneDoe\Desktop";
            var anon3 = service.AnonymizePath(path3);
            Assert.Equal(@"C:\Users\User_2\Desktop", anon3);

            var path4 = @"C:\Users\Public\Documents";
            var anon4 = service.AnonymizePath(path4);
            Assert.Equal(path4, anon4); // Preserves system folders like Public

            var path5 = @"\\server\profiles\JackDoe\settings.xml";
            var anon5 = service.AnonymizePath(path5);
            Assert.Equal(@"\\server\profiles\User_3\settings.xml", anon5);
        }

        [Fact]
        public void SecurityHardeningHelper_SecureFileAndDirectory_RunsWithoutExceptions()
        {
            var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            
            var tempFile = Path.Combine(tempDir, "test.txt");
            File.WriteAllText(tempFile, "test content");

            try
            {
                // Verify methods run without exceptions
                SecurityHardeningHelper.SecureDirectory(tempDir);
                SecurityHardeningHelper.SecureFile(tempFile);

                // Verify ACL permissions can be retrieved and inheritance is protected
                if (OperatingSystem.IsWindows())
                {
                    var fileInfo = new FileInfo(tempFile);
                    var fileSecurity = fileInfo.GetAccessControl();
                    Assert.True(fileSecurity.AreAccessRulesProtected); // Inheritance is disabled

                    var dirInfo = new DirectoryInfo(tempDir);
                    var dirSecurity = dirInfo.GetAccessControl();
                    Assert.True(dirSecurity.AreAccessRulesProtected); // Inheritance is disabled
                }
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
        }
    }
}
