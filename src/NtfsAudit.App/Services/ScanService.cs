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
                var dataLock = new object();
                var errorLock = new object();
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

                            List<string> children = null;
                            if (depth < options.MaxDepth)
                            {
                                try
                                {
                                    children = Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly).ToList();
                                }
                                catch (Exception ex)
                                {
                                    Interlocked.Increment(ref errorCount);
                                    lock (errorLock)
                                    {
                                        LogError(errorWriter, current, ex);
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

                            try
                            {
                                var security = new DirectoryInfo(current).GetAccessControl(AccessControlSections.Access);
                                var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));

                                foreach (FileSystemAccessRule rule in rules)
                                {
                                    token.ThrowIfCancellationRequested();
                                    var sid = rule.IdentityReference.Value;
                                    var resolved = _identityResolver.Resolve(sid);
                                    var rightsSummary = RightsNormalizer.Normalize(rule.FileSystemRights);
                                    var entry = new AceEntry
                                    {
                                        FolderPath = current,
                                        PrincipalName = resolved.Name,
                                        PrincipalSid = sid,
                                        PrincipalType = resolved.Type,
                                        AllowDeny = rule.AccessControlType.ToString(),
                                        RightsSummary = rightsSummary,
                                        IsInherited = rule.IsInherited,
                                        InheritanceFlags = rule.InheritanceFlags.ToString(),
                                        PropagationFlags = rule.PropagationFlags.ToString(),
                                        Source = "Diretto",
                                        Depth = depth,
                                        IsDisabled = resolved.IsDisabled
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

                                    lock (dataLock)
                                    {
                                        WriteExportRecord(dataWriter, entry);
                                    }

                                    if (resolved.IsGroup && options.ExpandGroups)
                                    {
                                        var members = _groupExpansion.ExpandGroup(sid, token);
                                        entry.MemberNames = members.Select(m =>
                                            string.IsNullOrWhiteSpace(m.Sid)
                                                ? m.Name
                                                : string.Format("{0} ({1})", m.Name, m.Sid)).ToList();
                                        foreach (var member in members)
                                        {
                                            var source = string.Format("Gruppo:{0}", resolved.Name);
                                            var memberEntry = new AceEntry
                                            {
                                                FolderPath = current,
                                                PrincipalName = member.Name,
                                                PrincipalSid = member.Sid,
                                                PrincipalType = "User",
                                                AllowDeny = rule.AccessControlType.ToString(),
                                                RightsSummary = rightsSummary,
                                                IsInherited = rule.IsInherited,
                                                InheritanceFlags = rule.InheritanceFlags.ToString(),
                                                PropagationFlags = rule.PropagationFlags.ToString(),
                                                Source = source,
                                                Depth = depth,
                                                IsDisabled = member.IsDisabled
                                            };
                                            lock (currentDetail)
                                            {
                                                currentDetail.UserEntries.Add(memberEntry);
                                                currentDetail.AllEntries.Add(memberEntry);
                                            }
                                            lock (dataLock)
                                            {
                                                WriteExportRecord(dataWriter, memberEntry);
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Interlocked.Increment(ref errorCount);
                                lock (errorLock)
                                {
                                    LogError(errorWriter, current, ex);
                                }
                            }

                            if (progress != null)
                            {
                                progress.Report(new ScanProgress
                                {
                                    Processed = processedCount,
                                    QueueCount = Volatile.Read(ref pendingCount),
                                    Errors = Volatile.Read(ref errorCount),
                                    Elapsed = stopwatch.Elapsed
                                });
                            }
                        }
                    }, token);
                }

                Task.WaitAll(workers);

                if (progress != null)
                {
                    progress.Report(new ScanProgress
                    {
                        Processed = Volatile.Read(ref processed),
                        QueueCount = Volatile.Read(ref pendingCount),
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

        private void WriteExportRecord(StreamWriter writer, AceEntry entry)
        {
            var record = new ExportRecord
            {
                FolderPath = entry.FolderPath,
                PrincipalName = entry.PrincipalName,
                PrincipalSid = entry.PrincipalSid,
                PrincipalType = entry.PrincipalType,
                AllowDeny = entry.AllowDeny,
                RightsSummary = entry.RightsSummary,
                IsInherited = entry.IsInherited,
                InheritanceFlags = entry.InheritanceFlags,
                PropagationFlags = entry.PropagationFlags,
                Source = entry.Source,
                Depth = entry.Depth,
                IsDisabled = entry.IsDisabled
            };
            writer.WriteLine(JsonConvert.SerializeObject(record));
        }

        private void LogError(StreamWriter writer, string path, Exception ex)
        {
            var error = new ErrorEntry
            {
                Path = path,
                ErrorType = ex.GetType().Name,
                Message = ex.Message
            };
            writer.WriteLine(JsonConvert.SerializeObject(error));
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
