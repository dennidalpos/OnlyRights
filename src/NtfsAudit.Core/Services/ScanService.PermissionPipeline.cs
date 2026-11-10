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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using NtfsAudit.Core.Export;
using NtfsAudit.Core.Models;

namespace NtfsAudit.Core.Services
{
    public partial class ScanService
    {
        private void ProcessAccessControl(
            FileSystemSecurity security,
            string folderKey,
            string targetPath,
            bool isFile,
            int depth,
            ScanOptions options,
            FolderDetail currentDetail,
            List<AclDiffKey> baselineKeys,
            SharePermissionContext shareContext,
            Dictionary<string, PermissionCalculator.AccessAccumulator> shareAccessMap,
            BlockingCollection<ExportRecord> dataQueue,
            BlockingCollection<ErrorEntry> errorQueue,
            CancellationToken token,
            string auditFailureReason,
            PathKind rootPathKind)
        {
            if (security == null) return;
            var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
            var isInheritanceDisabled = security.AreAccessRulesProtected;
            var hasExplicitPermissions = false;

            var anonFolderKey = options.AnonymizeIdentities ? AnonymizePath(folderKey) : folderKey;
            var anonTargetPath = options.AnonymizeIdentities ? AnonymizePath(targetPath) : targetPath;

            var ntfsPermissions = BuildNtfsPermissions(rules, options, anonFolderKey, anonTargetPath);
            var effectiveAccess = options.ComputeEffectiveAccess
                ? PermissionCalculator.BuildAccessMap(ntfsPermissions, options.IncludeInherited)
                : new Dictionary<string, PermissionCalculator.AccessAccumulator>(StringComparer.OrdinalIgnoreCase);
            var owner = options.ReadOwnerAndSacl ? ResolveOwner(security, options) : string.Empty;
            var auditSummary = options.ReadOwnerAndSacl ? ResolveAuditSummary(security, options, auditFailureReason) : string.Empty;

            foreach (var rule in rules)
            {
                token.ThrowIfCancellationRequested();
                if (!rule.IsInherited)
                {
                    hasExplicitPermissions = true;
                }
                if (!options.IncludeInherited && rule.IsInherited)
                {
                    continue;
                }
                var sid = rule.IdentityReference.Value;
                ResolvedPrincipal resolved;
                if (options.ResolveIdentities || options.AnonymizeIdentities)
                {
                    resolved = _identityResolver.Resolve(sid);
                }
                else
                {
                    resolved = new ResolvedPrincipal
                    {
                        Sid = sid,
                        Name = sid,
                        IsGroup = SidClassifier.IsGroupSid(sid),
                        IsDisabled = false,
                        IsServiceAccount = false,
                        IsAdminAccount = false
                    };
                }
                if (options.ResolveIdentities && options.ExcludeAdminAccounts && !resolved.IsGroup && !resolved.IsAdminAccount)
                {
                    if (_groupExpansion != null && _groupExpansion.IsPrivilegedUser(sid))
                    {
                        resolved.IsAdminAccount = true;
                    }
                }
                if (options.ResolveIdentities && options.ExcludeServiceAccounts && resolved.IsServiceAccount)
                {
                    continue;
                }
                if (options.ResolveIdentities && options.ExcludeAdminAccounts && resolved.IsAdminAccount)
                {
                    continue;
                }
                var rightsSummary = RightsNormalizer.Normalize(rule.FileSystemRights);
                var ntfsMask = options.ComputeEffectiveAccess
                    ? PermissionCalculator.GetEffectiveMask(effectiveAccess, sid)
                    : (int)rule.FileSystemRights;
                var shareMask = shareAccessMap != null && shareAccessMap.Count > 0
                    ? PermissionCalculator.GetEffectiveMask(shareAccessMap, sid)
                    : PermissionCalculator.FullControlMask;
                var effectiveMask = options.ComputeEffectiveAccess
                    ? PermissionCalculator.IntersectMasks(ntfsMask, shareMask, shareAccessMap != null && shareAccessMap.Count > 0)
                    : (int)rule.FileSystemRights;
                if (options.ComputeEffectiveAccess && effectiveMask == 0 && rule.AccessControlType == AccessControlType.Allow)
                {
                    effectiveMask = (int)rule.FileSystemRights;
                }
                var effectiveSummary = options.ComputeEffectiveAccess
                    ? RightsNormalizer.Normalize((FileSystemRights)effectiveMask)
                    : rightsSummary;
                var scope = PermissionCalculator.ResolveScope(rule.InheritanceFlags, rule.PropagationFlags);
                var entry = new AceEntry
                {
                    FolderPath = anonFolderKey,
                    TargetPath = anonTargetPath,
                    ResourceType = isFile ? "File" : "Folder",
                    Owner = owner,
                    AuditSummary = auditSummary,
                    PrincipalName = resolved.Name,
                    PrincipalSid = resolved.Sid,
                    PrincipalType = resolved.Type,
                    PermissionLayer = PermissionLayer.Ntfs,
                    AllowDeny = rule.AccessControlType.ToString(),
                    RightsSummary = rightsSummary,
                    RightsMask = (int)rule.FileSystemRights,
                    EffectiveRightsSummary = effectiveSummary,
                    EffectiveRightsMask = effectiveMask,
                    ShareRightsMask = shareMask,
                    NtfsRightsMask = ntfsMask,
                    IsInherited = rule.IsInherited,
                    AppliesToThisFolder = scope.AppliesToThisFolder,
                    AppliesToSubfolders = scope.AppliesToSubfolders,
                    AppliesToFiles = scope.AppliesToFiles,
                    InheritanceFlags = rule.InheritanceFlags.ToString(),
                    PropagationFlags = rule.PropagationFlags.ToString(),
                    Source = isFile ? "File" : "Direct",
                    Depth = depth,
                    IsDisabled = resolved.IsDisabled,
                    IsServiceAccount = resolved.IsServiceAccount,
                    IsAdminAccount = resolved.IsAdminAccount,
                    HasExplicitPermissions = !rule.IsInherited,
                    IsInheritanceDisabled = isInheritanceDisabled
                };

                entry.RiskLevel = EvaluateRisk(entry);

                lock (currentDetail)
                {
                    currentDetail.AllEntries.Add(entry);
                }

                List<ResolvedPrincipal> members = null;
                if (resolved.IsGroup && options.ExpandGroups && (options.ResolveIdentities || options.AnonymizeIdentities))
                {
                    members = _groupExpansion.ExpandGroup(sid, token);
                    if (options.AnonymizeIdentities && members != null)
                    {
                        members = members.Select(m => _identityResolver.Resolve(m.Sid ?? m.Name)).ToList();
                    }
                    entry.MemberNames = members?.Select(m =>
                        string.IsNullOrWhiteSpace(m.Sid)
                            ? m.Name
                            : string.Format("{0} ({1})", m.Name, m.Sid)).ToList();
                }

                EnqueueDataRecord(dataQueue, BuildExportRecord(entry, options), token);

                if (members != null)
                {
                    foreach (var member in members)
                    {
                        var source = string.Format("Group:{0}", resolved.Name);
                        if (options.ResolveIdentities && options.ExcludeServiceAccounts && member.IsServiceAccount)
                        {
                            continue;
                        }
                        if (options.ResolveIdentities && options.ExcludeAdminAccounts && member.IsAdminAccount)
                        {
                            continue;
                        }
                        var memberEntry = new AceEntry
                        {
                            FolderPath = anonFolderKey,
                            TargetPath = anonTargetPath,
                            ResourceType = isFile ? "File" : "Folder",
                            Owner = owner,
                            AuditSummary = auditSummary,
                            PrincipalName = member.Name,
                            PrincipalSid = member.Sid,
                            PrincipalType = member.IsGroup ? "Group" : "User",
                            PermissionLayer = PermissionLayer.Ntfs,
                            AllowDeny = rule.AccessControlType.ToString(),
                            RightsSummary = rightsSummary,
                            RightsMask = (int)rule.FileSystemRights,
                            EffectiveRightsSummary = effectiveSummary,
                            EffectiveRightsMask = effectiveMask,
                            ShareRightsMask = shareMask,
                            NtfsRightsMask = ntfsMask,
                            IsInherited = rule.IsInherited,
                            AppliesToThisFolder = scope.AppliesToThisFolder,
                            AppliesToSubfolders = scope.AppliesToSubfolders,
                            AppliesToFiles = scope.AppliesToFiles,
                            InheritanceFlags = rule.InheritanceFlags.ToString(),
                            PropagationFlags = rule.PropagationFlags.ToString(),
                            Source = source,
                            Depth = depth,
                            IsDisabled = member.IsDisabled,
                            IsServiceAccount = member.IsServiceAccount,
                            IsAdminAccount = member.IsAdminAccount,
                            HasExplicitPermissions = !rule.IsInherited,
                            IsInheritanceDisabled = isInheritanceDisabled
                        };
                        memberEntry.RiskLevel = EvaluateRisk(memberEntry);
                        lock (currentDetail)
                        {
                            currentDetail.AllEntries.Add(memberEntry);
                        }
                        EnqueueDataRecord(dataQueue, BuildExportRecord(memberEntry, options), token);
                    }
                }
            }

            lock (currentDetail)
            {
                currentDetail.HasExplicitPermissions = currentDetail.HasExplicitPermissions || hasExplicitPermissions;
                currentDetail.HasExplicitNtfs = currentDetail.HasExplicitNtfs || hasExplicitPermissions;
                currentDetail.IsInheritanceDisabled = currentDetail.IsInheritanceDisabled || isInheritanceDisabled;
                if (baselineKeys != null)
                {
                    var currentKeys = BuildAclKeysFromRules(rules, options);
                    currentDetail.BaselineSummary = AclBaselineComparer.BuildBaselineDiff(baselineKeys, currentKeys);
                }
            }

            AddShareAndEffectiveEntries(
                currentDetail,
                shareContext,
                shareAccessMap,
                effectiveAccess,
                options,
                anonFolderKey,
                anonTargetPath,
                isFile,
                depth,
                dataQueue,
                token,
                owner,
                auditSummary,
                isInheritanceDisabled,
                rootPathKind);
        }

