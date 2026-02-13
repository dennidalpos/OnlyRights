using System;
using System.ComponentModel;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;
using NtfsAudit.App.Models;
using NtfsAudit.App.ViewModels;

namespace NtfsAudit.App
{
    public partial class MainWindow : Window
    {
        private readonly WinForms.NotifyIcon _notifyIcon;
        private readonly DispatcherTimer _trayTimer;
        private string _lastTrayStatus;
        private bool _forceClose;
        public MainWindow()
            : this(new MainViewModel())
        {
        }

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel ?? new MainViewModel();

            _notifyIcon = new WinForms.NotifyIcon
            {
                Icon = SystemIcons.Information,
                Visible = false,
                Text = "NTFS Audit"
            };
            _notifyIcon.DoubleClick += (_, __) => RestoreFromTray();

            var trayMenu = new WinForms.ContextMenuStrip();
            trayMenu.Items.Add("Apri", null, (_, __) => RestoreFromTray());
            trayMenu.Items.Add("Esci", null, (_, __) =>
            {
                _forceClose = true;
                _notifyIcon.Visible = false;
                Close();
            });
            _notifyIcon.ContextMenuStrip = trayMenu;

            _trayTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _trayTimer.Tick += (_, __) => UpdateTrayStatus();
            _trayTimer.Start();

            StateChanged += (_, __) => HandleWindowStateChanged();
            Closing += OnMainWindowClosing;
            Closed += (_, __) =>
            {
                _trayTimer.Stop();
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            };

            UpdateTrayStatus();
        }

        private void UpdateTrayStatus()
        {
            var viewModel = DataContext as MainViewModel;
            if (viewModel == null)
            {
                _notifyIcon.Visible = WindowState == WindowState.Minimized;
                return;
            }

            var status = viewModel.ServiceRuntimeStatusText;
            var showTray = viewModel.IsServiceRuntimeRunning || WindowState == WindowState.Minimized;
            _notifyIcon.Visible = showTray;
            _notifyIcon.Text = TruncateForNotifyIcon(string.IsNullOrWhiteSpace(status) ? "NTFS Audit" : status);

            if (viewModel.IsServiceRuntimeRunning
                && !string.IsNullOrWhiteSpace(status)
                && !string.Equals(_lastTrayStatus, status, StringComparison.OrdinalIgnoreCase))
            {
                _notifyIcon.ShowBalloonTip(3000, "NTFS Audit", status, WinForms.ToolTipIcon.Info);
            }

            _lastTrayStatus = status;
        }

        private void HandleWindowStateChanged()
        {
            if (WindowState != WindowState.Minimized)
            {
                return;
            }

            Hide();
            UpdateTrayStatus();
            _notifyIcon.ShowBalloonTip(2500, "NTFS Audit", "App ridotta in tray. Monitoraggio servizio attivo.", WinForms.ToolTipIcon.Info);
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            UpdateTrayStatus();
        }

        private void OnMainWindowClosing(object sender, CancelEventArgs e)
        {
            if (_forceClose)
            {
                return;
            }

            var viewModel = DataContext as MainViewModel;
            if (viewModel != null && viewModel.IsServiceRuntimeRunning)
            {
                e.Cancel = true;
                WindowState = WindowState.Minimized;
                Hide();
                UpdateTrayStatus();
                _notifyIcon.ShowBalloonTip(2500, "NTFS Audit", "Scansione servizio in corso: app mantenuta in tray.", WinForms.ToolTipIcon.Info);
            }
        }

        private static string TruncateForNotifyIcon(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "NTFS Audit";
            return input.Length <= 63 ? input : input.Substring(0, 63);
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

        private void MainWindow_OnClosing(object sender, CancelEventArgs e)
        {
            var viewModel = DataContext as MainViewModel;
            if (viewModel == null || !viewModel.HasUnexportedData)
            {
                return;
            }

            var result = MessageBox.Show(
                this,
                "Hai dati di scansione non esportati. Vuoi chiudere comunque?",
                "Dati non esportati",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
            }
        }
    }
}
