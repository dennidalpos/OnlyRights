using System;
using System.Collections.Generic;
using System.Management;
using System.Security.AccessControl;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public class SharePermissionService
    {
        public SharePermissionContext TryGetSharePermissions(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath)) return null;
            if (!PathResolver.TryGetShareInfo(rootPath, out var server, out var share))
            {
                return null;
            }

            try
            {
                var scope = new ManagementScope(string.Format(@"\\{0}\root\cimv2", server));
                scope.Connect();
                var path = new ManagementPath(string.Format("Win32_LogicalShareSecuritySetting.Name='{0}'", share));
                using (var securitySetting = new ManagementObject(scope, path, null))
                {
                    using (var outParams = securitySetting.InvokeMethod("GetSecurityDescriptor", null, null))
                    {
                        if (outParams == null) return null;
                        var descriptor = outParams["Descriptor"] as ManagementBaseObject;
                        if (descriptor == null) return null;
                        var dacl = descriptor["DACL"] as ManagementBaseObject[];
                        if (dacl == null) return null;
                        var permissions = new List<SharePermission>();
                        foreach (var ace in dacl)
                        {
                            var trustee = ace["Trustee"] as ManagementBaseObject;
                            var sid = trustee == null ? null : trustee["SIDString"] as string;
                            var name = trustee == null ? null : trustee["Name"] as string;
                            var domain = trustee == null ? null : trustee["Domain"] as string;
                            var accessMask = ace["AccessMask"] == null ? 0 : Convert.ToInt32(ace["AccessMask"]);
                            var aceType = ace["AceType"] == null ? 0 : Convert.ToInt32(ace["AceType"]);
                            var accessType = aceType == 1 ? PermissionDecision.Deny : PermissionDecision.Allow;

                            var rightsSummary = RightsNormalizer.Normalize((FileSystemRights)accessMask);
                            permissions.Add(new SharePermission
                            {
                                ShareName = share,
                                ShareServer = server,
                                PrincipalSid = sid ?? string.Empty,
                                PrincipalName = BuildPrincipalName(name, domain, sid),
                                PrincipalType = "Group",
                                AccessType = accessType,
                                RightsMask = accessMask,
                                RightsSummary = rightsSummary,
                                IsInherited = false,
                                AppliesToThisFolder = true,
                                AppliesToSubfolders = true,
                                AppliesToFiles = true
                            });
                        }

                        return new SharePermissionContext(server, share, permissions);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private static string BuildPrincipalName(string name, string domain, string sid)
        {
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(domain))
            {
                return string.Format("{0}\\{1}", domain, name);
            }
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
            return sid ?? string.Empty;
        }
    }

    public class SharePermissionContext
    {
        public SharePermissionContext(string server, string shareName, List<SharePermission> permissions)
        {
            Server = server;
            ShareName = shareName;
            Permissions = permissions ?? new List<SharePermission>();
        }

        public string Server { get; }
        public string ShareName { get; }
        public List<SharePermission> Permissions { get; }
    }
}