        private List<NtfsPermission> BuildNtfsPermissions(IEnumerable<FileSystemAccessRule> rules, ScanOptions options, string folderKey, string targetPath)
        {
            var permissions = new List<NtfsPermission>();
            foreach (var rule in rules)
            {
                if (!options.IncludeInherited && rule.IsInherited)
                {
                    continue;
                }
                var scope = PermissionCalculator.ResolveScope(rule.InheritanceFlags, rule.PropagationFlags);
                permissions.Add(new NtfsPermission
                {
                    FolderPath = folderKey,
                    TargetPath = targetPath,
                    PrincipalSid = rule.IdentityReference.Value,
                    AccessType = rule.AccessControlType == AccessControlType.Allow ? PermissionDecision.Allow : PermissionDecision.Deny,
                    RightsMask = (int)rule.FileSystemRights,
                    RightsSummary = RightsNormalizer.Normalize(rule.FileSystemRights),
                    IsInherited = rule.IsInherited,
                    AppliesToThisFolder = scope.AppliesToThisFolder,
                    AppliesToSubfolders = scope.AppliesToSubfolders,
                    AppliesToFiles = scope.AppliesToFiles,
                    InheritanceFlags = rule.InheritanceFlags.ToString(),
                    PropagationFlags = rule.PropagationFlags.ToString()
                });
            }
            return permissions;
        }

