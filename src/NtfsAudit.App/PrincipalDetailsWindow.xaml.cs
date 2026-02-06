using System.Collections.Generic;
using System.Linq;
using System.Windows;
using NtfsAudit.App.Models;

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
                Type = entry.IsGroup ? "Gruppo" : "Utente"
            }).ToList();
        }

        private class PrincipalRow
        {
            public string Name { get; set; }
            public string Sid { get; set; }
            public string Type { get; set; }
        }
    }
}
