using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
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

            var treeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<WorkItem>();
            var processed = 0;
            var errorCount = 0;
            var stopwatch = Stopwatch.StartNew();

            queue.Enqueue(new WorkItem(options.RootPath, 0));
            treeMap[options.RootPath] = new List<string>();

            using (var dataWriter = new StreamWriter(tempDataPath))
            using (var errorWriter = new StreamWriter(errorPath))
            {
                while (queue.Count > 0)
                {
                    token.ThrowIfCancellationRequested();
                    var workItem = queue.Dequeue();
                    var current = workItem.Path;
                    var depth = workItem.Depth;
                    processed++;

                    if (!details.ContainsKey(current))
                    {
                        details[current] = new FolderDetail();
                    }

                    List<string> children = null;
                    if (depth < options.MaxDepth)
                    {
                        try
                        {
                            children = Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly).ToList();
                        }
                        catch (Exception ex)
                        {
                            errorCount++;
                            LogError(errorWriter, current, ex);
                        }
                    }

                    if (children != null)
                    {
                        if (!treeMap.ContainsKey(current)) treeMap[current] = new List<string>();
                        foreach (var child in children)
                        {
                            treeMap[current].Add(child);
                            if (!treeMap.ContainsKey(child)) treeMap[child] = new List<string>();
                            queue.Enqueue(new WorkItem(child, depth + 1));
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
                                Depth = depth
                            };

                            details[current].AllEntries.Add(entry);
                            if (resolved.IsGroup)
                            {
                                details[current].GroupEntries.Add(entry);
                            }
                            else
                            {
                                details[current].UserEntries.Add(entry);
                            }

                            WriteExportRecord(dataWriter, entry);

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
                                        Depth = depth
                                    };
                                    details[current].UserEntries.Add(memberEntry);
                                    details[current].AllEntries.Add(memberEntry);
                                    WriteExportRecord(dataWriter, memberEntry);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        LogError(errorWriter, current, ex);
                    }

                    if (progress != null)
                    {
                        progress.Report(new ScanProgress
                        {
                            Processed = processed,
                            QueueCount = queue.Count,
                            Errors = errorCount,
                            Elapsed = stopwatch.Elapsed
                        });
                    }
                }

                if (progress != null)
                {
                    progress.Report(new ScanProgress
                    {
                        Processed = processed,
                        QueueCount = queue.Count,
                        Errors = errorCount,
                        Elapsed = stopwatch.Elapsed
                    });
                }
            }

            return new ScanResult
            {
                TempDataPath = tempDataPath,
                ErrorPath = errorPath,
                Details = details,
                TreeMap = treeMap
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
                Depth = entry.Depth
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