        private void AddShareAndEffectiveEntries(
            FolderDetail currentDetail,
            SharePermissionContext shareContext,
            Dictionary<string, PermissionCalculator.AccessAccumulator> shareAccessMap,
            Dictionary<string, PermissionCalculator.AccessAccumulator> ntfsAccessMap,
            ScanOptions options,
            string folderKey,
            string targetPath,
            bool isFile,
            int depth,
            BlockingCollection<ExportRecord> dataQueue,
            CancellationToken token,
            string owner,
            string auditSummary,
            bool isInheritanceDisabled,
            PathKind rootPathKind)
        {
            var shareEntries = shareContext == null || shareContext.Permissions == null || shareContext.Permissions.Count == 0
                ? new List<AceEntry>()
                : BuildShareEntries(shareContext, options, folderKey, targetPath, isFile, depth, owner, auditSummary, isInheritanceDisabled, rootPathKind);
            var effectiveEntries = BuildEffectiveEntries(shareContext, shareAccessMap, ntfsAccessMap, options, folderKey, targetPath, isFile, depth, owner, auditSummary, isInheritanceDisabled, rootPathKind);

            lock (currentDetail)
            {
                foreach (var entry in shareEntries)
                {
                    currentDetail.ShareEntries.Add(entry);
                }
                foreach (var entry in effectiveEntries)
                {
                    currentDetail.EffectiveEntries.Add(entry);
                }

                currentDetail.HasExplicitShare = currentDetail.HasExplicitShare || shareEntries.Count > 0;
            }

            foreach (var entry in shareEntries)
            {
                EnqueueDataRecord(dataQueue, BuildExportRecord(entry, options), token);
            }
            foreach (var entry in effectiveEntries)
            {
                EnqueueDataRecord(dataQueue, BuildExportRecord(entry, options), token);
            }
        }

