using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Newtonsoft.Json;
using NtfsAudit.App.Export;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public class ScanService
    {
        private const string EveryoneSid = "S-1-1-0";
        private const string AuthenticatedUsersSid = "S-1-5-11";
        private readonly IdentityResolver _identityResolver;
        private readonly GroupExpansionService _groupExpansion;
        private readonly SharePermissionService _sharePermissionService;
        public ScanService(IdentityResolver identityResolver, GroupExpansionService groupExpansion)
        {
            _identityResolver = identityResolver;
            _groupExpansion = groupExpansion;
            _sharePermissionService = new SharePermissionService();
        }

        public ScanResult Run(ScanOptions options, IProgress<ScanProgress> progress, CancellationToken token)
        {
            if (options.ReadOwnerAndSacl)
            {
                TryEnableSecurityPrivilege();
            }
            var tempDir = Path.Combine(Path.GetTempPath(), "NtfsAudit");
            Directory.CreateDirectory(tempDir);
            var timestamp = DateTime.Now.ToString("dd-MM-yyyy-HH-mm");
            var tempDataPath = Path.Combine(tempDir, string.Format("scan_{0}.jsonl", timestamp));
            var errorPath = Path.Combine(tempDir, string.Format("errors_{0}.jsonl", timestamp));

            var treeMap = new ConcurrentDictionary<string, ConcurrentBag<string>>(StringComparer.OrdinalIgnoreCase);
            var details = new ConcurrentDictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase);
            var queue = new ConcurrentQueue<WorkItem>();
            var queueSignal = new SemaphoreSlim(0);
            var processed = 0;
            var processedFiles = 0;
            var errorCount = 0;
            var pendingCount = 0;
            var stopwatch = Stopwatch.StartNew();
            var rootPathKind = PathResolver.DetectPathKind(options.RootPath);

            void Enqueue(WorkItem workItem)
            {
                queue.Enqueue(workItem);
                Interlocked.Increment(ref pendingCount);
                queueSignal.Release();
            }

            Enqueue(new WorkItem(options.RootPath, 0));
            treeMap.TryAdd(options.RootPath, new ConcurrentBag<string>());

            using (var dataWriter = new StreamWriter(tempDataPath))
            using (var errorWriter = new StreamWriter(errorPath))
            {
                var dataQueue = new BlockingCollection<ExportRecord>(new ConcurrentQueue<ExportRecord>());
                var errorQueue = new BlockingCollection<ErrorEntry>(new ConcurrentQueue<ErrorEntry>());
                var baselineKeys = options.CompareBaseline ? BuildBaselineKeys(options, errorQueue) : null;
                var shareContext = options.IncludeSharePermissions ? LoadSharePermissions(options, errorQueue) : null;
                var shareAccessMap = shareContext == null
                    ? new Dictionary<string, PermissionCalculator.AccessAccumulator>(StringComparer.OrdinalIgnoreCase)
                    : PermissionCalculator.BuildAccessMap(shareContext.Permissions, true);
                var dataWriterTask = Task.Run(() => DrainQueue(dataQueue, dataWriter, token), token);
                var errorWriterTask = Task.Run(() => DrainQueue(errorQueue, errorWriter, token), token);

                dataQueue.Add(BuildExportRecord(BuildScanOptionsRecord(options, rootPathKind), options));
                var workerCount = Math.Max(2, Math.Min(Environment.ProcessorCount, 8));
                var workers = new Task[workerCount];
                for (var i = 0; i < workerCount; i++)
                {
                    workers[i] = Task.Run(() =>
                    {
                        while (true)
                        {
                            token.ThrowIfCancellationRequested();
                            WorkItem workItem;
                            if (!queue.TryDequeue(out workItem))
                            {
                                if (Volatile.Read(ref pendingCount) == 0)
                                {
                                    break;
                                }
                                queueSignal.Wait(100, token);
                                continue;
                            }

                            Interlocked.Decrement(ref pendingCount);
                            var current = workItem.Path;
                            var depth = workItem.Depth;
                            if (IsDfsCachePath(current))
                            {
                                continue;
                            }
                            var currentDetail = details.GetOrAdd(current, _ => new FolderDetail());
                            var processedCount = Interlocked.Increment(ref processed);

                            if (progress != null)
                            {
                                progress.Report(new ScanProgress
                                {
                                    Processed = processedCount,
                                    FilesProcessed = Volatile.Read(ref processedFiles),
                                    Errors = Volatile.Read(ref errorCount),
                                    Elapsed = stopwatch.Elapsed,
                                    Stage = "Enumerazione cartelle",
                                    CurrentPath = current
                                });
                            }

                            var hasChildren = false;
                            if (depth < options.MaxDepth)
                            {
                                try
                                {
                                    var ioPath = PathResolver.ToExtendedPath(current);
                                    var enumerationOptions = new EnumerationOptions
                                    {
                                        IgnoreInaccessible = true,
                                        RecurseSubdirectories = false,
                                        AttributesToSkip = 0
                                    };
                                    var parentBag = treeMap.GetOrAdd(current, _ => new ConcurrentBag<string>());
                                    foreach (var child in Directory.EnumerateDirectories(ioPath, "*", enumerationOptions))
                                    {
                                        var childPath = PathResolver.FromExtendedPath(child);
                                        parentBag.Add(childPath);
                                        treeMap.GetOrAdd(childPath, _ => new ConcurrentBag<string>());
                                        if (IsDfsCachePath(childPath)) continue;
                                        Enqueue(new WorkItem(childPath, depth + 1));
                                        hasChildren = true;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Interlocked.Increment(ref errorCount);
                                    errorQueue.Add(BuildErrorEntry(current, ex));
                                    if (progress != null)
                                    {
                                        progress.Report(new ScanProgress
                                        {
                                            Processed = processedCount,
                                            FilesProcessed = Volatile.Read(ref processedFiles),
                                            Errors = Volatile.Read(ref errorCount),
                                            Elapsed = stopwatch.Elapsed,
                                            Stage = "Errore",
                                            CurrentPath = current
                                        });
                                    }
                                }
                            }

                            if (hasChildren)
                            {
                                treeMap.GetOrAdd(current, _ => new ConcurrentBag<string>());
                            }

                            if (progress != null)
                            {
                                progress.Report(new ScanProgress
                                {
                                    Processed = processedCount,
                                    FilesProcessed = Volatile.Read(ref processedFiles),
                                    Errors = Volatile.Read(ref errorCount),
                                    Elapsed = stopwatch.Elapsed,
                                    Stage = "Lettura ACL",
                                    CurrentPath = current
                                });
                            }

                            try
                            {
                                var ioPath = PathResolver.ToExtendedPath(current);
                                var directoryInfo = new DirectoryInfo(ioPath);
                                var accessSections = AccessControlSections.Access;
                                if (options.ReadOwnerAndSacl)
                                {
                                    accessSections |= AccessControlSections.Owner | AccessControlSections.Audit;
                                }
                                var security = GetAccessControlWithFallback(
                                    sections => directoryInfo.GetAccessControl(sections),
                                    accessSections,
                                    current,
                                    errorQueue,
                                    () => Interlocked.Increment(ref errorCount),
                                    out var auditFailureReason);
                                ProcessAccessControl(
                                    security,
                                    current,
                                    current,
                                    false,
                                    depth,
                                    options,
                                    currentDetail,
                                    baselineKeys,
                                    shareContext,
                                    shareAccessMap,
                                    dataQueue,
                                    errorQueue,
                                    token,
                                    auditFailureReason, rootPathKind);

                                if (options.IncludeFiles)
                                {
                                    if (progress != null)
                                    {
                                        progress.Report(new ScanProgress
                                        {
                                            Processed = processedCount,
                                            FilesProcessed = Volatile.Read(ref processedFiles),
                                            Errors = Volatile.Read(ref errorCount),
                                            Elapsed = stopwatch.Elapsed,
                                            Stage = "Lettura ACL file",
                                            CurrentPath = current
                                        });
                                    }
                                    var enumerationOptions = new EnumerationOptions
                                    {
                                        IgnoreInaccessible = true,
                                        RecurseSubdirectories = false,
                                        AttributesToSkip = 0
                                    };
                                    foreach (var file in Directory.EnumerateFiles(ioPath, "*", enumerationOptions))
                                    {
                                        token.ThrowIfCancellationRequested();
                                        Interlocked.Increment(ref processedFiles);
                                        try
                                        {
                                            var filePath = PathResolver.FromExtendedPath(file);
                                            var fileInfo = new FileInfo(file);
                                            var fileSecurity = GetAccessControlWithFallback(
                                                sections => fileInfo.GetAccessControl(sections),
                                                accessSections,
                                                filePath,
                                                errorQueue,
                                                () => Interlocked.Increment(ref errorCount),
                                                out var fileAuditFailureReason);
                                            ProcessAccessControl(
                                                fileSecurity,
                                                current,
                                                filePath,
                                                true,
                                                depth,
                                                options,
                                                currentDetail,
                                                null,
                                                shareContext,
                                                shareAccessMap,
                                                dataQueue,
                                                errorQueue,
                                                token,
                                                fileAuditFailureReason,
                                                rootPathKind);
                                        }
                                        catch (Exception ex)
                                        {
                                            Interlocked.Increment(ref errorCount);
                                            errorQueue.Add(BuildErrorEntry(PathResolver.FromExtendedPath(file), ex));
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Interlocked.Increment(ref errorCount);
                                errorQueue.Add(BuildErrorEntry(current, ex));
                                if (progress != null)
                                {
                                    progress.Report(new ScanProgress
                                    {
                                        Processed = processedCount,
                                        FilesProcessed = Volatile.Read(ref processedFiles),
                                        Errors = Volatile.Read(ref errorCount),
                                        Elapsed = stopwatch.Elapsed,
                                        Stage = "Errore",
                                        CurrentPath = current
                                    });
                                }
                            }
                        }
                    }, token);
                }

                try
                {
                    Task.WaitAll(workers);
                }
                catch (AggregateException ex)
                {
                    if (ex.InnerExceptions.All(inner => inner is OperationCanceledException))
                    {
                        throw new OperationCanceledException(token);
                    }

                    throw;
                }

                dataQueue.CompleteAdding();
                errorQueue.CompleteAdding();
                Task.WaitAll(dataWriterTask, errorWriterTask);

                if (progress != null)
                {
                    progress.Report(new ScanProgress
                    {
                        Processed = Volatile.Read(ref processed),
                        FilesProcessed = Volatile.Read(ref processedFiles),
                        Errors = Volatile.Read(ref errorCount),
                        Elapsed = stopwatch.Elapsed
                    });
                }
            }

            var treeMapResult = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in treeMap)
            {
                treeMapResult[entry.Key] = entry.Value.ToList();
            }

            var detailsResult = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in details)
            {
                detailsResult[entry.Key] = entry.Value;
            }

            return new ScanResult
            {
                TempDataPath = tempDataPath,
                ErrorPath = errorPath,
                Details = detailsResult,
                TreeMap = treeMapResult,
                RootPath = options.RootPath,
                RootPathKind = rootPathKind,
                ScanOptions = options,
                ScannedAtUtc = DateTime.UtcNow
            };
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
            return new ErrorEntry
            {
                Path = path,
                ErrorType = ex.GetType().Name,
                Message = ex.Message
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
            var ntfsPermissions = BuildNtfsPermissions(rules, options, folderKey, targetPath);
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
                if (options.ResolveIdentities)
                {
                    resolved = _identityResolver.Resolve(sid);
                }
                else
                {
                    resolved = new ResolvedPrincipal
                    {
                        Sid = sid,
                        Name = sid,
                        IsGroup = false,
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
                    FolderPath = folderKey,
                    TargetPath = targetPath,
                    ResourceType = isFile ? "File" : "Cartella",
                    Owner = owner,
                    AuditSummary = auditSummary,
                    PrincipalName = resolved.Name,
                    PrincipalSid = sid,
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
                    Source = isFile ? "File" : "Diretto",
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
                    if (resolved.IsGroup)
                    {
                        currentDetail.GroupEntries.Add(entry);
                    }
                    else
                    {
                        currentDetail.UserEntries.Add(entry);
                    }
                }

                List<ResolvedPrincipal> members = null;
                if (resolved.IsGroup && options.ExpandGroups && options.ResolveIdentities)
                {
                    members = _groupExpansion.ExpandGroup(sid, token);
                    entry.MemberNames = members.Select(m =>
                        string.IsNullOrWhiteSpace(m.Sid)
                            ? m.Name
                            : string.Format("{0} ({1})", m.Name, m.Sid)).ToList();
                }

                dataQueue.Add(BuildExportRecord(entry, options));

                if (members != null)
                {
                    foreach (var member in members)
                    {
                        var source = string.Format("Gruppo:{0}", resolved.Name);
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
                            FolderPath = folderKey,
                            TargetPath = targetPath,
                            ResourceType = isFile ? "File" : "Cartella",
                            Owner = owner,
                            AuditSummary = auditSummary,
                            PrincipalName = member.Name,
                            PrincipalSid = member.Sid,
                            PrincipalType = "User",
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
                            currentDetail.UserEntries.Add(memberEntry);
                            currentDetail.AllEntries.Add(memberEntry);
                        }
                        dataQueue.Add(BuildExportRecord(memberEntry, options));
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
                folderKey,
                targetPath,
                isFile,
                depth,
                dataQueue,
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
                dataQueue.Add(BuildExportRecord(entry, options));
            }
            foreach (var entry in effectiveEntries)
            {
                dataQueue.Add(BuildExportRecord(entry, options));
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
                    ResourceType = isFile ? "File" : "Cartella",
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
                    ResourceType = isFile ? "File" : "Cartella",
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
            if (options.ResolveIdentities)
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
                IsGroup = false,
                IsDisabled = false,
                IsServiceAccount = false,
                IsAdminAccount = false
            };
        }

        private SharePermissionContext LoadSharePermissions(ScanOptions options, BlockingCollection<ErrorEntry> errorQueue)
        {
            try
            {
                return _sharePermissionService.TryGetSharePermissions(options.RootPath);
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
                    auditFailureReason = "SACL non disponibile (privilegi)";
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
                    auditFailureReason = "SACL non disponibile (accesso negato)";
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
                var summaries = new List<string>();
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
                return "SACL non disponibile (privilegi)";
            }
            catch (UnauthorizedAccessException)
            {
                return "SACL non disponibile (accesso negato)";
            }
            catch (InvalidOperationException)
            {
                return "SACL non disponibile (sezione audit mancante)";
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
            return string.IsNullOrWhiteSpace(summary) ? "Nessuna voce SACL" : summary;
        }

        private static string EvaluateRisk(AceEntry entry)
        {
            if (entry == null) return "Basso";
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
                return "Alto";
            }
            if (allow && (isBroadPrincipal && rank >= 2))
            {
                return "Medio";
            }
            if (!allow)
            {
                return "Medio";
            }
            if (entry.IsInheritanceDisabled)
            {
                return "Medio";
            }
            return "Basso";
        }

        private static bool IsEveryone(string sidOrName)
        {
            return string.Equals(sidOrName, EveryoneSid, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAuthenticatedUsers(string sidOrName)
        {
            return string.Equals(sidOrName, AuthenticatedUsersSid, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDfsCachePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var folderName = Path.GetFileName(trimmed);
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
                var parent = Path.GetDirectoryName(trimmed);
                var parentName = string.IsNullOrWhiteSpace(parent)
                    ? string.Empty
                    : Path.GetFileName(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.Equals(parentName, "DfsrPrivate", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(parentName, "DFSR", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (string.Equals(folderName, "DFSR", StringComparison.OrdinalIgnoreCase))
            {
                var parent = Path.GetDirectoryName(trimmed);
                var parentName = string.IsNullOrWhiteSpace(parent) ? string.Empty : Path.GetFileName(parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                return string.Equals(parentName, "System Volume Information", StringComparison.OrdinalIgnoreCase);
            }

            return false;
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
