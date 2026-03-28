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
using System.Threading;
using Newtonsoft.Json;
using NtfsAudit.App.Export;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public partial class ScanService
    {
        private static string EnsureTempDirectory()
        {
            foreach (var candidate in GetTempDirectoryCandidates())
            {
                if (string.IsNullOrWhiteSpace(candidate))
                {
                    continue;
                }

                try
                {
                    Directory.CreateDirectory(candidate);
                    return candidate;
                }
                catch
                {
                }
            }

            var fallback = RuntimePaths.GetTempRoot();
            Directory.CreateDirectory(fallback);
            return fallback;
        }

        private static IEnumerable<string> GetTempDirectoryCandidates()
        {
            var windowsSystemTempRoot = RuntimePaths.GetWindowsSystemTempRoot();
            if (!string.IsNullOrWhiteSpace(windowsSystemTempRoot))
            {
                yield return windowsSystemTempRoot;
            }

            yield return RuntimePaths.GetTempRoot();
        }

        private static void EnqueueDataRecord(BlockingCollection<ExportRecord> dataQueue, ExportRecord record, CancellationToken token)
        {
            while (!dataQueue.TryAdd(record, 100, token))
            {
                token.ThrowIfCancellationRequested();
            }
        }

        private static void DrainQueue<T>(BlockingCollection<T> queue, StreamWriter writer, CancellationToken token)
        {
            foreach (var item in queue.GetConsumingEnumerable(token))
            {
                writer.WriteLine(JsonConvert.SerializeObject(item));
            }
        }

        private ExportRecord BuildExportRecord(AceEntry entry, ScanOptions options)
        {
            return new ExportRecord
            {
                FolderPath = entry.FolderPath,
                PrincipalName = entry.PrincipalName,
                PrincipalSid = entry.PrincipalSid,
                PrincipalType = entry.PrincipalType,
                PermissionLayer = entry.PermissionLayer,
                AllowDeny = entry.AllowDeny,
                RightsSummary = entry.RightsSummary,
                RightsMask = entry.RightsMask,
                EffectiveRightsSummary = entry.EffectiveRightsSummary,
                EffectiveRightsMask = entry.EffectiveRightsMask,
                ShareRightsMask = entry.ShareRightsMask,
                NtfsRightsMask = entry.NtfsRightsMask,
                IsInherited = entry.IsInherited,
                AppliesToThisFolder = entry.AppliesToThisFolder,
                AppliesToSubfolders = entry.AppliesToSubfolders,
                AppliesToFiles = entry.AppliesToFiles,
                InheritanceFlags = entry.InheritanceFlags,
                PropagationFlags = entry.PropagationFlags,
                Source = entry.Source,
                PathKind = entry.PathKind,
                Depth = entry.Depth,
                ResourceType = entry.ResourceType,
                TargetPath = entry.TargetPath,
                Owner = entry.Owner,
                ShareName = entry.ShareName,
                ShareServer = entry.ShareServer,
                AuditSummary = entry.AuditSummary,
                RiskLevel = entry.RiskLevel,
                IsDisabled = entry.IsDisabled,
                IsServiceAccount = entry.IsServiceAccount,
                IsAdminAccount = entry.IsAdminAccount,
                HasExplicitPermissions = entry.HasExplicitPermissions,
                IsInheritanceDisabled = entry.IsInheritanceDisabled,
                MemberNames = entry.MemberNames == null ? null : new List<string>(entry.MemberNames),
                IncludeInherited = options.IncludeInherited,
                ResolveIdentities = options.ResolveIdentities,
                ExcludeServiceAccounts = options.ExcludeServiceAccounts,
                ExcludeAdminAccounts = options.ExcludeAdminAccounts,
                EnableAdvancedAudit = options.EnableAdvancedAudit,
                ComputeEffectiveAccess = options.ComputeEffectiveAccess,
                IncludeSharePermissions = options.IncludeSharePermissions,
                IncludeFiles = options.IncludeFiles,
                ReadOwnerAndSacl = options.ReadOwnerAndSacl,
                CompareBaseline = options.CompareBaseline,
                ScanAllDepths = options.ScanAllDepths,
                MaxDepth = options.MaxDepth,
                ExpandGroups = options.ExpandGroups,
                UsePowerShell = options.UsePowerShell
            };
        }

        private ErrorEntry BuildErrorEntry(string path, Exception ex)
        {
            return ScanExceptionClassifier.BuildErrorEntry(path, ex);
        }

        private ErrorEntry BuildErrorEntry(string path, string errorType, string message)
        {
            return new ErrorEntry
            {
                Path = path,
                ErrorType = string.IsNullOrWhiteSpace(errorType) ? "Error" : errorType,
                Message = message ?? string.Empty
            };
        }

        private AceEntry BuildScanOptionsRecord(ScanOptions options, PathKind rootPathKind)
        {
            return new AceEntry
            {
                FolderPath = options.RootPath,
                PrincipalName = "SCAN_OPTIONS",
                PrincipalSid = string.Empty,
                PrincipalType = "Meta",
                PermissionLayer = PermissionLayer.Ntfs,
                AllowDeny = string.Empty,
                RightsSummary = string.Empty,
                IsInherited = false,
                InheritanceFlags = string.Empty,
                PropagationFlags = string.Empty,
                Source = "Meta",
                PathKind = rootPathKind,
                Depth = 0,
                IsDisabled = false
            };
        }
    }
}
