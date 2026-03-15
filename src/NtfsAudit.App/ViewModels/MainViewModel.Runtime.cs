using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Forms;
using WinForms = System.Windows.Forms;
using System.Windows.Threading;
using Win32 = Microsoft.Win32;
using Newtonsoft.Json;
using WpfMessageBox = System.Windows.MessageBox;
using NtfsAudit.App.Cache;
using NtfsAudit.App.Export;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;

namespace NtfsAudit.App.ViewModels
{
    public partial class MainViewModel
    {
        private void UpdateProgress(ScanProgress progress)
        {
            CurrentPathText = string.IsNullOrWhiteSpace(progress.CurrentPath) ? string.Empty : progress.CurrentPath;
            ProcessedCount = progress.Processed;
            ProcessedFilesCount = progress.FilesProcessed;
            ElapsedText = FormatElapsed(progress.Elapsed);
            ErrorCount = progress.Errors;
            if (string.Equals(progress.Stage, "Errore", StringComparison.OrdinalIgnoreCase))
            {
                CurrentPathBackground = "#FFFFCDD2";
            }
            else if (!string.IsNullOrWhiteSpace(progress.CurrentPath))
            {
                CurrentPathBackground = "#FFC8E6C9";
            }
            else
            {
                CurrentPathBackground = "Transparent";
            }
        }

        private void LoadTree(ScanResult result)
        {
            FolderTree.Clear();
            _currentFilteredTreeMap = null;
            if (result == null)
            {
                return;
            }

            if (_fullTreeMap == null || _fullTreeMap.Count == 0)
            {
                _fullTreeMap = ResolveFullTreeMap(result);
            }

            var treeMap = _fullTreeMap;
            if (treeMap == null || treeMap.Count == 0)
            {
                return;
            }

            var preferredRoot = ResolveTreeRoot(treeMap, RootPath);
            var filteredTreeMap = ApplyTreeFilters(treeMap, result.Details, preferredRoot);
            _currentFilteredTreeMap = filteredTreeMap;
            var provider = new FolderTreeProvider(filteredTreeMap, result.Details);
            var roots = ResolveTreeRoots(filteredTreeMap, preferredRoot);
            if (roots.Count == 0) return;

            foreach (var rootPath in roots)
            {
                var rootName = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(rootName)) rootName = rootPath;
                var rootDetail = result.Details != null && result.Details.TryGetValue(rootPath, out var detail) ? detail : null;
                var rootSummary = rootDetail == null ? null : rootDetail.DiffSummary;
                var rootNode = new FolderNodeViewModel(
                    rootPath,
                    rootName,
                    provider,
                    rootDetail != null && rootDetail.HasExplicitPermissions,
                    rootDetail != null && rootDetail.IsInheritanceDisabled,
                    rootSummary == null ? 0 : rootSummary.Added.Count(key => !key.IsInherited),
                    rootSummary == null ? 0 : rootSummary.Removed.Count,
                    rootSummary == null ? 0 : rootSummary.DenyExplicitCount,
                    rootSummary != null && rootSummary.IsProtected,
                    rootDetail == null || rootDetail.BaselineSummary == null ? 0 : rootDetail.BaselineSummary.Added.Count,
                    rootDetail == null || rootDetail.BaselineSummary == null ? 0 : rootDetail.BaselineSummary.Removed.Count,
                    rootDetail != null && rootDetail.HasExplicitNtfs,
                    rootDetail != null && rootDetail.HasExplicitShare,
                    rootDetail != null && rootDetail.HasHighRiskEntries,
                    rootDetail != null && rootDetail.HasMediumRiskEntries,
                    rootDetail != null && rootDetail.HasLowRiskEntries);
                rootNode.IsExpanded = true;
                if (FolderTree.Count == 0)
                {
                    rootNode.IsSelected = true;
                }
                FolderTree.Add(rootNode);
            }
        }

        private void ReloadTreeWithFilters()
        {
            if (_scanResult == null || _fullTreeMap == null || _fullTreeMap.Count == 0) return;
            var preferredPath = SelectedFolderPath;
            var preferredRoot = ResolveTreeRoot(_fullTreeMap, _scanResult.RootPath);
            if (!string.IsNullOrWhiteSpace(preferredRoot)
                && !string.Equals(RootPath, preferredRoot, StringComparison.OrdinalIgnoreCase))
            {
                RootPath = preferredRoot;
            }
            LoadTree(_scanResult);
            if (_currentFilteredTreeMap == null || _currentFilteredTreeMap.Count == 0)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(preferredPath) && _currentFilteredTreeMap.ContainsKey(preferredPath))
            {
                SelectFolder(preferredPath);
                return;
            }

