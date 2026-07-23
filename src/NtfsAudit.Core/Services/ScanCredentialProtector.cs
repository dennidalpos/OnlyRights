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
using System.Security.Cryptography;
using System.Text;
using NtfsAudit.Core.Models;

namespace NtfsAudit.Core.Services
{
    public static class ScanCredentialProtector
    {
        private const string ScopeCurrentUser = "CurrentUser";
        private const string ScopeLocalMachine = "LocalMachine";
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("NtfsAudit.ScanCredentials.v1");

        public static ScanCredential ProtectForCurrentUser(ScanCredential credential)
        {
            return Protect(credential, DataProtectionScope.CurrentUser, ScopeCurrentUser);
        }

        public static ScanCredential ProtectForLocalMachine(ScanCredential credential)
        {
            return Protect(credential, DataProtectionScope.LocalMachine, ScopeLocalMachine);
        }

        public static ScanCredential ResolveForRuntime(ScanCredential credential)
        {
            if (credential == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(credential.Password))
            {
                return credential.Clone();
            }

            if (string.IsNullOrWhiteSpace(credential.ProtectedPassword))
            {
                return credential.Clone();
            }

            var scope = string.Equals(credential.ProtectionScope, ScopeLocalMachine, StringComparison.OrdinalIgnoreCase)
                ? DataProtectionScope.LocalMachine
                : DataProtectionScope.CurrentUser;

            return new ScanCredential
            {
                UserName = credential.UserName,
                Password = Unprotect(credential.ProtectedPassword, scope),
                ProtectedPassword = credential.ProtectedPassword,
                ProtectionScope = credential.ProtectionScope
            };
        }

        public static ScanCredential Sanitize(ScanCredential credential)
        {
            if (credential == null)
            {
                return null;
            }

            return new ScanCredential
            {
                UserName = credential.UserName
            };
        }

        private static ScanCredential Protect(ScanCredential credential, DataProtectionScope scope, string scopeLabel)
        {
            if (credential == null || !credential.IsConfigured)
            {
                return null;
            }

            var secret = !string.IsNullOrWhiteSpace(credential.Password)
                ? credential.Password
                : ResolveForRuntime(credential).Password;

            return new ScanCredential
            {
                UserName = credential.UserName,
                ProtectedPassword = Protect(secret, scope),
                ProtectionScope = scopeLabel
            };
        }

        private static string Protect(string value, DataProtectionScope scope)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var bytes = Encoding.UTF8.GetBytes(value);
            var protectedBytes = ProtectedData.Protect(bytes, Entropy, scope);
            return Convert.ToBase64String(protectedBytes);
        }

        private static string Unprotect(string protectedValue, DataProtectionScope scope)
        {
            if (string.IsNullOrWhiteSpace(protectedValue))
            {
                return null;
            }

            var bytes = Convert.FromBase64String(protectedValue);
            var clearBytes = ProtectedData.Unprotect(bytes, Entropy, scope);
            return Encoding.UTF8.GetString(clearBytes);
        }
    }
}
