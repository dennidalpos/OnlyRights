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
            WellKnownSidType.AccountSchemaAdminsSid,
            WellKnownSidType.AccountOperatorsSid,
            WellKnownSidType.ServerOperatorsSid,
            WellKnownSidType.BackupOperatorsSid,
            WellKnownSidType.PrintOperatorsSid
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
            return false;
        }
    }
}
