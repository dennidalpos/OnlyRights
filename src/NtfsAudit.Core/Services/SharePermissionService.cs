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
using System.Management;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using NtfsAudit.Core.Models;

namespace NtfsAudit.Core.Services
{
    public class SharePermissionService
    {
        private readonly ScanCredential _credential;
        private readonly Func<string, Tuple<string, string>> _shareInfoResolver;
        private readonly Func<string, string, ConnectionOptions, SharePermissionContext> _permissionLoader;

        public SharePermissionService()
            : this(null)
        {
        }

        public SharePermissionService(ScanCredential credential)
            : this(credential, null, null)
        {
        }

        internal SharePermissionService(
            ScanCredential credential,
            Func<string, Tuple<string, string>> shareInfoResolver,
            Func<string, string, ConnectionOptions, SharePermissionContext> permissionLoader)
        {
            _credential = credential;
            _shareInfoResolver = shareInfoResolver;
            _permissionLoader = permissionLoader ?? LoadSharePermissionsCore;
        }

        public SharePermissionDiagnostic LastDiagnostic { get; private set; }

        public SharePermissionContext TryGetSharePermissions(string rootPath)
        {
            LastDiagnostic = null;
            if (string.IsNullOrWhiteSpace(rootPath)) return null;

            var shareInfo = ResolveShareInfo(rootPath);
            if (shareInfo == null)
            {
                return null;
            }

            var server = shareInfo.Item1;
            var share = shareInfo.Item2;
            try
            {
                return _permissionLoader(server, share, BuildConnectionOptions());
            }
            catch (Exception ex)
            {
                LastDiagnostic = BuildDiagnostic(rootPath, server, share, ex);
                return null;
            }
        }

        private Tuple<string, string> ResolveShareInfo(string rootPath)
        {
            if (_shareInfoResolver != null)
            {
                return _shareInfoResolver(rootPath);
            }

            if (!PathResolver.TryGetShareInfo(rootPath, out var server, out var share))
            {
                return null;
            }

            return Tuple.Create(server, share);
        }

