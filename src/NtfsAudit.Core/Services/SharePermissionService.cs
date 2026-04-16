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
using System.Security.AccessControl;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
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

        private static SharePermissionContext LoadSharePermissionsCore(string server, string share, ConnectionOptions options)
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
            var message = ex == null ? string.Empty : ex.Message ?? string.Empty;
            if (ex is PrivilegeNotHeldException
                || ex is UnauthorizedAccessException
                || message.IndexOf("access denied", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("accesso negato", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("logon failure", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("bad password", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("SeSecurityPrivilege", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "SharePermissionsAccessDenied";
            }

            if (message.IndexOf("invalid class", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("not supported", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("non support", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "SharePermissionsUnsupported";
            }

            if (ex is IOException
                || message.IndexOf("rpc server is unavailable", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("server rpc non disponibile", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("network path was not found", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("network name cannot be found", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("percorso di rete", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("non trovato", StringComparison.OrdinalIgnoreCase) >= 0)
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
                    return string.Format("Permessi share SMB non letti per {0}: accesso negato o credenziali rifiutate.", rootPath);
                case "SharePermissionsUnsupported":
                    return string.Format("Permessi share SMB non letti per {0}: share o provider WMI/SMB non compatibile.", rootPath);
                case "SharePermissionsUnavailable":
                    return string.Format("Permessi share SMB non letti per {0}: host/share non raggiungibile o WMI non disponibile.", rootPath);
                default:
                    return string.Format("Permessi share SMB non letti per {0}: errore imprevisto.", rootPath);
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
