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
using Newtonsoft.Json;
using NtfsAudit.App.Export;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public class ScanService
    {
        private readonly IdentityResolver _identityResolver;
        private readonly GroupExpansionService _groupExpansion;
        public ScanService(IdentityResolver identityResolver, GroupExpansionService groupExpansion)
        {
            _identityResolver = identityResolver;
            _groupExpansion = groupExpansion;
        }

        public ScanResult Run(ScanOptions options, IProgress<ScanProgress> progress, CancellationToken token)
        {
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
            var errorCount = 0;
            var pendingCount = 0;
            var stopwatch = Stopwatch.StartNew();

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
                var dataWriterTask = Task.Run(() => DrainQueue(dataQueue, dataWriter, token), token);
                var errorWriterTask = Task.Run(() => DrainQueue(errorQueue, errorWriter, token), token);

                dataQueue.Add(BuildExportRecord(BuildScanOptionsRecord(options), options));
                var workerCount = Math.Max(2, Environment.ProcessorCount);
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
                            var currentDetail = details.GetOrAdd(current, _ => new FolderDetail());
                            var processedCount = Interlocked.Increment(ref processed);

                            if (progress != null)
                            {
                                progress.Report(new ScanProgress
                                {
                                    Processed = processedCount,
                                    Errors = Volatile.Read(ref errorCount),
                                    Elapsed = stopwatch.Elapsed,
                                    Stage = "Enumerazione cartelle",
                                    CurrentPath = current
                                });
                            }

                            List<string> children = null;
                            if (depth < options.MaxDepth)
                            {
                                try
                                {
                                    var ioPath = PathResolver.ToExtendedPath(current);
                                    children = new List<string>();
                                    foreach (var child in Directory.EnumerateDirectories(ioPath, "*", SearchOption.TopDirectoryOnly))
                                    {
                                        children.Add(PathResolver.FromExtendedPath(child));
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
                                            Errors = Volatile.Read(ref errorCount),
                                            Elapsed = stopwatch.Elapsed,
                                            Stage = "Errore",
                                            CurrentPath = current
                                        });
                                    }
                                }
                            }

                            if (children != null)
                            {
                                var parentBag = treeMap.GetOrAdd(current, _ => new ConcurrentBag<string>());
                                foreach (var child in children)
                                {
                                    parentBag.Add(child);
                                    treeMap.GetOrAdd(child, _ => new ConcurrentBag<string>());
                                    Enqueue(new WorkItem(child, depth + 1));
                                }
                            }

                            if (progress != null)
                            {
                                progress.Report(new ScanProgress
                                {
                                    Processed = processedCount,
                                    Errors = Volatile.Read(ref errorCount),
                                    Elapsed = stopwatch.Elapsed,
                                    Stage = "Lettura ACL",
                                    CurrentPath = current
                                });
                            }

                            try
                            {
                                var ioPath = PathResolver.ToExtendedPath(current);
                                var security = new DirectoryInfo(ioPath).GetAccessControl(AccessControlSections.Access);
                                var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
                                var isInheritanceDisabled = security.AreAccessRulesProtected;
                                var hasExplicitPermissions = false;

                                foreach (FileSystemAccessRule rule in rules)
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
                                    if (options.ResolveIdentities && options.ExcludeServiceAccounts && resolved.IsServiceAccount)
                                    {
                                        continue;
                                    }
                                    if (options.ResolveIdentities && options.ExcludeAdminAccounts && resolved.IsAdminAccount)
                                    {
                                        continue;
                                    }
                                    var rightsSummary = RightsNormalizer.Normalize(rule.FileSystemRights);
                                    var entry = new AceEntry
                                    {
                                        FolderPath = current,
                                        PrincipalName = resolved.Name,
                                        PrincipalSid = sid,
                                        PrincipalType = resolved.Type,
                                        AllowDeny = rule.AccessControlType.ToString(),
                                        RightsSummary = rightsSummary,
                                        RightsMask = (int)rule.FileSystemRights,
                                        IsInherited = rule.IsInherited,
                                        InheritanceFlags = rule.InheritanceFlags.ToString(),
                                        PropagationFlags = rule.PropagationFlags.ToString(),
                                        Source = "Diretto",
                                        Depth = depth,
                                        IsDisabled = resolved.IsDisabled,
                                        IsServiceAccount = resolved.IsServiceAccount,
                                        IsAdminAccount = resolved.IsAdminAccount,
                                        HasExplicitPermissions = !rule.IsInherited,
                                        IsInheritanceDisabled = isInheritanceDisabled
                                    };

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
                                                FolderPath = current,
                                                PrincipalName = member.Name,
                                                PrincipalSid = member.Sid,
                                                PrincipalType = "User",
                                                AllowDeny = rule.AccessControlType.ToString(),
                                                RightsSummary = rightsSummary,
                                                RightsMask = (int)rule.FileSystemRights,
                                                IsInherited = rule.IsInherited,
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
                                    currentDetail.HasExplicitPermissions = hasExplicitPermissions;
                                    currentDetail.IsInheritanceDisabled = isInheritanceDisabled;
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
                TreeMap = treeMapResult
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
                AllowDeny = entry.AllowDeny,
                RightsSummary = entry.RightsSummary,
                RightsMask = entry.RightsMask,
                IsInherited = entry.IsInherited,
                InheritanceFlags = entry.InheritanceFlags,
                PropagationFlags = entry.PropagationFlags,
                Source = entry.Source,
                Depth = entry.Depth,
                IsDisabled = entry.IsDisabled,
                IsServiceAccount = entry.IsServiceAccount,
                IsAdminAccount = entry.IsAdminAccount,
                HasExplicitPermissions = entry.HasExplicitPermissions,
                IsInheritanceDisabled = entry.IsInheritanceDisabled,
                MemberNames = entry.MemberNames == null ? null : new List<string>(entry.MemberNames),
                IncludeInherited = options.IncludeInherited,
                ResolveIdentities = options.ResolveIdentities,
                ExcludeServiceAccounts = options.ExcludeServiceAccounts,
                ExcludeAdminAccounts = options.ExcludeAdminAccounts
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

        private AceEntry BuildScanOptionsRecord(ScanOptions options)
        {
            return new AceEntry
            {
                FolderPath = options.RootPath,
                PrincipalName = "SCAN_OPTIONS",
                PrincipalSid = string.Empty,
                PrincipalType = "Meta",
                AllowDeny = string.Empty,
                RightsSummary = string.Empty,
                IsInherited = false,
                InheritanceFlags = string.Empty,
                PropagationFlags = string.Empty,
                Source = "Meta",
                Depth = 0,
                IsDisabled = false
            };
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
