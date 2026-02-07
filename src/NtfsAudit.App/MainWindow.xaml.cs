using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NtfsAudit.App.Models;
using NtfsAudit.App.ViewModels;

namespace NtfsAudit.App
{
    public partial class MainWindow : Window
    {
        public MainWindow()
            : this(new MainViewModel())
        {
        }

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel ?? new MainViewModel();
        }

        private void TreeView_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var viewModel = DataContext as MainViewModel;
            var node = e.NewValue as FolderNodeViewModel;
            if (viewModel != null && node != null)
            {
                viewModel.SelectFolder(node.Path);
            }
        }

        private async void GroupEntries_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var entry = GetSelectedEntry(sender);
            if (entry == null) return;
            await ShowGroupMembers(entry);
        }

        private async void UserEntries_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var entry = GetSelectedEntry(sender);
            if (entry == null) return;
            await ShowUserGroups(entry);
        }

        private AceEntry GetSelectedEntry(object sender)
        {
            var grid = sender as DataGrid;
            return grid == null ? null : grid.SelectedItem as AceEntry;
        }

        private async Task ShowGroupMembers(AceEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.PrincipalSid))
            {
                MessageBox.Show(this, "SID non disponibile per il gruppo selezionato.", "Dettagli gruppo", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var viewModel = DataContext as MainViewModel;
            if (viewModel == null) return;
            var members = await viewModel.GetGroupMembersAsync(entry.PrincipalSid);
            var title = string.Format("Membri del gruppo: {0}", entry.PrincipalName);
            var window = new PrincipalDetailsWindow(title, members);
            window.Owner = this;
            window.ShowDialog();
        }

        private async Task ShowUserGroups(AceEntry entry)
        {
            if (string.IsNullOrWhiteSpace(entry.PrincipalSid))
            {
                MessageBox.Show(this, "SID non disponibile per l'utente selezionato.", "Dettagli utente", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var viewModel = DataContext as MainViewModel;
            if (viewModel == null) return;
            var groups = await viewModel.GetUserGroupsAsync(entry.PrincipalSid);
            var title = string.Format("Gruppi dell'utente: {0}", entry.PrincipalName);
            var window = new PrincipalDetailsWindow(title, groups);
            window.Owner = this;
            window.ShowDialog();
        }

        private void ExpandTree_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MainViewModel;
            if (viewModel == null) return;
            foreach (var node in viewModel.FolderTree)
            {
                SetExpanded(node, true);
            }
        }

        private void CollapseTree_Click(object sender, RoutedEventArgs e)
        {
            var viewModel = DataContext as MainViewModel;
            if (viewModel == null) return;
            foreach (var node in viewModel.FolderTree)
            {
                SetExpanded(node, false);
            }
        }

        private void SetExpanded(FolderNodeViewModel node, bool expanded)
        {
            if (node == null || node.IsPlaceholder) return;
            node.IsExpanded = expanded;
            foreach (var child in node.Children)
            {
                SetExpanded(child, expanded);
            }
        }
    }
}
