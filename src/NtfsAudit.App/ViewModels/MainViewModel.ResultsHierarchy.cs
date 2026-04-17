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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;

namespace NtfsAudit.App.ViewModels
{
    public partial class MainViewModel
    {
        public ObservableCollection<ResultHierarchyNodeViewModel> ResultHierarchy
        {
            get { return _resultHierarchy; }
        }

        private void RebuildResultHierarchy(FolderDetail detail)
        {
            ResultHierarchy.Clear();
            if (detail == null || detail.AllEntries == null || detail.AllEntries.Count == 0)
            {
                OnPropertyChanged("ResultHierarchy");
                return;
            }

            foreach (var group in detail.AllEntries
                .GroupBy(entry => string.Format("{0}|{1}|{2}", entry.PrincipalType, entry.PrincipalSid, entry.PrincipalName), StringComparer.OrdinalIgnoreCase)
                    .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
            {
                var sample = group.First();
                var isGroup = string.Equals(sample.PrincipalType, "Group", StringComparison.OrdinalIgnoreCase);
                var node = new ResultHierarchyNodeViewModel(
                    sample.PrincipalName ?? LocalizationManager.Text("Hierarchy.UnknownPrincipal"),
                    sample.PrincipalSid ?? string.Empty,
                    isGroup ? LocalizationManager.Text("Hierarchy.Group") : LocalizationManager.Text("Hierarchy.User"),
                    isGroup ? "#FF3949AB" : "#FF00897B",
                    sample.RiskLevel ?? string.Empty,
                    ResolveRiskBadge(sample.RiskLevel),
                    isGroup
                        ? (() => LoadGroupMembersNodesAsync(sample.PrincipalSid))
                        : (() => LoadUserMembershipNodesAsync(sample.PrincipalSid)));

                foreach (var entry in group.OrderBy(item => item.ResourceType).ThenBy(item => item.AllowDeny).ThenBy(item => item.RightsSummary))
                {
                    node.Children.Add(BuildPermissionNode(entry));
                }

                ResultHierarchy.Add(node);
            }

            OnPropertyChanged("ResultHierarchy");
        }

        private ResultHierarchyNodeViewModel BuildPermissionNode(AceEntry entry)
        {
            var resourceLabel = string.Equals(entry.ResourceType, "File", StringComparison.OrdinalIgnoreCase)
                ? LocalizationManager.Text("Hierarchy.File")
                : LocalizationManager.Text("Hierarchy.Folder");
            var accessLabel = string.IsNullOrWhiteSpace(entry.AllowDeny) ? "NTFS" : entry.AllowDeny.ToUpperInvariant();
            var subtitle = string.Format("{0} | {1}", entry.TargetPath ?? entry.FolderPath ?? string.Empty, entry.RightsSummary ?? "-");
            return new ResultHierarchyNodeViewModel(
                entry.RightsSummary ?? LocalizationManager.Text("Hierarchy.RightsFallback"),
                subtitle,
                resourceLabel,
                string.Equals(entry.ResourceType, "File", StringComparison.OrdinalIgnoreCase) ? "#FF6D4C41" : "#FF546E7A",
                accessLabel,
                string.Equals(entry.AllowDeny, "Deny", StringComparison.OrdinalIgnoreCase) ? "#FFC62828" : "#FF1E88E5");
        }

        private async Task<ResultHierarchyNodeViewModel[]> LoadGroupMembersNodesAsync(string groupSid)
        {
            var members = await GetGroupMembersAsync(groupSid);
            return members
                .OrderBy(member => member.IsGroup ? 0 : 1)
                .ThenBy(member => member.Name, StringComparer.OrdinalIgnoreCase)
                .Select(BuildPrincipalNode)
                .ToArray();
        }

        private async Task<ResultHierarchyNodeViewModel[]> LoadUserMembershipNodesAsync(string userSid)
        {
            var groups = await GetUserGroupsAsync(userSid);
            if (groups == null || groups.Length == 0)
            {
                return new[]
                {
                    new ResultHierarchyNodeViewModel(LocalizationManager.Text("Hierarchy.NoNestedGroups"), string.Empty, LocalizationManager.Text("Hierarchy.Info"), "#FF90A4AE")
                };
            }

            return groups
                .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
                .Select(BuildPrincipalNode)
                .ToArray();
        }

        private ResultHierarchyNodeViewModel BuildPrincipalNode(ResolvedPrincipal principal)
        {
            var isGroup = principal != null && principal.IsGroup;
            return new ResultHierarchyNodeViewModel(
                principal == null ? LocalizationManager.Text("Hierarchy.UnknownEntry") : principal.Name ?? LocalizationManager.Text("Hierarchy.UnknownEntry"),
                principal == null ? string.Empty : principal.Sid ?? string.Empty,
                isGroup ? LocalizationManager.Text("Hierarchy.Group") : LocalizationManager.Text("Hierarchy.User"),
                isGroup ? "#FF3949AB" : "#FF00897B",
                principal != null && principal.IsDisabled ? LocalizationManager.Text("Hierarchy.Disabled") : string.Empty,
                principal != null && principal.IsDisabled ? "#FFC62828" : "#FFCFD8DC",
                isGroup && principal != null ? (() => LoadGroupMembersNodesAsync(principal.Sid)) : null);
        }

        private static string ResolveRiskBadge(string riskLevel)
        {
            switch (NormalizeRiskLevel(riskLevel))
            {
                case "high":
                    return "#FFC62828";
                case "medium":
                    return "#FFF9A825";
                case "low":
                    return "#FF2E7D32";
                default:
                    return "#FFCFD8DC";
            }
        }
    }
}
