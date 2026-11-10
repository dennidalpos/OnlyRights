using NtfsAudit.Core.Logging;
using NtfsAudit.Core.Export;
using NtfsAudit.Core.Cache;
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
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using NtfsAudit.Core.Models;
using NtfsAudit.App.Services;
using NtfsAudit.Core.Services;

namespace NtfsAudit.App
{
    public partial class PrincipalDetailsWindow : Window
    {
        public PrincipalDetailsWindow(string title, IEnumerable<ResolvedPrincipal> entries)
        {
            InitializeComponent();
            Title = title;
            HeaderText.Text = title;
            LoadEntries(entries);
        }

        private void LoadEntries(IEnumerable<ResolvedPrincipal> entries)
        {
            var list = entries == null
                ? new List<ResolvedPrincipal>()
                : entries.Where(entry => entry != null)
                    .OrderBy(entry => entry.IsGroup)
                    .ThenBy(entry => entry.Name)
                    .ToList();

            if (list.Count == 0)
            {
                EntriesGrid.Visibility = Visibility.Collapsed;
                EmptyText.Visibility = Visibility.Visible;
                return;
            }

            EntriesGrid.ItemsSource = list.Select(entry => new PrincipalRow
            {
                Name = entry.Name,
                Sid = entry.Sid,
                Type = entry.IsGroup ? LocalizationManager.Text("Principal.Group") : LocalizationManager.Text("Principal.User"),
                IsDisabled = entry.IsDisabled
            }).ToList();
        }

        private class PrincipalRow
        {
            public string Name { get; set; }
            public string Sid { get; set; }
            public string Type { get; set; }
            public bool IsDisabled { get; set; }
        }
    }
}
