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
using System.Windows;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;
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
            trayMenu.Items.Add("Ferma scansione / job", null, (_, __) =>
            {
                var currentViewModel = DataContext as MainViewModel;
                if (currentViewModel != null && currentViewModel.StopCommand.CanExecute(null))
                {
                    currentViewModel.StopCommand.Execute(null);
                }
            });
            trayMenu.Items.Add("Pulisci cache/residui", null, (_, __) =>
            {
                var currentViewModel = DataContext as MainViewModel;
                if (currentViewModel != null && currentViewModel.CleanupResidualFilesCommand.CanExecute(null))
                {
                    currentViewModel.CleanupResidualFilesCommand.Execute(null);
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
                _notifyIcon.ShowBalloonTip(2500, "NTFS Audit", "Servizio attivo: l'app resta nel tray finché la scansione è in corso.", WinForms.ToolTipIcon.Info);
            }
        }

        private static string TruncateForNotifyIcon(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return "NTFS Audit";
            return input.Length <= 63 ? input : input.Substring(0, 63);
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
