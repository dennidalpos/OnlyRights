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
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public partial class ScanService
    {
        private FileSystemSecurity GetAccessControlWithFallback(
            Func<AccessControlSections, FileSystemSecurity> accessControlFetcher,
            AccessControlSections accessSections,
            string path,
            BlockingCollection<ErrorEntry> errorQueue,
            Action incrementError,
            out string auditFailureReason)
        {
            auditFailureReason = null;
            try
            {
                return accessControlFetcher(accessSections);
            }
            catch (PrivilegeNotHeldException ex)
            {
                if (accessSections.HasFlag(AccessControlSections.Audit))
                {
                    auditFailureReason = "SACL unavailable (privileges)";
                }
                if (incrementError != null)
                {
                    incrementError();
                }
                if (errorQueue != null)
                {
                    errorQueue.Add(BuildErrorEntry(path, ex));
                }
                try
                {
                    if (accessSections == AccessControlSections.Access)
                    {
                        return null;
                    }
                    return accessControlFetcher(AccessControlSections.Access);
                }
                catch (Exception accessEx)
                {
                    if (incrementError != null)
                    {
                        incrementError();
                    }
                    if (errorQueue != null)
                    {
                        errorQueue.Add(BuildErrorEntry(path, accessEx));
                    }
                    return null;
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                if (accessSections.HasFlag(AccessControlSections.Audit))
                {
                    auditFailureReason = "SACL unavailable (access denied)";
                }
                if (incrementError != null)
                {
                    incrementError();
                }
                if (errorQueue != null)
                {
                    errorQueue.Add(BuildErrorEntry(path, ex));
                }
                try
                {
                    if (accessSections == AccessControlSections.Access)
                    {
                        return null;
                    }
                    return accessControlFetcher(AccessControlSections.Access);
                }
                catch (Exception accessEx)
                {
                    if (incrementError != null)
                    {
                        incrementError();
                    }
                    if (errorQueue != null)
                    {
                        errorQueue.Add(BuildErrorEntry(path, accessEx));
                    }
                    return null;
                }
            }
        }

        private string ResolveOwner(FileSystemSecurity security, ScanOptions options)
        {
            try
            {
                var sid = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
                if (sid == null) return string.Empty;
                if (!options.ResolveIdentities) return sid.Value;
                var resolved = _identityResolver.Resolve(sid.Value);
                return resolved == null ? sid.Value : resolved.Name;
            }
            catch
            {
                return string.Empty;
            }
        }

        private string BuildAuditSummary(FileSystemSecurity security, ScanOptions options)
        {
            try
            {
                var rules = security.GetAuditRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAuditRule>().ToList();
                if (rules.Count == 0) return string.Empty;
                var summaries = new System.Collections.Generic.List<string>();
                foreach (var rule in rules)
                {
                    var sid = rule.IdentityReference.Value;
                    var principal = sid;
                    if (options.ResolveIdentities)
                    {
                        try
                        {
                            var resolved = _identityResolver.Resolve(sid);
                            if (resolved != null && !string.IsNullOrWhiteSpace(resolved.Name))
                            {
                                principal = resolved.Name;
                            }
                        }
                        catch
                        {
                            principal = sid;
                        }
                    }
                    var rights = RightsNormalizer.Normalize(rule.FileSystemRights);
                    summaries.Add(string.Format("{0}:{1}:{2}", principal, rule.AuditFlags, rights));
                }
                return string.Join(" | ", summaries);
            }
            catch (PrivilegeNotHeldException)
            {
                return "SACL unavailable (privileges)";
            }
            catch (UnauthorizedAccessException)
            {
                return "SACL unavailable (access denied)";
            }
            catch (InvalidOperationException)
            {
                return "SACL unavailable (audit section missing)";
            }
            catch
            {
                return string.Empty;
            }
        }

        private string ResolveAuditSummary(FileSystemSecurity security, ScanOptions options, string auditFailureReason)
        {
            if (!string.IsNullOrWhiteSpace(auditFailureReason))
            {
                return auditFailureReason;
            }

            var summary = BuildAuditSummary(security, options);
            return string.IsNullOrWhiteSpace(summary) ? "No SACL entries" : summary;
        }

        private static string EvaluateRisk(AceEntry entry)
        {
            if (entry == null) return "Low";
            var allow = string.Equals(entry.AllowDeny, "Allow", StringComparison.OrdinalIgnoreCase);
            var principal = entry.PrincipalSid ?? entry.PrincipalName ?? string.Empty;
            var rightsSummary = string.IsNullOrWhiteSpace(entry.EffectiveRightsSummary)
                ? entry.RightsSummary ?? string.Empty
                : entry.EffectiveRightsSummary;
            var rank = RightsNormalizer.Rank(rightsSummary);
            var isBroadPrincipal = IsEveryone(principal) || IsAuthenticatedUsers(principal)
                || principal.IndexOf("Everyone", StringComparison.OrdinalIgnoreCase) >= 0
                || principal.IndexOf("Authenticated Users", StringComparison.OrdinalIgnoreCase) >= 0;

            if (allow && isBroadPrincipal && rank >= 4)
            {
                return "High";
            }
            if (allow && (isBroadPrincipal && rank >= 2))
            {
                return "Medium";
            }
            if (!allow)
            {
                return "Medium";
            }
            if (entry.IsInheritanceDisabled)
            {
                return "Medium";
            }
            return "Low";
        }

        private static bool IsEveryone(string sidOrName)
        {
            return string.Equals(sidOrName, EveryoneSid, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAuthenticatedUsers(string sidOrName)
        {
            return string.Equals(sidOrName, AuthenticatedUsersSid, StringComparison.OrdinalIgnoreCase);
        }

        private static void UpdateFolderDetailFlags(FolderDetail detail)
        {
            if (detail == null)
            {
                return;
            }

            detail.HasShareEntries = detail.ShareEntries.Count > 0;
            detail.HasEffectiveEntries = detail.EffectiveEntries.Count > 0;
            detail.HasFileEntries = detail.AllEntries.Any(entry => string.Equals(entry.ResourceType, "File", StringComparison.OrdinalIgnoreCase));
            detail.HasFolderEntries = detail.AllEntries.Any(entry => !string.Equals(entry.ResourceType, "File", StringComparison.OrdinalIgnoreCase));
            detail.HasHighRiskEntries = detail.AllEntries.Any(entry =>
                string.Equals(entry.RiskLevel, "High", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.RiskLevel, "Alto", StringComparison.OrdinalIgnoreCase));
            detail.HasMediumRiskEntries = detail.AllEntries.Any(entry =>
                string.Equals(entry.RiskLevel, "Medium", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.RiskLevel, "Medio", StringComparison.OrdinalIgnoreCase));
            detail.HasLowRiskEntries = detail.AllEntries.Any(entry =>
                string.Equals(entry.RiskLevel, "Low", StringComparison.OrdinalIgnoreCase)
                || string.Equals(entry.RiskLevel, "Basso", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsDfsCachePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var normalized = path.Replace('/', '\\');
            var trimmed = normalized.TrimEnd('\\');
            var folderName = GetLastPathSegment(trimmed);
            if (string.IsNullOrWhiteSpace(folderName)) return false;

            if (string.Equals(folderName, "System Volume Information", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(folderName, "DfsrPrivate", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(folderName, "DfsPrivate", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(folderName, "ConflictAndDeleted", StringComparison.OrdinalIgnoreCase)
                || string.Equals(folderName, "Deleted", StringComparison.OrdinalIgnoreCase)
                || string.Equals(folderName, "PreExisting", StringComparison.OrdinalIgnoreCase)
                || string.Equals(folderName, "Staging", StringComparison.OrdinalIgnoreCase)
                || string.Equals(folderName, "Staging Areas", StringComparison.OrdinalIgnoreCase))
            {
                var parentName = GetParentPathSegment(trimmed);
                if (string.Equals(parentName, "DfsrPrivate", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(parentName, "DFSR", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (string.Equals(folderName, "DFSR", StringComparison.OrdinalIgnoreCase))
            {
                var parentName = GetParentPathSegment(trimmed);
                return string.Equals(parentName, "System Volume Information", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        private static string GetParentPathSegment(string normalizedPath)
        {
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return string.Empty;
            }

            var lastSeparator = normalizedPath.LastIndexOf('\\');
            if (lastSeparator <= 0)
            {
                return string.Empty;
            }

            var parentPath = normalizedPath.Substring(0, lastSeparator).TrimEnd('\\');
            return GetLastPathSegment(parentPath) ?? string.Empty;
        }

        private static string GetLastPathSegment(string normalizedPath)
        {
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return string.Empty;
            }

            var index = normalizedPath.LastIndexOf('\\');
            return index >= 0 ? normalizedPath.Substring(index + 1) : normalizedPath;
        }

        private static bool TryEnableSecurityPrivilege()
        {
            try
            {
                IntPtr tokenHandle;
                if (!OpenProcessToken(Process.GetCurrentProcess().Handle, TokenAdjustPrivileges | TokenQuery, out tokenHandle))
                {
                    return false;
                }

                try
                {
                    Luid luid;
                    if (!LookupPrivilegeValue(null, "SeSecurityPrivilege", out luid))
                    {
                        return false;
                    }

                    var tokenPrivileges = new TokenPrivileges
                    {
                        PrivilegeCount = 1,
                        Privileges = new LuidAndAttributes
                        {
                            Luid = luid,
                            Attributes = SePrivilegeEnabled
                        }
                    };

                    AdjustTokenPrivileges(tokenHandle, false, ref tokenPrivileges, 0, IntPtr.Zero, IntPtr.Zero);
                    return Marshal.GetLastWin32Error() == 0;
                }
                finally
                {
                    CloseHandle(tokenHandle);
                }
            }
            catch
            {
                return false;
            }
        }

        private const int TokenAdjustPrivileges = 0x20;
        private const int TokenQuery = 0x8;
        private const int SePrivilegeEnabled = 0x2;

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool OpenProcessToken(IntPtr processHandle, int desiredAccess, out IntPtr tokenHandle);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool LookupPrivilegeValue(string systemName, string name, out Luid luid);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool AdjustTokenPrivileges(
            IntPtr tokenHandle,
            bool disableAllPrivileges,
            ref TokenPrivileges newState,
            int bufferLength,
            IntPtr previousState,
            IntPtr returnLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [StructLayout(LayoutKind.Sequential)]
        private struct Luid
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LuidAndAttributes
        {
            public Luid Luid;
            public int Attributes;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TokenPrivileges
        {
            public int PrivilegeCount;
            public LuidAndAttributes Privileges;
        }

        private class WorkItem
        {
            public WorkItem(string path, int depth)
            {
                Path = path;
                Depth = depth;
            }

            public string Path { get; private set; }
            public int Depth { get; private set; }
        }
    }
}