            var visibleRoot = ResolveTreeRoot(_currentFilteredTreeMap, RootPath);
            if (!string.IsNullOrWhiteSpace(visibleRoot))
            {
                SelectFolder(visibleRoot);
            }
        }

        private Dictionary<string, List<string>> ApplyTreeFilters(Dictionary<string, List<string>> treeMap, Dictionary<string, FolderDetail> details, string rootPath)
        {
            if (treeMap == null || treeMap.Count == 0) return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var filtered = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var roots = ResolveTreeRoots(treeMap, rootPath);
            if (roots.Count == 0)
            {
                return filtered;
            }

            bool IncludeNode(string node, string root)
            {
                if (string.IsNullOrWhiteSpace(node)) return false;
                var direct = NodeMatchesTreeFilters(node, details);
                if (!treeMap.TryGetValue(node, out var children) || children == null || children.Count == 0)
                {
                    if (direct) filtered[node] = new List<string>();
                    return direct;
                }

                var includedChildren = new List<string>();
                foreach (var child in children)
                {
                    if (IncludeNode(child, root)) includedChildren.Add(child);
                }
                if (direct || includedChildren.Count > 0 || string.Equals(node, root, StringComparison.OrdinalIgnoreCase))
                {
                    filtered[node] = includedChildren;
                    return true;
                }
                return false;
            }

            foreach (var root in roots)
            {
                IncludeNode(root, root);
            }

            return filtered;
        }

        private List<string> ResolveTreeRoots(Dictionary<string, List<string>> treeMap, string preferredRoot)
        {
            var roots = new List<string>();
            if (treeMap == null || treeMap.Count == 0)
            {
                return roots;
            }

            var childSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in treeMap)
            {
                if (entry.Value == null) continue;
                foreach (var child in entry.Value)
                {
                    if (!string.IsNullOrWhiteSpace(child))
                    {
                        childSet.Add(NormalizeTreePath(child));
                    }
                }
            }

            roots = treeMap.Keys
                .Where(key => !childSet.Contains(NormalizeTreePath(key)))
                .OrderBy(key => NormalizeTreePath(key))
                .ToList();

            if (roots.Count == 0)
            {
                roots.AddRange(treeMap.Keys.OrderBy(key => NormalizeTreePath(key)));
            }

            if (!string.IsNullOrWhiteSpace(preferredRoot))
            {
                var normalizedPreferred = NormalizeTreePath(preferredRoot);
                var preferred = roots.FirstOrDefault(root => string.Equals(NormalizeTreePath(root), normalizedPreferred, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(preferred))
                {
                    roots.Remove(preferred);
                    roots.Insert(0, preferred);
                }
            }

            return roots;
        }

        private Dictionary<string, List<string>> ResolveFullTreeMap(ScanResult result)
        {
            if (result == null)
            {
                return null;
            }

            var treeMap = result.TreeMap;
            var preferredRoot = !string.IsNullOrWhiteSpace(result.RootPath) ? result.RootPath : RootPath;
            var detailsTreeMap = BuildTreeMapFromDetails(result.Details, preferredRoot);
            var treeMapCount = treeMap == null ? 0 : treeMap.Count;
            var detailsTreeMapCount = detailsTreeMap == null ? 0 : detailsTreeMap.Count;

            Debug.WriteLine(string.Format(
                "[TreeMap] import source counts => treeMap:{0}, detailsTreeMap:{1}, root:{2}",
                treeMapCount,
                detailsTreeMapCount,
                preferredRoot));

            if ((treeMap == null || treeMap.Count == 0) && detailsTreeMap != null && detailsTreeMap.Count > 0)
            {
                Debug.WriteLine("[TreeMap] using detailsTreeMap (treeMap missing or empty)");
                return detailsTreeMap;
            }

            // Compatibilità con analisi legacy: alcune esportazioni storiche contengono TreeMap parziali.
            if (treeMap != null && treeMap.Count > 0 && detailsTreeMap != null && detailsTreeMap.Count > treeMap.Count)
            {
                Debug.WriteLine("[TreeMap] using detailsTreeMap (legacy partial treeMap detected)");
                return detailsTreeMap;
            }

            if (treeMap != null && treeMap.Count > 0)
            {
                Debug.WriteLine("[TreeMap] using persisted treeMap");
                return treeMap;
            }

            Debug.WriteLine("[TreeMap] fallback to export records treeMap reconstruction");
            return BuildTreeMapFromExportRecords(result.TempDataPath, preferredRoot);
        }

        private bool NodeMatchesTreeFilters(string path, Dictionary<string, FolderDetail> details)
        {
            if (details == null || !details.TryGetValue(path, out var detail) || detail == null)
            {
                return !AnyTreeFilterEnabled();
            }

            var diff = detail.DiffSummary;
            var hasDiff = diff != null && (diff.Added.Count > 0 || diff.Removed.Count > 0);
            var hasExplicitDeny = diff != null && diff.DenyExplicitCount > 0;
            var hasBaselineMismatch = detail.BaselineSummary != null && (detail.BaselineSummary.Added.Count > 0 || detail.BaselineSummary.Removed.Count > 0);

            var hasTypeMatch = true;
            var hasFiles = detail.HasFileEntries;
            var hasFolders = detail.HasFolderEntries;
            if (TreeFilterFilesOnly && !TreeFilterFoldersOnly)
            {
                hasTypeMatch = hasFiles;
            }
            else if (TreeFilterFoldersOnly && !TreeFilterFilesOnly)
            {
                hasTypeMatch = hasFolders;
            }

            if (!hasTypeMatch)
            {
                return false;
            }

            var includeByCategory = false;
            var categoryFilterSelected = false;

            if (TreeFilterExplicitOnly)
            {
                categoryFilterSelected = true;
                includeByCategory = includeByCategory || detail.HasExplicitPermissions;
            }

            if (TreeFilterInheritanceDisabledOnly)
            {
                categoryFilterSelected = true;
                includeByCategory = includeByCategory || detail.IsInheritanceDisabled;
            }

            if (TreeFilterDiffOnly)
            {
                categoryFilterSelected = true;
                includeByCategory = includeByCategory || hasDiff;
            }

            if (TreeFilterExplicitDenyOnly)
            {
                categoryFilterSelected = true;
                includeByCategory = includeByCategory || hasExplicitDeny;
            }

            if (TreeFilterBaselineMismatchOnly)
            {
                categoryFilterSelected = true;
                includeByCategory = includeByCategory || hasBaselineMismatch;
            }

            return !categoryFilterSelected || includeByCategory;
        }

        private static bool IsFileResourceType(string resourceType)
        {
            return string.Equals(resourceType, "File", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFolderResourceType(string resourceType)
        {
            return string.Equals(resourceType, "Folder", StringComparison.OrdinalIgnoreCase)
                || string.Equals(resourceType, "Cartella", StringComparison.OrdinalIgnoreCase)
                || string.Equals(resourceType, "Directory", StringComparison.OrdinalIgnoreCase);
        }

        private bool AnyTreeFilterEnabled()
        {
            return TreeFilterExplicitOnly
                || TreeFilterInheritanceDisabledOnly
                || TreeFilterDiffOnly
                || TreeFilterExplicitDenyOnly
                || TreeFilterBaselineMismatchOnly
                || (TreeFilterFilesOnly ^ TreeFilterFoldersOnly);
        }

        private void UpdateSelectedFolderInfo(string path, FolderDetail detail)
        {
            var entries = detail == null ? new List<AceEntry>() : detail.AllEntries;
            SelectedPathKind = PathResolver.DetectPathKind(path).ToString();
            SelectedOwnerSummary = entries.Select(e => e.Owner).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "-";
            SelectedInheritanceSummary = detail != null && detail.IsInheritanceDisabled ? "Ereditarietà disabilitata" : "Ereditarietà attiva";
            SelectedTotalAceCount = entries.Count;
            SelectedExplicitAceCount = entries.Count(e => !e.IsInherited);
            SelectedInheritedAceCount = entries.Count(e => e.IsInherited);
            SelectedDenyAceCount = entries.Count(e => string.Equals(e.AllowDeny, "Deny", StringComparison.OrdinalIgnoreCase));
            var layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (entries.Count > 0) layers.Add("NTFS");
            if (detail != null && detail.HasShareEntries) layers.Add("Share");
            if (detail != null && detail.HasEffectiveEntries) layers.Add("Effective");
            if (string.Equals(SelectedPathKind, "Nfs", StringComparison.OrdinalIgnoreCase)) layers.Add("NFS");
            SelectedPermissionLayers = layers.Count == 0 ? "-" : string.Join(", ", layers);
            SelectedRiskSummary = string.Format("High: {0}, Medium: {1}, Low: {2}",
                entries.Count(e => string.Equals(e.RiskLevel, "Alto", StringComparison.OrdinalIgnoreCase)),
                entries.Count(e => string.Equals(e.RiskLevel, "Medio", StringComparison.OrdinalIgnoreCase)),
                entries.Count(e => string.Equals(e.RiskLevel, "Basso", StringComparison.OrdinalIgnoreCase)));
            var warning = entries.Select(e => e.AuditSummary).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v) && v.IndexOf("non", StringComparison.OrdinalIgnoreCase) >= 0);
            if (string.IsNullOrWhiteSpace(warning) && string.Equals(SelectedPathKind, "Nfs", StringComparison.OrdinalIgnoreCase))
            {
                warning = "Percorso NFS: alcune ACL potrebbero non essere disponibili in ambiente Windows.";
            }
            SelectedAcquisitionWarnings = string.IsNullOrWhiteSpace(warning) ? "-" : warning;
            SelectedScannedAtText = _scanResult != null && _scanResult.ScannedAtUtc != default(DateTime)
                ? _scanResult.ScannedAtUtc.ToLocalTime().ToString("dd-MM-yyyy HH:mm:ss")
                : "-";
        }

        private string ResolveTreeRoot(Dictionary<string, List<string>> treeMap, string preferredRoot)
        {
            if (treeMap == null || treeMap.Count == 0)
            {
                return preferredRoot;
            }
            if (!string.IsNullOrWhiteSpace(preferredRoot))
            {
                var normalizedPreferred = NormalizeTreePath(preferredRoot);
                var matchingRoot = treeMap.Keys.FirstOrDefault(
                    key => string.Equals(NormalizeTreePath(key), normalizedPreferred, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(matchingRoot))
                {
                    return matchingRoot;
                }
            }

            var childSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in treeMap)
            {
                foreach (var child in entry.Value)
                {
                    if (!string.IsNullOrWhiteSpace(child))
                    {
                        childSet.Add(NormalizeTreePath(child));
                    }
                }
            }

            var roots = treeMap.Keys
                .Where(key => !childSet.Contains(NormalizeTreePath(key)))
                .OrderBy(key => NormalizeTreePath(key).Length)
                .ToList();
            if (roots.Count > 0)
            {
                return roots[0];
            }

            return treeMap.Keys.First();
        }

        private Dictionary<string, List<string>> BuildTreeMapFromDetails(
            Dictionary<string, FolderDetail> details,
            string fallbackRoot)
        {
            if (details == null || details.Count == 0)
            {
                if (string.IsNullOrWhiteSpace(fallbackRoot))
                {
                    return null;
                }
                return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    [fallbackRoot] = new List<string>()
                };
            }

            var normalizedRoot = NormalizeTreePath(fallbackRoot);
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in details.Keys.Where(key => !string.IsNullOrWhiteSpace(key)))
            {
                var normalizedPath = NormalizeTreePath(path);
                if (!IsWithinRoot(normalizedPath, normalizedRoot))
                {
                    continue;
                }
                var current = path;
                if (!map.ContainsKey(current))
                {
                    map[current] = new List<string>();
                }

                while (true)
                {
                    var parent = SafeGetParentPath(current);
                    if (string.IsNullOrWhiteSpace(parent))
                    {
                        break;
                    }
                    var normalizedParent = NormalizeTreePath(parent);
                    if (!IsWithinRoot(normalizedParent, normalizedRoot))
                    {
                        break;
                    }
                    if (!map.ContainsKey(parent))
                    {
                        map[parent] = new List<string>();
                    }
                    if (!map[parent].Contains(current, StringComparer.OrdinalIgnoreCase))
                    {
                        map[parent].Add(current);
                    }
                    if (!string.IsNullOrWhiteSpace(normalizedRoot)
                        && string.Equals(normalizedParent, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                    current = parent;
                }
            }

            if (!string.IsNullOrWhiteSpace(fallbackRoot) && !map.ContainsKey(fallbackRoot))
            {
                map[fallbackRoot] = new List<string>();
            }

            return map;
        }

        private Dictionary<string, List<string>> BuildTreeMapFromExportRecords(string dataPath, string fallbackRoot)
        {
            if (string.IsNullOrWhiteSpace(dataPath))
            {
                return null;
            }
            var ioPath = PathResolver.ToExtendedPath(dataPath);
            if (!File.Exists(ioPath))
            {
                return null;
            }

            var normalizedRoot = NormalizeTreePath(fallbackRoot);
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var line in File.ReadLines(ioPath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    ExportRecord record;
                    try
                    {
                        record = Newtonsoft.Json.JsonConvert.DeserializeObject<ExportRecord>(line);
                    }
                    catch
                    {
                        continue;
                    }
                    if (record == null) continue;
                    var path = record.FolderPath;
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    if (string.Equals(record.PrincipalType, "Meta", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(record.PrincipalName, "SCAN_OPTIONS", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    AddPathWithAncestors(map, path, normalizedRoot);
                }
            }
            catch
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(fallbackRoot) && !map.ContainsKey(fallbackRoot))
            {
                map[fallbackRoot] = new List<string>();
            }

            return map.Count == 0 ? null : map;
        }

        private void AddPathWithAncestors(Dictionary<string, List<string>> map, string path)
            => AddPathWithAncestors(map, path, NormalizeTreePath(RootPath));

        private void AddPathWithAncestors(Dictionary<string, List<string>> map, string path, string normalizedRoot)
        {
            if (!IsWithinRoot(NormalizeTreePath(path), normalizedRoot))
            {
                return;
            }
            var current = path;
            if (!map.ContainsKey(current))
            {
                map[current] = new List<string>();
            }

            while (true)
            {
                var parent = SafeGetParentPath(current);
                if (string.IsNullOrWhiteSpace(parent))
                {
                    break;
                }
                var normalizedParent = NormalizeTreePath(parent);
                if (!IsWithinRoot(normalizedParent, normalizedRoot))
                {
                    break;
                }
                if (!map.ContainsKey(parent))
                {
                    map[parent] = new List<string>();
                }
                if (!map[parent].Contains(current, StringComparer.OrdinalIgnoreCase))
                {
                    map[parent].Add(current);
                }
                if (!string.IsNullOrWhiteSpace(normalizedRoot)
                    && string.Equals(normalizedParent, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                current = parent;
            }
        }

        private string SafeGetParentPath(string path)
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

        private static string NormalizeTreePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            var normalized = PathResolver.FromExtendedPath(path);
            return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsWithinRoot(string candidate, string root)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }
            if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;
            return candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyDiffs(ScanResult result)
        {
            if (result == null || result.Details == null) return;
            if (result.UsesSqliteBackend) return;
            var diffService = new AclDiffService();
            diffService.ApplyDiffs(result.Details);
        }

        private FolderDetail GetFolderDetail(string path)
        {
            if (_scanResult == null || string.IsNullOrWhiteSpace(path) || _scanResult.Details == null)
            {
                return null;
            }

            if (!_scanResult.Details.TryGetValue(path, out var detail) || detail == null)
            {
                return null;
            }

            if (!detail.EntriesLoaded
                && _scanResult.UsesSqliteBackend
                && !string.IsNullOrWhiteSpace(_scanResult.SqliteDatabasePath)
                && File.Exists(PathResolver.ToExtendedPath(_scanResult.SqliteDatabasePath)))
            {
                var loadedDetail = _analysisSqliteStore.LoadFolderDetail(_scanResult.SqliteDatabasePath, path);
                if (loadedDetail != null)
                {
                    _scanResult.Details[path] = loadedDetail;
                    detail = loadedDetail;
                }
            }

            return detail;
        }

        private void LoadErrors(string path)
        {
            Errors.Clear();
            _errorsTruncated = false;
            if (string.IsNullOrWhiteSpace(path)) return;
            var ioPath = PathResolver.ToExtendedPath(path);
            if (!File.Exists(ioPath)) return;
            var loaded = 0;
            foreach (var line in File.ReadLines(ioPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var error = Newtonsoft.Json.JsonConvert.DeserializeObject<ErrorEntry>(line);
                    if (error != null)
                    {
                        if (loaded < MaxErrorsToLoad)
                        {
                            Errors.Add(error);
                            loaded++;
                        }
                        else
                        {
                            _errorsTruncated = true;
                            break;
                        }
                    }
                }
                catch
                {
                }
            }

            if (_errorsTruncated)
            {
                ProgressText = string.Format("Errori caricati parzialmente ({0}): limita uso RAM. Esporta il report completo per analisi totale.", MaxErrorsToLoad);
            }
        }

        private void RefreshAclFilters()
        {
            if (!_suspendUiPreferencePersistence)
            {
                SaveUiPreferences();
            }
            FilteredGroupEntries.Refresh();
            FilteredUserEntries.Refresh();
            FilteredAllEntries.Refresh();
            FilteredShareEntries.Refresh();
            FilteredEffectiveEntries.Refresh();
        }

        private void EnsureAtLeastOnePrincipalCategoryEnabled()
        {
            if (_showEveryone || _showAuthenticatedUsers || _showServiceAccounts || _showAdminAccounts || _showOtherPrincipals)
            {
                return;
            }

            _showOtherPrincipals = true;
            OnPropertyChanged("ShowOtherPrincipals");
        }

        private bool FilterErrors(object item)
        {
            var error = item as ErrorEntry;
            if (error == null) return false;
            return true;
        }

        private bool FilterAclEntries(object item)
        {
            var entry = item as AceEntry;
            if (entry == null) return false;
            if (!ShowAllow && !ShowDeny)
            {
                return false;
            }
            if (!ShowInherited && !ShowExplicit)
            {
                return false;
            }
            if (!ShowEveryone && !ShowAuthenticatedUsers && !ShowServiceAccounts && !ShowAdminAccounts && !ShowOtherPrincipals)
            {
                return false;
            }
            var isDeny = IsDenyEntry(entry);
            var isAllow = IsAllowEntry(entry);
            if (!ShowAllow && isAllow)
            {
                return false;
            }
            if (!ShowDeny && isDeny)
            {
                return false;
            }
            if (!ShowInherited && entry.IsInherited)
            {
                return false;
            }
            if (!ShowExplicit && !entry.IsInherited)
            {
                return false;
            }
            if (!ShowProtected && entry.IsInheritanceDisabled)
            {
                return false;
            }
            if (!ShowDisabled && entry.IsDisabled)
            {
                return false;
            }

            var isEveryone = IsEveryone(entry.PrincipalSid, entry.PrincipalName);
            var isAuthUsers = IsAuthenticatedUsers(entry.PrincipalSid, entry.PrincipalName);
            var isService = entry.IsServiceAccount || SidClassifier.IsServiceAccountSid(entry.PrincipalSid);
            var isAdmin = entry.IsAdminAccount || SidClassifier.IsPrivilegedGroupSid(entry.PrincipalSid);
            var isOther = !(isEveryone || isAuthUsers || isService || isAdmin);
            if (isEveryone && !ShowEveryone)
            {
                return false;
            }
            if (isAuthUsers && !ShowAuthenticatedUsers)
            {
                return false;
            }
            if (isService && !ShowServiceAccounts)
            {
                return false;
            }
            if (isAdmin && !ShowAdminAccounts)
            {
                return false;
            }
            if (isOther && !ShowOtherPrincipals)
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(AclFilter)) return true;
            var term = AclFilter.Trim();
            if (string.IsNullOrWhiteSpace(term)) return true;
            return MatchesFilter(entry.PrincipalName, term)
                || MatchesFilter(entry.PrincipalSid, term)
                || MatchesFilter(entry.PermissionLayer.ToString(), term)
                || MatchesFilter(entry.AllowDeny, term)
                || MatchesFilter(entry.RightsSummary, term)
                || MatchesFilter(entry.EffectiveRightsSummary, term)
                || MatchesFilter(entry.FolderPath, term)
                || MatchesFilter(GetFolderName(entry.FolderPath), term)
                || MatchesFilter(entry.AuditSummary, term)
                || MatchesFilter(entry.ResourceType, term)
                || MatchesFilter(entry.TargetPath, term)
                || MatchesFilter(entry.Owner, term)
                || MatchesFilter(entry.ShareName, term)
                || MatchesFilter(entry.ShareServer, term)
                || MatchesFilter(entry.RiskLevel, term)
                || MatchesFilter(entry.Source, term)
                || MatchesFilter(entry.PathKind.ToString(), term)
                || MatchesMemberFilter(entry.MemberNames, term);
        }

        private bool IsAllowEntry(AceEntry entry)
        {
            return entry != null && string.Equals(entry.AllowDeny, "Allow", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsDenyEntry(AceEntry entry)
        {
            return entry != null && string.Equals(entry.AllowDeny, "Deny", StringComparison.OrdinalIgnoreCase);
        }

        private string GetFolderName(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) return string.Empty;
            var trimmed = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? folderPath : name;
        }

        private bool MatchesMemberFilter(IEnumerable<string> members, string filter)
        {
            if (members == null) return false;
            return members.Any(member => MatchesFilter(member, filter));
        }

        private bool MatchesFilter(string value, string filter)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsEveryone(string sid, string name)
        {
            return string.Equals(sid, "S-1-1-0", StringComparison.OrdinalIgnoreCase)
                || MatchesFilter(name, "Everyone")
                || MatchesFilter(name, "Tutti");
        }

        private bool IsAuthenticatedUsers(string sid, string name)
        {
            return string.Equals(sid, "S-1-5-11", StringComparison.OrdinalIgnoreCase)
                || MatchesFilter(name, "Authenticated Users")
                || MatchesFilter(name, "Utenti autenticati");
        }

        private void ClearResults()
        {
            _suspendUiPreferencePersistence = true;
            try
            {
                FolderTree.Clear();
                _fullTreeMap = null;
                _currentFilteredTreeMap = null;
                GroupEntries.Clear();
                UserEntries.Clear();
                AllEntries.Clear();
                ShareEntries.Clear();
                EffectiveEntries.Clear();
                Errors.Clear();
                SelectedFolderPath = string.Empty;
                ProcessedCount = 0;
                ProcessedFilesCount = 0;
                ErrorCount = 0;
                ElapsedText = "00:00:00";
                CurrentPathText = string.Empty;
                CurrentPathBackground = "Transparent";
                AclFilter = string.Empty;
                ShowAllow = true;
                ShowDeny = true;
                ShowInherited = true;
                ShowExplicit = true;
                ShowProtected = true;
                ShowDisabled = true;
                ShowEveryone = true;
                ShowAuthenticatedUsers = true;
                ShowServiceAccounts = true;
                ShowAdminAccounts = true;
                ShowOtherPrincipals = true;
                ResetTreeFilters(false);
                UpdateSummary(null);
            }
            finally
            {
                _suspendUiPreferencePersistence = false;
            }

            SaveUiPreferences();
        }

        private void ResetTreeFilters()
        {
            ResetTreeFilters(true);
        }

        private void ResetTreeFilters(bool reloadTree)
        {
            _treeFilterExplicitOnly = false;
            _treeFilterInheritanceDisabledOnly = false;
            _treeFilterDiffOnly = false;
            _treeFilterExplicitDenyOnly = false;
            _treeFilterBaselineMismatchOnly = false;
            _treeFilterFilesOnly = true;
            _treeFilterFoldersOnly = true;
            OnPropertyChanged("TreeFilterExplicitOnly");
            OnPropertyChanged("TreeFilterInheritanceDisabledOnly");
            OnPropertyChanged("TreeFilterDiffOnly");
            OnPropertyChanged("TreeFilterExplicitDenyOnly");
            OnPropertyChanged("TreeFilterBaselineMismatchOnly");
            OnPropertyChanged("TreeFilterFilesOnly");
            OnPropertyChanged("TreeFilterFoldersOnly");

            if (_scanResult != null && _fullTreeMap != null && _fullTreeMap.Count > 0)
            {
                var root = ResolveTreeRoot(_fullTreeMap, _scanResult.RootPath);
                if (!string.IsNullOrWhiteSpace(root)
                    && !string.Equals(RootPath, root, StringComparison.OrdinalIgnoreCase))
                {
                    RootPath = root;
                }
            }

            if (reloadTree)
            {
                ReloadTreeWithFilters();
            }
        }

        private void UpdateDfsTargets()
        {
            var targets = PathResolver.GetDfsTargets(RootPath);
            var nextTargets = new ObservableCollection<string>(targets ?? new List<string>());
            var previousSelection = SelectedDfsTarget;
            DfsTargets = nextTargets;
            if (DfsTargets.Count == 0)
            {
                SelectedDfsTarget = string.Empty;
                return;
            }
            var selected = string.IsNullOrWhiteSpace(previousSelection)
                ? null
                : DfsTargets.FirstOrDefault(target => string.Equals(target, previousSelection, StringComparison.OrdinalIgnoreCase));
            SelectedDfsTarget = selected ?? DfsTargets[0];
            OnPropertyChanged("HasDfsTargets");
            OnPropertyChanged("DfsTargetBackground");
        }

        private void ApplyIdentityDependencies()
        {
            if (_resolveIdentities) return;
            if (_expandGroups)
            {
                _expandGroups = false;
                OnPropertyChanged("ExpandGroups");
            }
            if (_usePowerShell)
            {
                _usePowerShell = false;
                OnPropertyChanged("UsePowerShell");
            }
            if (_excludeServiceAccounts)
            {
                _excludeServiceAccounts = false;
                OnPropertyChanged("ExcludeServiceAccounts");
            }
            if (_excludeAdminAccounts)
            {
                _excludeAdminAccounts = false;
                OnPropertyChanged("ExcludeAdminAccounts");
            }
        }

        private void ApplyAdvancedAuditDependencies()
        {
            if (_enableAdvancedAudit) return;
            if (_computeEffectiveAccess)
            {
                _computeEffectiveAccess = false;
                OnPropertyChanged("ComputeEffectiveAccess");
            }
            if (_includeSharePermissions)
            {
                _includeSharePermissions = false;
                OnPropertyChanged("IncludeSharePermissions");
            }
            if (_includeFiles)
            {
                _includeFiles = false;
                OnPropertyChanged("IncludeFiles");
            }
            if (_readOwnerAndSacl)
            {
                _readOwnerAndSacl = false;
                OnPropertyChanged("ReadOwnerAndSacl");
            }
            if (_compareBaseline)
            {
                _compareBaseline = false;
                OnPropertyChanged("CompareBaseline");
            }
        }

        private void ApplyImportedOptions(ScanOptions options)
        {
            if (options == null) return;
            ScanAllDepths = options.ScanAllDepths;
            if (!options.ScanAllDepths && options.MaxDepth > 0)
            {
                MaxDepth = ClampMaxDepth(options.MaxDepth);
            }
            IncludeInherited = options.IncludeInherited;
            ResolveIdentities = options.ResolveIdentities;
            ExcludeServiceAccounts = options.ResolveIdentities && options.ExcludeServiceAccounts;
            ExcludeAdminAccounts = options.ResolveIdentities && options.ExcludeAdminAccounts;
            ExpandGroups = options.ResolveIdentities && options.ExpandGroups;
            UsePowerShell = options.ResolveIdentities && options.UsePowerShell;
            EnableAdvancedAudit = options.EnableAdvancedAudit;
            ComputeEffectiveAccess = options.EnableAdvancedAudit && options.ComputeEffectiveAccess;
            IncludeSharePermissions = options.EnableAdvancedAudit && options.IncludeSharePermissions;
            IncludeFiles = options.EnableAdvancedAudit && options.IncludeFiles;
            ReadOwnerAndSacl = options.EnableAdvancedAudit && options.ReadOwnerAndSacl;
            CompareBaseline = options.EnableAdvancedAudit && options.CompareBaseline;
        }

        private void UpdateCommands()
        {
            OnPropertyChanged("IsScanning");
            OnPropertyChanged("IsNotScanning");
            OnPropertyChanged("IsScanConfigEnabled");
            OnPropertyChanged("HasScanResult");
            OnPropertyChanged("HasUnexportedData");
            OnPropertyChanged("StatusText");
            OnPropertyChanged("StatusBrush");
            OnPropertyChanged("CanStart");
            OnPropertyChanged("CanStop");
            OnPropertyChanged("CanExport");
            OnPropertyChanged("CanImportAnalysis");
            OnPropertyChanged("IsBusy");
            OnPropertyChanged("IsNotBusy");
            StartCommand.RaiseCanExecuteChanged();
            AddScanRootCommand.RaiseCanExecuteChanged();
            RemoveScanRootCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
            ExportCommand.RaiseCanExecuteChanged();
            ImportAnalysisCommand.RaiseCanExecuteChanged();
            InstallServiceCommand.RaiseCanExecuteChanged();
            UninstallServiceCommand.RaiseCanExecuteChanged();
            ResetTreeFiltersCommand.RaiseCanExecuteChanged();
            CleanupResidualFilesCommand.RaiseCanExecuteChanged();
            SaveScanRootSetCommand.RaiseCanExecuteChanged();
            LoadScanRootSetCommand.RaiseCanExecuteChanged();
        }

        private string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalDays >= 1)
            {
                return elapsed.ToString(@"d\.hh\:mm\:ss");
            }

            return elapsed.ToString(@"hh\:mm\:ss");
        }

        private string BuildExportPath(string folder, string rootPath, string extension)
        {
            var fileName = BuildExportFileName(rootPath, extension);
            return Path.Combine(folder, fileName);
        }

        private string BuildExportFileName(string rootPath, string extension)
        {
            var safeRoot = rootPath ?? string.Empty;
            var baseName = BuildScanNameFromRoot(safeRoot);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "Root";
            var timestamp = DateTime.Now.ToString("yyyy_MM_dd_HH_mm");
            return string.Format("{0}_{1}.{2}", baseName, timestamp, extension);
        }

        private string ResolveInitialDirectory(string preferredDirectory, string rootPath)
        {
            if (!string.IsNullOrWhiteSpace(preferredDirectory) && Directory.Exists(preferredDirectory))
            {
                return preferredDirectory;
            }

            if (!string.IsNullOrWhiteSpace(rootPath))
            {
                var candidate = rootPath;
                if (!Directory.Exists(candidate))
                {
                    candidate = Path.GetDirectoryName(rootPath);
                }

                if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        private void UpdateLastDirectory(ref string targetDirectory, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                targetDirectory = directory;
            }
        }

        private int ClampMaxDepth(int value)
        {
            return value < 1 ? 1 : value;
        }

        private void InitializeScanTimer()
        {
            _scanTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _scanTimer.Tick += (_, __) =>
            {
                if (!_isScanning) return;
                ElapsedText = FormatElapsed(DateTime.Now - _scanStart);
            };
        }

        private void InitializeServiceStatusMonitor()
        {
            _serviceStatusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };
            _serviceStatusTimer.Tick += (_, __) => RefreshServiceRuntimeStatus();
            _serviceStatusTimer.Start();
            RefreshServiceRuntimeStatus();
        }

        private void RefreshServiceRuntimeStatus()
        {
            var serviceState = QueryServiceState();
            IsServiceInstalled = serviceState.IsInstalled;
            var status = TryReadServiceRuntimeStatus(RuntimePaths.GetServiceStatusPath());
            var viewState = _serviceRuntimeStatusPresenter.Build(serviceState.IsInstalled, serviceState.IsRunning, status);
            ServiceBadgeText = viewState.BadgeText;
            ServiceBadgeBackground = viewState.BadgeBackground;
            ServiceRuntimeStatusText = viewState.StatusText;
            IsServiceRuntimeRunning = viewState.IsServiceRuntimeRunning;
            if (viewState.IsServiceRuntimeRunning && !_isScanning)
            {
                ProgressText = ServiceRuntimeStatusText;
            }
        }

        private ServiceStateSnapshot QueryServiceState()
        {
            var queryResult = ExecuteScCommand(string.Format("query {0}", ServiceName), "query", false);
            if (queryResult.ExitCode == 1060)
            {
                return new ServiceStateSnapshot { IsInstalled = false, IsRunning = false };
            }

            var combinedOutput = string.Format("{0} {1}", queryResult.Output ?? string.Empty, queryResult.Error ?? string.Empty);
            if (queryResult.ExitCode != 0)
            {
                var isNotInstalled = combinedOutput.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0
                    || combinedOutput.IndexOf("non esiste", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isNotInstalled)
                {
                    return new ServiceStateSnapshot { IsInstalled = false, IsRunning = false };
                }

                return new ServiceStateSnapshot { IsInstalled = true, IsRunning = false };
            }

            var running = combinedOutput.IndexOf("RUNNING", StringComparison.OrdinalIgnoreCase) >= 0;
            return new ServiceStateSnapshot { IsInstalled = true, IsRunning = running };
        }

        private bool TryEnsureServiceInstalledForScan()
        {
            var serviceState = QueryServiceState();
            if (serviceState.IsInstalled)
            {
                return true;
            }

            ProgressText = "Servizio Windows non installato: installa NtfsAuditWorker o disattiva 'Esegui tramite servizio Windows'.";
            WpfMessageBox.Show(
                ProgressText,
                "Servizio non installato",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return false;
        }

        private bool TryValidateScanInputs(IReadOnlyCollection<string> roots, out string message)
        {
            if (roots == null || roots.Count == 0)
            {
                message = "Aggiungi almeno una cartella da analizzare.";
                return false;
            }

            foreach (var root in roots)
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    message = "È presente una cartella vuota nell'elenco scansione.";
                    return false;
                }

                var ioRoot = PathResolver.ToExtendedPath(root);
                if (File.Exists(ioRoot))
                {
                    message = string.Format("Il percorso selezionato è un file e non una cartella: {0}", root);
                    return false;
                }

                if (!Directory.Exists(ioRoot))
                {
                    message = string.Format("Percorso non valido o non raggiungibile: {0}", root);
                    return false;
                }
            }

            if (string.IsNullOrWhiteSpace(AuditOutputDirectory))
            {
                message = null;
                return true;
            }

            try
            {
                Directory.CreateDirectory(PathResolver.ToExtendedPath(AuditOutputDirectory));
            }
            catch (Exception ex) when (
                ex is UnauthorizedAccessException
                || ex is IOException
                || ex is NotSupportedException
                || ex is ArgumentException)
            {
                message = string.Format("Directory output non valida o non accessibile: {0}", AuditOutputDirectory);
                return false;
            }

            message = null;
            return true;
        }

        private static ServiceRuntimeStatus TryReadServiceRuntimeStatus(string statusPath)
        {
            if (string.IsNullOrWhiteSpace(statusPath) || !File.Exists(statusPath))
            {
                return null;
            }

            try
            {
                return JsonConvert.DeserializeObject<ServiceRuntimeStatus>(File.ReadAllText(statusPath));
            }
            catch
            {
                return null;
            }
        }

        private void StartElapsedTimer()
        {
            _scanStart = DateTime.Now;
            if (_scanTimer != null)
            {
                _scanTimer.Stop();
                _scanTimer.Start();
            }
        }

        private void StopElapsedTimer()
        {
            if (_scanTimer != null)
            {
                _scanTimer.Stop();
            }
        }

        private void SetBusy(bool isBusy)
        {
            if (_isBusy == isBusy) return;
            _isBusy = isBusy;
            UpdateCommands();
        }

        private bool ValidateImportedResult(ScanResult result, out string message)
        {
            if (result == null)
            {
                message = "Analisi importata non valida: dati mancanti.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(result.TempDataPath))
            {
                message = "Analisi importata non valida: file dati non presente.";
                return false;
            }
            var dataPath = PathResolver.ToExtendedPath(result.TempDataPath);
            if (!File.Exists(dataPath))
            {
                message = "Analisi importata non valida: file dati non trovato.";
                return false;
            }
            if (new FileInfo(dataPath).Length == 0)
            {
                message = "Analisi importata non valida: file dati vuoto.";
                return false;
            }
            if (result.Details == null || result.TreeMap == null)
            {
                message = "Analisi importata non valida: struttura dati incompleta (TreeMap/Details mancanti).";
                return false;
            }
            if (result.TreeMap.Count == 0)
            {
                message = "Analisi importata con albero cartelle vuoto.";
                return false;
            }
            if (result.Details.Count == 0)
            {
                message = "Analisi importata senza dettagli ACL.";
                return false;
            }
            message = null;
            return true;
        }

        private void LoadCache()
        {
            var cachePath = _cacheStore.GetCacheFilePath("sid-cache.json");
            _sidNameCache.Load(cachePath);
            _uiPreferencesPath = _cacheStore.GetCacheFilePath("ui-preferences.json");
            LoadUiPreferences();
        }

        private void SaveCache()
        {
            var cachePath = _cacheStore.GetCacheFilePath("sid-cache.json");
            _sidNameCache.Save(cachePath);
            SaveUiPreferences();
        }


        private void LoadUiPreferences()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_uiPreferencesPath) || !File.Exists(_uiPreferencesPath)) return;
                var json = File.ReadAllText(_uiPreferencesPath);
                var prefs = JsonConvert.DeserializeObject<UiPreferences>(json);
                if (prefs == null) return;

                _suspendUiPreferencePersistence = true;

                ShowAllow = prefs.ShowAllow;
                ShowDeny = prefs.ShowDeny;
                ShowInherited = prefs.ShowInherited;
                ShowExplicit = prefs.ShowExplicit;
                ShowProtected = prefs.ShowProtected;
                ShowDisabled = prefs.ShowDisabled;
                ShowEveryone = prefs.ShowEveryone;
                ShowAuthenticatedUsers = prefs.ShowAuthenticatedUsers;
                ShowServiceAccounts = prefs.ShowServiceAccounts;
                ShowAdminAccounts = prefs.ShowAdminAccounts;
                ShowOtherPrincipals = prefs.ShowOtherPrincipals;
                TreeFilterExplicitOnly = prefs.TreeFilterExplicitOnly;
                TreeFilterInheritanceDisabledOnly = prefs.TreeFilterInheritanceDisabledOnly;
                TreeFilterDiffOnly = prefs.TreeFilterDiffOnly;
                TreeFilterExplicitDenyOnly = prefs.TreeFilterExplicitDenyOnly;
                TreeFilterBaselineMismatchOnly = prefs.TreeFilterBaselineMismatchOnly;
                TreeFilterFilesOnly = prefs.TreeFilterFilesOnly;
                TreeFilterFoldersOnly = prefs.TreeFilterFoldersOnly;
                AuditOutputDirectory = prefs.AuditOutputDirectory;
                UseWindowsServiceMode = prefs.UseWindowsServiceMode;
                ScanRoots.Clear();
                _scanRootDfsTargets.Clear();
                _scanRootNamespacePaths.Clear();
                if (prefs.ScanRoots != null)
                {
                    foreach (var root in prefs.ScanRoots.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        ScanRoots.Add(root);
                    }
                }
                if (prefs.ScanRootTargets != null)
                {
                    foreach (var target in prefs.ScanRootTargets.Where(item => item != null && !string.IsNullOrWhiteSpace(item.RootPath)))
                    {
                        var key = GetScanRootKey(target.RootPath);
                        _scanRootDfsTargets[key] = target.DfsTarget;
                        if (!string.IsNullOrWhiteSpace(target.NamespacePath))
                        {
                            _scanRootNamespacePaths[key] = target.NamespacePath;
                        }
                    }
                }
                if (ScanRoots.Count > 0)
                {
                    SelectedScanRoot = ScanRoots[0];
                }
            }
            catch
            {
            }
            finally
            {
                _suspendUiPreferencePersistence = false;
            }
        }

        private void SaveUiPreferences()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_uiPreferencesPath)) return;
                var prefs = new UiPreferences
                {
                    ShowAllow = ShowAllow,
                    ShowDeny = ShowDeny,
                    ShowInherited = ShowInherited,
                    ShowExplicit = ShowExplicit,
                    ShowProtected = ShowProtected,
                    ShowDisabled = ShowDisabled,
                    ShowEveryone = ShowEveryone,
                    ShowAuthenticatedUsers = ShowAuthenticatedUsers,
                    ShowServiceAccounts = ShowServiceAccounts,
                    ShowAdminAccounts = ShowAdminAccounts,
                    ShowOtherPrincipals = ShowOtherPrincipals,
                    TreeFilterExplicitOnly = TreeFilterExplicitOnly,
                    TreeFilterInheritanceDisabledOnly = TreeFilterInheritanceDisabledOnly,
                    TreeFilterDiffOnly = TreeFilterDiffOnly,
                    TreeFilterExplicitDenyOnly = TreeFilterExplicitDenyOnly,
                    TreeFilterBaselineMismatchOnly = TreeFilterBaselineMismatchOnly,
                    TreeFilterFilesOnly = TreeFilterFilesOnly,
                    TreeFilterFoldersOnly = TreeFilterFoldersOnly,
                    AuditOutputDirectory = AuditOutputDirectory,
                    UseWindowsServiceMode = UseWindowsServiceMode,
                    ScanRoots = ScanRoots.ToList(),
                    ScanRootTargets = _scanRootDfsTargets.Select(item => new ScanRootTargetPreference
                    {
                        RootPath = GetScanRootKey(item.Key),
                        DfsTarget = item.Value,
                        NamespacePath = _scanRootNamespacePaths.ContainsKey(item.Key) ? _scanRootNamespacePaths[item.Key] : null
                    }).ToList()
                };
                File.WriteAllText(_uiPreferencesPath, JsonConvert.SerializeObject(prefs, Formatting.Indented));
            }
            catch
            {
            }
        }

        private sealed class UiPreferences
        {
            public bool ShowAllow { get; set; } = true;
            public bool ShowDeny { get; set; } = true;
            public bool ShowInherited { get; set; } = true;
            public bool ShowExplicit { get; set; } = true;
            public bool ShowProtected { get; set; } = true;
            public bool ShowDisabled { get; set; } = true;
            public bool ShowEveryone { get; set; } = true;
            public bool ShowAuthenticatedUsers { get; set; } = true;
            public bool ShowServiceAccounts { get; set; } = true;
            public bool ShowAdminAccounts { get; set; } = true;
            public bool ShowOtherPrincipals { get; set; } = true;
            public bool TreeFilterExplicitOnly { get; set; }
            public bool TreeFilterInheritanceDisabledOnly { get; set; }
            public bool TreeFilterDiffOnly { get; set; }
            public bool TreeFilterExplicitDenyOnly { get; set; }
            public bool TreeFilterBaselineMismatchOnly { get; set; }
            public bool TreeFilterFilesOnly { get; set; } = true;
            public bool TreeFilterFoldersOnly { get; set; } = true;
            public string AuditOutputDirectory { get; set; }
            public bool UseWindowsServiceMode { get; set; }
            public List<string> ScanRoots { get; set; }
            public List<ScanRootTargetPreference> ScanRootTargets { get; set; }
        }

        private sealed class ScanRootTargetPreference
        {
            public string RootPath { get; set; }
            public string DfsTarget { get; set; }
            public string NamespacePath { get; set; }
        }

        private sealed class ScanRootSet
        {
            public DateTime CreatedAtUtc { get; set; }
            public string RootPath { get; set; }
            public string AuditOutputDirectory { get; set; }
            public List<string> ScanRoots { get; set; }
            public List<ScanRootTargetPreference> ScanRootTargets { get; set; }
        }

        private void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private void RunOnUi(Action action)
        {
            var dispatcher = System.Windows.Application.Current == null ? null : System.Windows.Application.Current.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished || dispatcher.CheckAccess())
            {
                try
                {
                    action();
                }
                catch
                {
                }
                return;
            }
            try
            {
                dispatcher.Invoke(action);
            }
            catch
            {
            }
        }
    }
}
