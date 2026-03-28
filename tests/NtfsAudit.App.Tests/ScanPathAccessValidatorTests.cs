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
using System.ComponentModel;
using System.IO;
using System.Security.AccessControl;
using NtfsAudit.App.Services;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class ScanPathAccessValidatorTests
    {
        [Fact]
        public void ValidateDirectoryRootCore_ReturnsNull_ForExistingDirectory()
        {
            var message = ScanPathAccessValidator.ValidateDirectoryRootCore(
                @"\\server\share",
                _ => FileAttributes.Directory);

            Assert.Null(message);
        }

        [Fact]
        public void ValidateDirectoryRootCore_ReturnsFileMessage_ForFiles()
        {
            var message = ScanPathAccessValidator.ValidateDirectoryRootCore(
                @"\\server\share\report.txt",
                _ => FileAttributes.Normal);

            Assert.Equal(@"Il percorso selezionato è un file e non una cartella: \\server\share\report.txt", message);
        }

        [Theory]
        [InlineData("Method failed with unexpected error code 1326.")]
        [InlineData("Nome utente o password non corretta.")]
        [InlineData("Logon failure: unknown user name or bad password.")]
        public void BuildValidationMessage_ReturnsCredentialError_ForLogonFailureMessages(string errorMessage)
        {
            var message = ScanPathAccessValidator.BuildValidationMessage(
                @"\\server\share",
                new InvalidOperationException(errorMessage));

            Assert.Equal(@"Credenziali non valide o rifiutate per il percorso: \\server\share", message);
        }

        [Fact]
        public void BuildValidationMessage_ReturnsCredentialError_ForWin32LogonFailure()
        {
            var message = ScanPathAccessValidator.BuildValidationMessage(
                @"\\server\share",
                new Win32Exception(1326));

            Assert.Equal(@"Credenziali non valide o rifiutate per il percorso: \\server\share", message);
        }

        [Fact]
        public void BuildValidationMessage_ReturnsAccessDenied_ForUnauthorizedAccess()
        {
            var message = ScanPathAccessValidator.BuildValidationMessage(
                @"\\server\share",
                new UnauthorizedAccessException("denied"));

            Assert.Equal(@"Accesso negato al percorso: \\server\share", message);
        }

        [Fact]
        public void BuildValidationMessage_ReturnsGenericReachability_ForIoErrors()
        {
            var message = ScanPathAccessValidator.BuildValidationMessage(
                @"\\server\share",
                new IOException("network path not found"));

            Assert.Equal(@"Percorso non valido o non raggiungibile: \\server\share", message);
        }

        [Fact]
        public void BuildValidationMessage_ReturnsUnsupportedMessage_ForUnsupportedFilesystem()
        {
            var message = ScanPathAccessValidator.BuildValidationMessage(
                @"\\server\share",
                new NotSupportedException("filesystem unsupported"));

            Assert.Equal(@"Filesystem o provider non supportato per il percorso: \\server\share", message);
        }

        [Fact]
        public void BuildValidationMessage_ReturnsPrivilegeMessage_ForMissingSecurityPrivilege()
        {
            var message = ScanPathAccessValidator.BuildValidationMessage(
                @"\\server\share",
                new PrivilegeNotHeldException("SeSecurityPrivilege"));

            Assert.Equal(@"Privilegi insufficienti per leggere owner o audit del percorso: \\server\share. La scansione prosegue con i dati disponibili.", message);
        }

        [Fact]
        public void ValidateDirectoryRootDetailed_DoesNotBlockStart_ForAccessDenied()
        {
            var result = ScanPathAccessValidator.ValidateDirectoryRootDetailed(
                @"\\server\share",
                null,
                _ => FileAttributes.Directory);

            Assert.False(result.IsBlocking);
            Assert.Null(result.Message);

            result = ScanPathAccessValidator.ValidateDirectoryRootDetailed(
                @"\\server\share",
                null,
                path => throw new UnauthorizedAccessException("denied"));

            Assert.False(result.IsBlocking);
            Assert.Equal(@"Accesso negato al percorso: \\server\share", result.Message);
        }
    }
}
