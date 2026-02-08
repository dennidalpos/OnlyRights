using System.Collections.Generic;
using System.IO;
using System.Linq;
using NtfsAudit.App.Models;
using NtfsAudit.App.ViewModels;

namespace NtfsAudit.App.Services
{
    public class FolderTreeProvider
    {
        private readonly Dictionary<string, List<string>> _childrenMap;
        private readonly Dictionary<string, FolderDetail> _details;

        public FolderTreeProvider(Dictionary<string, List<string>> childrenMap, Dictionary<string, FolderDetail> details)
        {
            _childrenMap = childrenMap;
            _details = details;
        }

        public IEnumerable<FolderNodeViewModel> GetChildren(string parentPath)
        {
            List<string> children;
            if (!_childrenMap.TryGetValue(parentPath, out children))
            {
                yield break;
            }

            foreach (var child in children)
            {
                var name = Path.GetFileName(child.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(name)) name = child;
                var flags = GetFlags(child);
                yield return new FolderNodeViewModel(
                    child,
                    name,
                    this,
                    flags.hasExplicitPermissions,
                    flags.isInheritanceDisabled,
                    flags.explicitAddedCount,
                    flags.explicitRemovedCount,
                    flags.denyExplicitCount,
                    flags.isProtected,
                    flags.baselineAddedCount,
                    flags.baselineRemovedCount,
                    flags.hasExplicitNtfs,
                    flags.hasExplicitShare);
            }
        }

        public bool HasChildren(string parentPath)
        {
            List<string> children;
            return _childrenMap.TryGetValue(parentPath, out children) && children.Count > 0;
        }

        private (bool hasExplicitPermissions, bool isInheritanceDisabled, int explicitAddedCount, int explicitRemovedCount, int denyExplicitCount, bool isProtected, int baselineAddedCount, int baselineRemovedCount, bool hasExplicitNtfs, bool hasExplicitShare) GetFlags(string path)
        {
            if (_details == null || string.IsNullOrWhiteSpace(path))
            {
                return (false, false, 0, 0, 0, false, 0, 0, false, false);
            }

            FolderDetail detail;
            if (!_details.TryGetValue(path, out detail) || detail == null)
            {
                return (false, false, 0, 0, 0, false, 0, 0, false, false);
            }

            var summary = detail.DiffSummary;
            var added = summary == null ? 0 : summary.Added.Count(key => !key.IsInherited);
            var removed = summary == null ? 0 : summary.Removed.Count;
            var deny = summary == null ? 0 : summary.DenyExplicitCount;
            var isProtected = summary != null ? summary.IsProtected : detail.IsInheritanceDisabled;
            var baselineAdded = detail.BaselineSummary == null ? 0 : detail.BaselineSummary.Added.Count;
            var baselineRemoved = detail.BaselineSummary == null ? 0 : detail.BaselineSummary.Removed.Count;
            return (detail.HasExplicitPermissions, detail.IsInheritanceDisabled, added, removed, deny, isProtected, baselineAdded, baselineRemoved, detail.HasExplicitNtfs, detail.HasExplicitShare);
        }
    }
}
