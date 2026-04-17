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
using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    internal static class WindowsImpersonationHelper
    {
        private const int Logon32LogonInteractive = 2;
        private const int Logon32LogonNewCredentials = 9;
        private const int Logon32ProviderDefault = 0;
        private const int Logon32ProviderWinNt50 = 3;

        internal static T Run<T>(ScanCredential credential, Func<T> action)
        {
            if (action == null)
            {
                throw new ArgumentNullException("action");
            }

            if (credential == null || !credential.IsConfigured)
            {
                return action();
            }

            using (var token = CreateToken(credential))
            {
                return System.Security.Principal.WindowsIdentity.RunImpersonated(token, action);
            }
        }

        private static SafeAccessTokenHandle CreateToken(ScanCredential credential)
        {
            var networkCredential = BuildNetworkCredential(credential);
            SafeAccessTokenHandle token;
            if (LogonUser(
                networkCredential.UserName,
                string.IsNullOrWhiteSpace(networkCredential.Domain) ? null : networkCredential.Domain,
                networkCredential.Password,
                Logon32LogonNewCredentials,
                Logon32ProviderWinNt50,
                out token))
            {
                return token;
            }

            var firstError = Marshal.GetLastWin32Error();
            if (LogonUser(
                networkCredential.UserName,
                string.IsNullOrWhiteSpace(networkCredential.Domain) ? null : networkCredential.Domain,
                networkCredential.Password,
                Logon32LogonInteractive,
                Logon32ProviderDefault,
                out token))
            {
                return token;
            }

            throw new Win32Exception(firstError, "Unable to impersonate with the configured credentials.");
        }

        private static NetworkCredential BuildNetworkCredential(ScanCredential credential)
        {
            var userName = credential == null ? null : credential.UserName;
            var password = credential == null ? null : credential.Password;
            if (string.IsNullOrWhiteSpace(userName))
            {
                return new NetworkCredential(string.Empty, password ?? string.Empty);
            }

            var separatorIndex = userName.IndexOf('\\');
            if (separatorIndex > 0 && separatorIndex < userName.Length - 1)
            {
                return new NetworkCredential(
                    userName.Substring(separatorIndex + 1),
                    password ?? string.Empty,
                    userName.Substring(0, separatorIndex));
            }

            return new NetworkCredential(userName, password ?? string.Empty);
        }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LogonUser(
            string lpszUsername,
            string lpszDomain,
            string lpszPassword,
            int dwLogonType,
            int dwLogonProvider,
            out SafeAccessTokenHandle phToken);
    }
}
