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
        private MainViewModel _boundViewModel;
        private string _lastTrayStatus;
        private bool _forceClose;
        private bool _isSynchronizingPasswords;
        public MainWindow()
            : this(new MainViewModel())
        {
        }

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel ?? new MainViewModel();
            AttachViewModelHandlers(DataContext as MainViewModel);
            SyncCredentialPasswordBoxes();

            _notifyIcon = new WinForms.NotifyIcon
            {
                Icon = SystemIcons.Information,
                Visible = false,
                Text = "NTFS Audit"
            };
            _notifyIcon.DoubleClick += (_, __) => RestoreFromTray();

            var trayMenu = new WinForms.ContextMenuStrip();
            trayMenu.Items.Add("Apri", null, (_, __) => RestoreFromTray());
            trayMenu.Items.Add("Ferma scansione / job", null, (_, __) =>
            {
                var viewModel = DataContext as MainViewModel;
                if (viewModel != null && viewModel.StopCommand.CanExecute(null))
                {
                    viewModel.StopCommand.Execute(null);
                }
            });
            trayMenu.Items.Add("Pulisci cache/residui", null, (_, __) =>
            {
                var viewModel = DataContext as MainViewModel;
                if (viewModel != null && viewModel.CleanupResidualFilesCommand.CanExecute(null))
                {
                    viewModel.CleanupResidualFilesCommand.Execute(null);
                }
            });
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
                DetachViewModelHandlers();
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

            if (WindowState == WindowState.Minimized
                && viewModel.IsServiceRuntimeRunning
                && !string.IsNullOrWhiteSpace(status)
                && !string.Equals(_lastTrayStatus, status, StringComparison.OrdinalIgnoreCase))
            {
                _notifyIcon.ShowBalloonTip(2500, "NTFS Audit - servizio", status, WinForms.ToolTipIcon.Info);
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
            _notifyIcon.ShowBalloonTip(2500, "NTFS Audit", "App ridotta in tray. Il servizio continua in background.", WinForms.ToolTipIcon.Info);
        }

        private void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            Activate();
            UpdateTrayStatus();
        }

        private void AttachViewModelHandlers(MainViewModel viewModel)
        {
            _boundViewModel = viewModel;
            if (_boundViewModel != null)
            {
                _boundViewModel.PropertyChanged += OnViewModelPropertyChanged;
            }
        }

        private void DetachViewModelHandlers()
        {
            if (_boundViewModel != null)
            {
                _boundViewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _boundViewModel = null;
            }
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e == null)
            {
                return;
            }

            if (string.Equals(e.PropertyName, "GlobalCredentialPassword", StringComparison.Ordinal)
                || string.Equals(e.PropertyName, "SelectedScanRootCredentialPassword", StringComparison.Ordinal)
                || string.Equals(e.PropertyName, "SelectedScanRoot", StringComparison.Ordinal))
            {
                SyncCredentialPasswordBoxes();
            }
        }

        private void SyncCredentialPasswordBoxes()
        {
            var viewModel = DataContext as MainViewModel;
            if (viewModel == null)
            {
                return;
            }

            try
            {
                _isSynchronizingPasswords = true;
                GlobalCredentialPasswordBox.Password = viewModel.GlobalCredentialPassword ?? string.Empty;
                SelectedScanRootCredentialPasswordBox.Password = viewModel.SelectedScanRootCredentialPassword ?? string.Empty;
            }
            finally
            {
                _isSynchronizingPasswords = false;
            }
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
                _notifyIcon.ShowBalloonTip(2500, "NTFS Audit", "Servizio attivo: l'app resta nel tray finchÃ© la scansione Ã¨ in corso.", WinForms.ToolTipIcon.Info);
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
            await ExecutePrincipalLookupAsync(() => ShowGroupMembers(entry), "Dettagli gruppo");
        }

        private async void UserEntries_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var entry = GetSelectedEntry(sender);
            if (entry == null) return;
            await ExecutePrincipalLookupAsync(() => ShowUserGroups(entry), "Dettagli utente");
        }

        private async Task ExecutePrincipalLookupAsync(Func<Task> operation, string contextTitle)
        {
            try
            {
                await operation();
            }
            catch (Exception ex)
            {
                var message = string.Format("Impossibile caricare i dettagli richiesti. Verifica connettivitÃ  AD/permesse e riprova.\n\nDettagli: {0}", ex.Message);
                MessageBox.Show(this, message, contextTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
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

        private void GlobalCredentialPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isSynchronizingPasswords)
            {
                return;
            }

            var viewModel = DataContext as MainViewModel;
            if (viewModel != null)
            {
                viewModel.GlobalCredentialPassword = GlobalCredentialPasswordBox.Password;
            }
        }

        private void SelectedScanRootCredentialPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
        {
            if (_isSynchronizingPasswords)
            {
                return;
            }

            var viewModel = DataContext as MainViewModel;
            if (viewModel != null)
            {
                viewModel.SelectedScanRootCredentialPassword = SelectedScanRootCredentialPasswordBox.Password;
            }
        }
    }
}