        private List<AceEntry> BuildShareEntries(
            SharePermissionContext shareContext,
            ScanOptions options,
            string folderKey,
            string targetPath,
            bool isFile,
            int depth,
            string owner,
            string auditSummary,
            bool isInheritanceDisabled,
            PathKind rootPathKind)
        {
            var entries = new List<AceEntry>();
            foreach (var permission in shareContext.Permissions)
            {
                if (!options.IncludeInherited && permission.IsInherited)
                {
                    continue;
                }
                var resolved = ResolvePrincipal(permission.PrincipalSid, permission.PrincipalName, options);
                if (resolved == null) continue;
                if (options.ResolveIdentities && options.ExcludeServiceAccounts && resolved.IsServiceAccount)
                {
                    continue;
                }
                if (options.ResolveIdentities && options.ExcludeAdminAccounts && resolved.IsAdminAccount)
                {
                    continue;
                }

                entries.Add(new AceEntry
                {
                    FolderPath = folderKey,
                    TargetPath = targetPath,
                    ResourceType = isFile ? "File" : "Folder",
                    Owner = owner,
                    AuditSummary = auditSummary,
                    PrincipalName = resolved.Name,
                    PrincipalSid = resolved.Sid,
                    PrincipalType = resolved.Type,
                    PermissionLayer = PermissionLayer.Share,
                    AllowDeny = permission.AccessType.ToString(),
                    RightsSummary = permission.RightsSummary,
                    RightsMask = permission.RightsMask,
                    EffectiveRightsSummary = permission.RightsSummary,
                    EffectiveRightsMask = permission.RightsMask,
                    ShareRightsMask = permission.RightsMask,
                    NtfsRightsMask = 0,
                    IsInherited = permission.IsInherited,
                    AppliesToThisFolder = permission.AppliesToThisFolder,
                    AppliesToSubfolders = permission.AppliesToSubfolders,
                    AppliesToFiles = permission.AppliesToFiles,
                    InheritanceFlags = string.Empty,
                    PropagationFlags = string.Empty,
                    Source = "Share",
                    PathKind = rootPathKind,
                    Depth = depth,
                    IsDisabled = resolved.IsDisabled,
                    IsServiceAccount = resolved.IsServiceAccount,
                    IsAdminAccount = resolved.IsAdminAccount,
                    HasExplicitPermissions = true,
                    IsInheritanceDisabled = isInheritanceDisabled,
                    ShareName = shareContext.ShareName,
                    ShareServer = shareContext.Server
                });
            }
            return entries;
        }

        private List<AceEntry> BuildEffectiveEntries(
            SharePermissionContext shareContext,
            Dictionary<string, PermissionCalculator.AccessAccumulator> shareAccessMap,
            Dictionary<string, PermissionCalculator.AccessAccumulator> ntfsAccessMap,
            ScanOptions options,
            string folderKey,
            string targetPath,
            bool isFile,
            int depth,
            string owner,
            string auditSummary,
            bool isInheritanceDisabled,
            PathKind rootPathKind)
        {
            var entries = new List<AceEntry>();
            if (!options.ComputeEffectiveAccess) return entries;

            var sids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (shareAccessMap != null)
            {
                foreach (var sid in shareAccessMap.Keys)
                {
                    sids.Add(sid);
                }
            }
            if (ntfsAccessMap != null)
            {
                foreach (var sid in ntfsAccessMap.Keys)
                {
                    sids.Add(sid);
                }
            }

            foreach (var sid in sids)
            {
                var ntfsMask = PermissionCalculator.GetEffectiveMask(ntfsAccessMap, sid);
                var shareMask = PermissionCalculator.GetEffectiveMask(shareAccessMap, sid);
                var effectiveMask = PermissionCalculator.IntersectMasks(ntfsMask, shareMask, shareAccessMap != null && shareAccessMap.Count > 0);
                if (effectiveMask == 0) continue;

                var resolved = ResolvePrincipal(sid, sid, options);
                if (resolved == null) continue;
                if (options.ResolveIdentities && options.ExcludeServiceAccounts && resolved.IsServiceAccount)
                {
                    continue;
                }
                if (options.ResolveIdentities && options.ExcludeAdminAccounts && resolved.IsAdminAccount)
                {
                    continue;
                }

                entries.Add(new AceEntry
                {
                    FolderPath = folderKey,
                    TargetPath = targetPath,
                    ResourceType = isFile ? "File" : "Folder",
                    Owner = owner,
                    AuditSummary = auditSummary,
                    PrincipalName = resolved.Name,
                    PrincipalSid = resolved.Sid,
                    PrincipalType = resolved.Type,
                    PermissionLayer = PermissionLayer.Effective,
                    AllowDeny = PermissionDecision.Allow.ToString(),
                    RightsSummary = RightsNormalizer.Normalize((FileSystemRights)effectiveMask),
                    RightsMask = effectiveMask,
                    EffectiveRightsSummary = RightsNormalizer.Normalize((FileSystemRights)effectiveMask),
                    EffectiveRightsMask = effectiveMask,
                    ShareRightsMask = shareMask,
                    NtfsRightsMask = ntfsMask,
                    IsInherited = false,
                    AppliesToThisFolder = true,
                    AppliesToSubfolders = true,
                    AppliesToFiles = true,
                    InheritanceFlags = string.Empty,
                    PropagationFlags = string.Empty,
                    Source = "Effective",
                    PathKind = rootPathKind,
                    Depth = depth,
                    IsDisabled = resolved.IsDisabled,
                    IsServiceAccount = resolved.IsServiceAccount,
                    IsAdminAccount = resolved.IsAdminAccount,
                    HasExplicitPermissions = false,
                    IsInheritanceDisabled = isInheritanceDisabled,
                    ShareName = shareContext == null ? string.Empty : shareContext.ShareName,
                    ShareServer = shareContext == null ? string.Empty : shareContext.Server
                });
            }

            return entries;
        }

