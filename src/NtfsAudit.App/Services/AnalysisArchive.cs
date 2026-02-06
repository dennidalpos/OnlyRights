using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json;
using NtfsAudit.App.Export;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public class AnalysisArchive
    {
        private const string DataEntryName = "data.jsonl";
        private const string ErrorsEntryName = "errors.jsonl";
        private const string TreeEntryName = "tree.json";
        private const string MetaEntryName = "meta.json";

        public void Export(ScanResult result, string rootPath, string outputPath)
        {
            if (result == null) throw new ArgumentNullException("result");
            if (string.IsNullOrWhiteSpace(outputPath)) throw new ArgumentException("Output path required", "outputPath");

            using (var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create))
            {
                AddFileEntry(archive, DataEntryName, result.TempDataPath);
                AddFileEntry(archive, ErrorsEntryName, result.ErrorPath);
                AddJsonEntry(archive, TreeEntryName, result.TreeMap);
                AddJsonEntry(archive, MetaEntryName, new ArchiveMeta
                {
                    RootPath = rootPath,
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        public AnalysisArchiveResult Import(string archivePath)
        {
            if (string.IsNullOrWhiteSpace(archivePath)) throw new ArgumentException("Archive path required", "archivePath");
            var tempDir = Path.Combine(Path.GetTempPath(), "NtfsAudit", "imports", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            using (var archive = ZipFile.OpenRead(archivePath))
            {
                ExtractEntry(archive, DataEntryName, tempDir);
                ExtractEntry(archive, ErrorsEntryName, tempDir);
                ExtractEntry(archive, TreeEntryName, tempDir);
                ExtractEntry(archive, MetaEntryName, tempDir);
            }

            var dataPath = Path.Combine(tempDir, DataEntryName);
            var errorPath = Path.Combine(tempDir, ErrorsEntryName);
            var treePath = Path.Combine(tempDir, TreeEntryName);
            var metaPath = Path.Combine(tempDir, MetaEntryName);

            var treeMap = LoadTreeMap(treePath, dataPath);
            var meta = LoadMeta(metaPath);

            var details = BuildDetailsFromExport(dataPath);

            var result = new ScanResult
            {
                TempDataPath = dataPath,
                ErrorPath = errorPath,
                Details = details,
                TreeMap = treeMap
            };

            return new AnalysisArchiveResult
            {
                ScanResult = result,
                RootPath = meta.RootPath
            };
        }

        private Dictionary<string, List<string>> LoadTreeMap(string treePath, string dataPath)
        {
            if (File.Exists(treePath))
            {
                try
                {
                    var tree = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(File.ReadAllText(treePath));
                    if (tree != null && tree.Count > 0)
                    {
                        return new Dictionary<string, List<string>>(tree, StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch
                {
                }
            }

            return BuildTreeFromExport(dataPath);
        }

        private ArchiveMeta LoadMeta(string metaPath)
        {
            if (!File.Exists(metaPath))
            {
                return new ArchiveMeta();
            }

            try
            {
                return JsonConvert.DeserializeObject<ArchiveMeta>(File.ReadAllText(metaPath)) ?? new ArchiveMeta();
            }
            catch
            {
                return new ArchiveMeta();
            }
        }

        private Dictionary<string, FolderDetail> BuildDetailsFromExport(string dataPath)
        {
            var details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(dataPath)) return details;

            foreach (var line in File.ReadLines(dataPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                ExportRecord record;
                try
                {
                    record = JsonConvert.DeserializeObject<ExportRecord>(line);
                }
                catch
                {
                    continue;
                }
                if (record == null) continue;

                FolderDetail detail;
                if (!details.TryGetValue(record.FolderPath, out detail))
                {
                    detail = new FolderDetail();
                    details[record.FolderPath] = detail;
                }

                var entry = new AceEntry
                {
                    FolderPath = record.FolderPath,
                    PrincipalName = record.PrincipalName,
                    PrincipalSid = record.PrincipalSid,
                    PrincipalType = record.PrincipalType,
                    AllowDeny = record.AllowDeny,
                    RightsSummary = record.RightsSummary,
                    IsInherited = record.IsInherited,
                    InheritanceFlags = record.InheritanceFlags,
                    PropagationFlags = record.PropagationFlags,
                    Source = record.Source,
                    Depth = record.Depth
                };

                detail.AllEntries.Add(entry);
                if (string.Equals(record.PrincipalType, "Group", StringComparison.OrdinalIgnoreCase))
                {
                    detail.GroupEntries.Add(entry);
                }
                else
                {
                    detail.UserEntries.Add(entry);
                }
            }

            return details;
        }

        private Dictionary<string, List<string>> BuildTreeFromExport(string dataPath)
        {
            var treeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(dataPath)) return treeMap;

            var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadLines(dataPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                ExportRecord record;
                try
                {
                    record = JsonConvert.DeserializeObject<ExportRecord>(line);
                }
                catch
                {
                    continue;
                }
                if (record == null || string.IsNullOrWhiteSpace(record.FolderPath)) continue;
                folders.Add(record.FolderPath);
            }

            var toProcess = folders.ToList();
            foreach (var folder in toProcess)
            {
                var parent = SafeGetParent(folder);
                while (!string.IsNullOrWhiteSpace(parent))
                {
                    if (!folders.Add(parent))
                    {
                        break;
                    }
                    parent = SafeGetParent(parent);
                }
            }

            foreach (var folder in folders)
            {
                if (!treeMap.ContainsKey(folder))
                {
                    treeMap[folder] = new List<string>();
                }
            }

            foreach (var folder in folders)
            {
                var parent = SafeGetParent(folder);
                if (parent != null && treeMap.ContainsKey(parent))
                {
                    treeMap[parent].Add(folder);
                }
            }

            return treeMap;
        }

        private string SafeGetParent(string path)
        {
            try
            {
                var parent = Directory.GetParent(path);
                return parent == null ? null : parent.FullName;
            }
            catch
            {
                return null;
            }
        }

        private void AddFileEntry(ZipArchive archive, string entryName, string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) return;
            archive.CreateEntryFromFile(sourcePath, entryName);
        }

        private void AddJsonEntry(ZipArchive archive, string entryName, object data)
        {
            var entry = archive.CreateEntry(entryName);
            using (var stream = entry.Open())
            using (var writer = new StreamWriter(stream))
            {
                writer.Write(JsonConvert.SerializeObject(data));
            }
        }

        private void ExtractEntry(ZipArchive archive, string entryName, string destinationDir)
        {
            var entry = archive.GetEntry(entryName);
            if (entry == null) return;
            var destinationPath = Path.Combine(destinationDir, entryName);
            entry.ExtractToFile(destinationPath, true);
        }

        private class ArchiveMeta
        {
            public string RootPath { get; set; }
            public DateTime CreatedAt { get; set; }
        }
    }

    public class AnalysisArchiveResult
    {
        public ScanResult ScanResult { get; set; }
        public string RootPath { get; set; }
    }
}