        [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetShareGetInfo(string serverName, string shareName, int level, out IntPtr bufptr);

        [DllImport("Netapi32.dll")]
        private static extern int NetApiBufferFree(IntPtr buffer);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern int GetSecurityDescriptorLength(IntPtr pSecurityDescriptor);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHARE_INFO_502
        {
            [MarshalAs(UnmanagedType.LPWStr)]
            public string shi502_netname;
            public uint shi502_type;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string shi502_remark;
            public int shi502_permissions;
            public int shi502_max_uses;
            public int shi502_current_uses;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string shi502_path;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string shi502_passwd;
            public int shi502_reserved;
            public IntPtr shi502_security_descriptor;
        }

        private static SharePermissionContext LoadSharePermissionsCore(string server, string share, ConnectionOptions options)
        {
            try
            {
                var scope = new ManagementScope(string.Format(@"\\{0}\root\cimv2", server), options);
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
                                PrincipalType = SidClassifier.IsGroupSid(sid) ? "Group" : "User",
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
            catch (Exception wmiEx)
            {
                try
                {
                    return LoadSharePermissionsWin32(server, share);
                }
                catch (Exception win32Ex)
                {
                    throw new AggregateException("WMI connection failed, and Win32 fallback failed.", wmiEx, win32Ex);
                }
            }
        }

        private static SharePermissionContext LoadSharePermissionsWin32(string server, string share)
        {
            IntPtr bufPtr = IntPtr.Zero;
            try
            {
                string formattedServer = server.StartsWith("\\\\") ? server : "\\\\" + server;
                int result = NetShareGetInfo(formattedServer, share, 502, out bufPtr);
                if (result != 0)
                {
                    throw new System.ComponentModel.Win32Exception(result, "NetShareGetInfo failed with error code " + result);
                }

                var shareInfo = Marshal.PtrToStructure<SHARE_INFO_502>(bufPtr);
                if (shareInfo.shi502_security_descriptor == IntPtr.Zero)
                {
                    return new SharePermissionContext(server, share, new List<SharePermission>());
                }

                int sdLength = GetSecurityDescriptorLength(shareInfo.shi502_security_descriptor);
                if (sdLength <= 0)
                {
                    throw new InvalidOperationException("Invalid security descriptor length: " + sdLength);
                }

                byte[] sdBytes = new byte[sdLength];
                Marshal.Copy(shareInfo.shi502_security_descriptor, sdBytes, 0, sdLength);

                var sd = new System.Security.AccessControl.RawSecurityDescriptor(sdBytes, 0);
                var permissions = new List<SharePermission>();

                if (sd.DiscretionaryAcl != null)
                {
                    foreach (System.Security.AccessControl.GenericAce ace in sd.DiscretionaryAcl)
                    {
                        if (ace is System.Security.AccessControl.KnownAce knownAce)
                        {
                            var sid = knownAce.SecurityIdentifier.Value;
                            var principalName = knownAce.SecurityIdentifier.ToString();
                            try
                            {
                                var account = knownAce.SecurityIdentifier.Translate(typeof(System.Security.Principal.NTAccount));
                                principalName = account.Value;
                            }
                            catch
                            {
                            }

                            var accessMask = knownAce.AccessMask;
                            var accessType = knownAce.AceType == System.Security.AccessControl.AceType.AccessDenied
                                ? PermissionDecision.Deny
                                : PermissionDecision.Allow;

                            var rightsSummary = RightsNormalizer.Normalize((FileSystemRights)accessMask);

                            permissions.Add(new SharePermission
                            {
                                ShareName = share,
                                ShareServer = server,
                                PrincipalSid = sid,
                                PrincipalName = principalName,
                                PrincipalType = SidClassifier.IsGroupSid(sid) ? "Group" : "User",
                                AccessType = accessType,
                                RightsMask = accessMask,
                                RightsSummary = rightsSummary,
                                IsInherited = false,
                                AppliesToThisFolder = true,
                                AppliesToSubfolders = true,
                                AppliesToFiles = true
                            });
                        }
                    }
                }

                return new SharePermissionContext(server, share, permissions);
            }
            finally
            {
                if (bufPtr != IntPtr.Zero)
                {
                    NetApiBufferFree(bufPtr);
                }
            }
        }

        private static SharePermissionDiagnostic BuildDiagnostic(string rootPath, string server, string share, Exception ex)
        {
            var errorType = ResolveDiagnosticType(ex);
            return new SharePermissionDiagnostic(
                rootPath,
                server,
                share,
                errorType,
                BuildDiagnosticMessage(errorType, rootPath),
                ex == null ? string.Empty : ex.Message);
        }

        private static string ResolveDiagnosticType(Exception ex)
        {
            if (ex == null) return "SharePermissionsFailure";

            var messages = new List<string>();
            var types = new List<Type>();
            var win32ErrorCodes = new List<int>();

            void ExtractExceptions(Exception e)
            {
                if (e == null) return;
                types.Add(e.GetType());
                messages.Add(e.Message ?? string.Empty);

                if (e is System.ComponentModel.Win32Exception w32ex)
                {
                    win32ErrorCodes.Add(w32ex.NativeErrorCode);
                }

                if (e is AggregateException aggEx)
                {
                    if (aggEx.InnerExceptions != null)
                    {
                        foreach (var inner in aggEx.InnerExceptions)
                        {
                            ExtractExceptions(inner);
                        }
                    }
                }
                else if (e.InnerException != null)
                {
                    ExtractExceptions(e.InnerException);
                }
            }

            ExtractExceptions(ex);

            bool hasAccessDenied = false;
            foreach (var code in win32ErrorCodes)
            {
                if (code == 5 || code == 1326 || code == 1909 || code == 1331 || code == 1332)
                {
                    hasAccessDenied = true;
                    break;
                }
            }

            if (!hasAccessDenied)
            {
                foreach (var msg in messages)
                {
                    if (msg.IndexOf("access denied", StringComparison.OrdinalIgnoreCase) >= 0
                        || msg.IndexOf("accesso negato", StringComparison.OrdinalIgnoreCase) >= 0
                        || msg.IndexOf("logon failure", StringComparison.OrdinalIgnoreCase) >= 0
                        || msg.IndexOf("bad password", StringComparison.OrdinalIgnoreCase) >= 0
                        || msg.IndexOf("username or password", StringComparison.OrdinalIgnoreCase) >= 0
                        || msg.IndexOf("nome utente o password", StringComparison.OrdinalIgnoreCase) >= 0
                        || msg.IndexOf("SeSecurityPrivilege", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hasAccessDenied = true;
                        break;
                    }
                }
            }

            if (hasAccessDenied || types.Contains(typeof(PrivilegeNotHeldException)) || types.Contains(typeof(UnauthorizedAccessException)))
            {
                return "SharePermissionsAccessDenied";
            }

            bool hasUnsupported = false;
            foreach (var msg in messages)
            {
                if (msg.IndexOf("invalid class", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("not supported", StringComparison.OrdinalIgnoreCase) >= 0
                    || msg.IndexOf("non support", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    hasUnsupported = true;
                    break;
                }
            }

            if (hasUnsupported)
            {
                return "SharePermissionsUnsupported";
            }

            bool hasUnavailable = false;
            foreach (var code in win32ErrorCodes)
            {
                if (code == 1722 || code == 53 || code == 67 || code == 1203 || code == 1222)
                {
                    hasUnavailable = true;
                    break;
                }
            }

            if (!hasUnavailable)
            {
                foreach (var msg in messages)
                {
                    if (msg.IndexOf("rpc server is unavailable", StringComparison.OrdinalIgnoreCase) >= 0
                        || msg.IndexOf("server rpc non disponibile", StringComparison.OrdinalIgnoreCase) >= 0
                        || msg.IndexOf("network path was not found", StringComparison.OrdinalIgnoreCase) >= 0
                        || msg.IndexOf("network name cannot be found", StringComparison.OrdinalIgnoreCase) >= 0
                        || msg.IndexOf("percorso di rete", StringComparison.OrdinalIgnoreCase) >= 0
                        || msg.IndexOf("non trovato", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hasUnavailable = true;
                        break;
                    }
                }
            }

            if (hasUnavailable || types.Contains(typeof(IOException)))
            {
                return "SharePermissionsUnavailable";
            }

            return "SharePermissionsFailure";
        }

        private static string BuildDiagnosticMessage(string errorType, string rootPath)
        {
            switch (errorType)
            {
                case "SharePermissionsAccessDenied":
                    return string.Format("SMB share permissions were not read for {0}: access denied or credentials rejected.", rootPath);
                case "SharePermissionsUnsupported":
                    return string.Format("SMB share permissions were not read for {0}: share or WMI/SMB provider is not supported.", rootPath);
                case "SharePermissionsUnavailable":
                    return string.Format("SMB share permissions were not read for {0}: host/share is unreachable or WMI is unavailable.", rootPath);
                default:
                    return string.Format("SMB share permissions were not read for {0}: unexpected error.", rootPath);
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

        private ConnectionOptions BuildConnectionOptions()
        {
            var options = new ConnectionOptions
            {
                EnablePrivileges = true
            };

            if (_credential == null || !_credential.IsConfigured)
            {
                return options;
            }

            options.Username = _credential.UserName;
            options.Password = _credential.Password;
            return options;
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

    public sealed class SharePermissionDiagnostic
    {
        public SharePermissionDiagnostic(string path, string server, string shareName, string errorType, string message, string technicalDetails)
        {
            Path = path;
            Server = server;
            ShareName = shareName;
            ErrorType = errorType;
            Message = message;
            TechnicalDetails = technicalDetails;
        }

        public string Path { get; }
        public string Server { get; }
        public string ShareName { get; }
        public string ErrorType { get; }
        public string Message { get; }
        public string TechnicalDetails { get; }
    }
}
