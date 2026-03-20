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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using NtfsAudit.App.Export;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public partial class ScanService
    {
        private const string EveryoneSid = "S-1-1-0";
        private const string AuthenticatedUsersSid = "S-1-5-11";
        private const int ExportQueueCapacity = 2048;
        private static readonly EnumerationOptions DirectoryEnumerationOptions = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = 0
        };

        private readonly IdentityResolver _identityResolver;
        private readonly GroupExpansionService _groupExpansion;
        private readonly SharePermissionService _sharePermissionService;

        public ScanService(IdentityResolver identityResolver, GroupExpansionService groupExpansion)
            : this(identityResolver, groupExpansion, null)
        {
        }

        public ScanService(IdentityResolver identityResolver, GroupExpansionService groupExpansion, SharePermissionService sharePermissionService)
        {
            _identityResolver = identityResolver;
            _groupExpansion = groupExpansion;
            _sharePermissionService = sharePermissionService ?? new SharePermissionService();
        }

        public ScanResult Run(ScanOptions options, IProgress<ScanProgress> progress, CancellationToken token)
        {
            var runtimeOptions = options == null ? null : options.Clone();
            if (runtimeOptions != null && runtimeOptions.Credential != null)
            {
                runtimeOptions.Credential = ScanCredentialProtector.ResolveForRuntime(runtimeOptions.Credential);
            }

            return WindowsImpersonationHelper.Run(
                runtimeOptions == null ? null : runtimeOptions.Credential,
                () => RunCore(runtimeOptions, progress, token));
        }

        private ScanResult RunCore(ScanOptions options, IProgress<ScanProgress> progress, CancellationToken token)
        {
            if (options.ReadOwnerAndSacl)
            {
                TryEnableSecurityPrivilege();
            }
            var tempDir = EnsureTempDirectory();
            var timestamp = DateTime.Now.ToString("dd-MM-yyyy-HH-mm");
            var tempDataPath = Path.Combine(tempDir, string.Format("scan_{0}.jsonl", timestamp));
            var errorPath = Path.Combine(tempDir, string.Format("errors_{0}.jsonl", timestamp));

            var treeMap = new ConcurrentDictionary<string, ConcurrentDictionary<string, byte>>(StringComparer.OrdinalIgnoreCase);
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
            treeMap.TryAdd(options.RootPath, new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));

            using (var dataWriter = new StreamWriter(tempDataPath))
            using (var errorWriter = new StreamWriter(errorPath))
            {
                var dataQueue = new BlockingCollection<ExportRecord>(new ConcurrentQueue<ExportRecord>(), ExportQueueCapacity);
                var errorQueue = new BlockingCollection<ErrorEntry>(new ConcurrentQueue<ErrorEntry>());
                var baselineKeys = options.CompareBaseline ? BuildBaselineKeys(options, errorQueue) : null;
                var shareContext = options.IncludeSharePermissions ? LoadSharePermissions(options, errorQueue) : null;
                var shareAccessMap = shareContext == null
                    ? new Dictionary<string, PermissionCalculator.AccessAccumulator>(StringComparer.OrdinalIgnoreCase)
                    : PermissionCalculator.BuildAccessMap(shareContext.Permissions, true);
                var dataWriterTask = Task.Run(() => DrainQueue(dataQueue, dataWriter, token), token);
                var errorWriterTask = Task.Run(() => DrainQueue(errorQueue, errorWriter, token), token);

                EnqueueDataRecord(dataQueue, BuildExportRecord(BuildScanOptionsRecord(options, rootPathKind), options), token);
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
                                    var parentChildren = treeMap.GetOrAdd(current, _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
                                    foreach (var child in Directory.EnumerateDirectories(ioPath, "*", DirectoryEnumerationOptions))
                                    {
                                        var childPath = PathResolver.FromExtendedPath(child);
                                        if (IsDfsCachePath(childPath))
                                        {
                                            continue;
                                        }

                                        parentChildren.TryAdd(childPath, 0);
                                        treeMap.GetOrAdd(childPath, _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
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
                                treeMap.GetOrAdd(current, _ => new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase));
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
                                    auditFailureReason,
                                    rootPathKind);

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
                                    foreach (var file in Directory.EnumerateFiles(ioPath, "*", DirectoryEnumerationOptions))
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
                treeMapResult[entry.Key] = entry.Value.Keys.ToList();
            }

            var detailsResult = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in details)
            {
                UpdateFolderDetailFlags(entry.Value);
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
    }
}
