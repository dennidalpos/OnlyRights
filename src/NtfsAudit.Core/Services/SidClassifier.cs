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
using System.Security.Principal;

namespace NtfsAudit.App.Services
{
    public static class SidClassifier
    {
        private static readonly WellKnownSidType[] PrivilegedGroupSids =
        {
            WellKnownSidType.BuiltinAdministratorsSid,
            WellKnownSidType.AccountAdministratorSid,
            WellKnownSidType.AccountDomainAdminsSid,
            WellKnownSidType.AccountEnterpriseAdminsSid,
            WellKnownSidType.AccountSchemaAdminsSid
        };

        private static readonly string[] PrivilegedGroupSidStrings =
        {
            "S-1-5-32-544", // BUILTIN\\Administrators
            "S-1-5-32-548", // BUILTIN\\Account Operators
            "S-1-5-32-549", // BUILTIN\\Server Operators
            "S-1-5-32-550", // BUILTIN\\Print Operators
            "S-1-5-32-551"  // BUILTIN\\Backup Operators
        };

        public static bool IsServiceAccountSid(string sid)
        {
            if (string.IsNullOrWhiteSpace(sid)) return false;
            if (sid.StartsWith("S-1-5-80-", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            try
            {
                var sidObj = new SecurityIdentifier(sid);
                return sidObj.IsWellKnown(WellKnownSidType.LocalSystemSid)
                    || sidObj.IsWellKnown(WellKnownSidType.LocalServiceSid)
                    || sidObj.IsWellKnown(WellKnownSidType.NetworkServiceSid);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsPrivilegedGroupSid(string sid)
        {
            if (string.IsNullOrWhiteSpace(sid)) return false;
            try
            {
                var sidObj = new SecurityIdentifier(sid);
                foreach (var privilegedSid in PrivilegedGroupSids)
                {
                    if (sidObj.IsWellKnown(privilegedSid))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }
            foreach (var privilegedSid in PrivilegedGroupSidStrings)
            {
                if (string.Equals(sid, privilegedSid, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public static bool IsGroupSid(string sid)
        {
            if (string.IsNullOrWhiteSpace(sid)) return false;

            if (string.Equals(sid, "S-1-1-0", StringComparison.OrdinalIgnoreCase)) return true; // Everyone
            if (string.Equals(sid, "S-1-5-11", StringComparison.OrdinalIgnoreCase)) return true; // Authenticated Users
            if (string.Equals(sid, "S-1-5-32-544", StringComparison.OrdinalIgnoreCase)) return true; // Administrators

            if (sid.StartsWith("S-1-5-32-", StringComparison.OrdinalIgnoreCase)) return true;

            if (string.Equals(sid, "S-1-5-1", StringComparison.OrdinalIgnoreCase)) return true; // Dialup
            if (string.Equals(sid, "S-1-5-2", StringComparison.OrdinalIgnoreCase)) return true; // Network
            if (string.Equals(sid, "S-1-5-3", StringComparison.OrdinalIgnoreCase)) return true; // Batch
            if (string.Equals(sid, "S-1-5-4", StringComparison.OrdinalIgnoreCase)) return true; // Interactive
            if (string.Equals(sid, "S-1-5-6", StringComparison.OrdinalIgnoreCase)) return true; // Service
            if (string.Equals(sid, "S-1-5-7", StringComparison.OrdinalIgnoreCase)) return true; // Anonymous
            if (string.Equals(sid, "S-1-5-9", StringComparison.OrdinalIgnoreCase)) return true; // Enterprise Domain Controllers
            if (string.Equals(sid, "S-1-5-13", StringComparison.OrdinalIgnoreCase)) return true; // Terminal Server User
            if (string.Equals(sid, "S-1-5-14", StringComparison.OrdinalIgnoreCase)) return true; // Remote Interactive Logon
            if (string.Equals(sid, "S-1-5-15", StringComparison.OrdinalIgnoreCase)) return true; // This Organization
            if (string.Equals(sid, "S-1-5-17", StringComparison.OrdinalIgnoreCase)) return true; // IIS Users

            if (sid.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase))
            {
                var lastIndex = sid.LastIndexOf('-');
                if (lastIndex >= 0 && lastIndex < sid.Length - 1)
                {
                    var ridStr = sid.Substring(lastIndex + 1);
                    if (int.TryParse(ridStr, out var rid))
                    {
                        switch (rid)
                        {
                            case 512: // Domain Admins
                            case 513: // Domain Users
                            case 514: // Domain Guests
                            case 515: // Domain Computers
                            case 516: // Domain Controllers
                            case 517: // Cert Publishers
                            case 518: // Schema Admins
                            case 519: // Enterprise Admins
                            case 520: // Group Policy Creator Owners
                            case 553: // RAS and IAS Servers
                                return true;
                        }
                    }
                }
            }

            try
            {
                var sidObj = new SecurityIdentifier(sid);
                foreach (WellKnownSidType type in Enum.GetValues(typeof(WellKnownSidType)))
                {
                    if (IsWellKnownGroupType(type) && sidObj.IsWellKnown(type))
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static bool IsWellKnownGroupType(WellKnownSidType type)
        {
            switch (type)
            {
                case WellKnownSidType.BuiltinAdministratorsSid:
                case WellKnownSidType.BuiltinUsersSid:
                case WellKnownSidType.BuiltinGuestsSid:
                case WellKnownSidType.BuiltinPowerUsersSid:
                case WellKnownSidType.BuiltinAccountOperatorsSid:
                case WellKnownSidType.BuiltinSystemOperatorsSid:
                case WellKnownSidType.BuiltinPrintOperatorsSid:
                case WellKnownSidType.BuiltinBackupOperatorsSid:
                case WellKnownSidType.BuiltinReplicatorSid:
                case WellKnownSidType.BuiltinPreWindows2000CompatibleAccessSid:
                case WellKnownSidType.BuiltinRemoteDesktopUsersSid:
                case WellKnownSidType.BuiltinNetworkConfigurationOperatorsSid:
                case WellKnownSidType.BuiltinIncomingForestTrustBuildersSid:
                case WellKnownSidType.WorldSid:
                case WellKnownSidType.AuthenticatedUserSid:
                case WellKnownSidType.BatchSid:
                case WellKnownSidType.DialupSid:
                case WellKnownSidType.InteractiveSid:
                case WellKnownSidType.NetworkSid:
                case WellKnownSidType.ServiceSid:
                case WellKnownSidType.TerminalServerSid:
                case WellKnownSidType.AccountDomainAdminsSid:
                case WellKnownSidType.AccountDomainUsersSid:
                case WellKnownSidType.AccountDomainGuestsSid:
                case WellKnownSidType.AccountComputersSid:
                case WellKnownSidType.AccountControllersSid:
                case WellKnownSidType.AccountCertAdminsSid:
                case WellKnownSidType.AccountSchemaAdminsSid:
                case WellKnownSidType.AccountEnterpriseAdminsSid:
                case WellKnownSidType.AccountPolicyAdminsSid:
                case WellKnownSidType.AccountRasAndIasServersSid:
                    return true;
                default:
                    return false;
            }
        }
    }
}