        private ResolvedPrincipal ResolvePrincipal(string sid, string fallbackName, ScanOptions options)
        {
            if (string.IsNullOrWhiteSpace(sid)) return null;
            if (options.ResolveIdentities || options.AnonymizeIdentities)
            {
                var resolved = _identityResolver.Resolve(sid);
                if (resolved != null)
                {
                    return resolved;
                }
            }
            return new ResolvedPrincipal
            {
                Sid = sid,
                Name = string.IsNullOrWhiteSpace(fallbackName) ? sid : fallbackName,
                IsGroup = SidClassifier.IsGroupSid(sid),
                IsDisabled = false,
                IsServiceAccount = false,
                IsAdminAccount = false
            };
        }

        private SharePermissionContext LoadSharePermissions(ScanOptions options, BlockingCollection<ErrorEntry> errorQueue)
        {
            try
            {
                var context = _sharePermissionService.TryGetSharePermissions(options.RootPath);
                var diagnostic = _sharePermissionService.LastDiagnostic;
                if (diagnostic != null && errorQueue != null)
                {
                    var message = string.IsNullOrWhiteSpace(diagnostic.TechnicalDetails)
                        ? diagnostic.Message
                        : string.Format("{0} Details: {1}", diagnostic.Message, diagnostic.TechnicalDetails);
                    errorQueue.Add(BuildErrorEntry(diagnostic.Path, diagnostic.ErrorType, message));
                }

                return context;
            }
            catch (Exception ex)
            {
                if (errorQueue != null)
                {
                    errorQueue.Add(BuildErrorEntry(options.RootPath, ex));
                }
                return null;
            }
        }

        private List<AclDiffKey> BuildBaselineKeys(ScanOptions options, BlockingCollection<ErrorEntry> errorQueue)
        {
            try
            {
                var ioRootPath = PathResolver.ToExtendedPath(options.RootPath);
                var accessSections = AccessControlSections.Access;
                if (options.ReadOwnerAndSacl)
                {
                    accessSections |= AccessControlSections.Owner | AccessControlSections.Audit;
                }
                var security = GetAccessControlWithFallback(
                    sections => new DirectoryInfo(ioRootPath).GetAccessControl(sections),
                    accessSections,
                    options.RootPath,
                    errorQueue,
                    null,
                    out var _);
                if (security == null)
                {
                    return null;
                }
                var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
                return BuildAclKeysFromRules(rules, options);
            }
            catch (Exception ex)
            {
                if (errorQueue != null)
                {
                    errorQueue.Add(BuildErrorEntry(options.RootPath, ex));
                }
                return null;
            }
        }

        private static List<AclDiffKey> BuildAclKeysFromRules(IEnumerable<FileSystemAccessRule> rules, ScanOptions options)
        {
            var keys = new List<AclDiffKey>();
            foreach (var rule in rules)
            {
                if (!options.IncludeInherited && rule.IsInherited)
                {
                    continue;
                }
                var sid = rule.IdentityReference.Value;
                keys.Add(new AclDiffKey
                {
                    Sid = sid,
                    AllowDeny = rule.AccessControlType.ToString(),
                    RightsMask = (int)rule.FileSystemRights,
                    InheritanceFlags = rule.InheritanceFlags.ToString(),
                    PropagationFlags = rule.PropagationFlags.ToString(),
                    IsInherited = rule.IsInherited
                });
            }
            return keys;
        }
    }
}
