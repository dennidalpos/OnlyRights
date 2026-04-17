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
                    sample.PrincipalName ?? "(unknown principal)",
                    sample.PrincipalSid ?? string.Empty,
                    isGroup ? "GROUP" : "USER",
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
            var resourceLabel = string.Equals(entry.ResourceType, "File", StringComparison.OrdinalIgnoreCase) ? "FILE" : "FOLDER";
            var accessLabel = string.IsNullOrWhiteSpace(entry.AllowDeny) ? "NTFS" : entry.AllowDeny.ToUpperInvariant();
            var subtitle = string.Format("{0} | {1}", entry.TargetPath ?? entry.FolderPath ?? string.Empty, entry.RightsSummary ?? "-");
            return new ResultHierarchyNodeViewModel(
                entry.RightsSummary ?? "(rights)",
                subtitle,
                resourceLabel,
                string.Equals(resourceLabel, "FILE", StringComparison.OrdinalIgnoreCase) ? "#FF6D4C41" : "#FF546E7A",
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
                    new ResultHierarchyNodeViewModel("No nested groups", string.Empty, "INFO", "#FF90A4AE")
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
                principal == null ? "(unknown)" : principal.Name ?? "(unknown)",
                principal == null ? string.Empty : principal.Sid ?? string.Empty,
                isGroup ? "GROUP" : "USER",
                isGroup ? "#FF3949AB" : "#FF00897B",
                principal != null && principal.IsDisabled ? "DISABLED" : string.Empty,
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
