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
using System.Windows.Input;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;
using NtfsAudit.App.Services;
using NtfsAudit.App.ViewModels;

namespace NtfsAudit.App
{
    public partial class MainWindow : Window
    {
        private readonly WinForms.NotifyIcon _notifyIcon;
        private readonly Icon _applicationIcon;
        private readonly DispatcherTimer _trayTimer;
        private MainViewModel _boundViewModel;
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
            AttachViewModel(DataContext as MainViewModel);
            DataContextChanged += MainWindow_OnDataContextChanged;
            _applicationIcon = LoadApplicationIcon();

            _notifyIcon = new WinForms.NotifyIcon
            {
                Icon = _applicationIcon,
                Visible = false,
                Text = LocalizationManager.Text("App.Title")
            };
            _notifyIcon.DoubleClick += (_, __) => RestoreFromTray();

            var trayMenu = new WinForms.ContextMenuStrip();
            trayMenu.Items.Add(LocalizationManager.Text("Tray.Open"), null, (_, __) => RestoreFromTray());
            trayMenu.Items.Add(LocalizationManager.Text("Tray.StopScan"), null, (_, __) =>
            {
                var currentViewModel = DataContext as MainViewModel;
                if (currentViewModel != null && currentViewModel.StopCommand.CanExecute(null))
                {
                    currentViewModel.StopCommand.Execute(null);
                }
            });
            trayMenu.Items.Add(LocalizationManager.Text("Tray.CleanCache"), null, (_, __) =>
            {
                var currentViewModel = DataContext as MainViewModel;
                if (currentViewModel != null && currentViewModel.CleanupResidualFilesCommand.CanExecute(null))
                {
                    currentViewModel.CleanupResidualFilesCommand.Execute(null);
                }
            });
            trayMenu.Items.Add(LocalizationManager.Text("Tray.Exit"), null, (_, __) =>
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
                DetachViewModel();
                _trayTimer.Stop();
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _applicationIcon.Dispose();
            };

            UpdateTrayStatus();
        }

        private void MainWindow_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            DetachViewModel();
            AttachViewModel(e.NewValue as MainViewModel);
        }

        private void AttachViewModel(MainViewModel viewModel)
        {
            _boundViewModel = viewModel;
            if (_boundViewModel != null)
            {
                _boundViewModel.PropertyChanged += ViewModel_OnPropertyChanged;
            }
        }

        private void DetachViewModel()
        {
            if (_boundViewModel != null)
            {
                _boundViewModel.PropertyChanged -= ViewModel_OnPropertyChanged;
                _boundViewModel = null;
            }
        }

        private void ViewModel_OnPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e == null || !string.Equals(e.PropertyName, "IsSettingsOpen", StringComparison.Ordinal))
            {
                return;
            }

            var viewModel = sender as MainViewModel;
            if (viewModel == null || viewModel.IsSettingsOpen)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (SettingsToggleButton.Visibility == Visibility.Visible && SettingsToggleButton.IsEnabled)
                {
                    Keyboard.Focus(SettingsToggleButton);
                }
            }), DispatcherPriority.Input);
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
                _notifyIcon.ShowBalloonTip(2500, LocalizationManager.Text("Tray.ServiceTitle"), status, WinForms.ToolTipIcon.Info);
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
            _notifyIcon.ShowBalloonTip(2500, LocalizationManager.Text("App.Title"), LocalizationManager.Text("Tray.Minimized"), WinForms.ToolTipIcon.Info);
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
                _notifyIcon.ShowBalloonTip(2500, LocalizationManager.Text("App.Title"), LocalizationManager.Text("Tray.ServiceActive"), WinForms.ToolTipIcon.Info);
            }
        }

        private static string TruncateForNotifyIcon(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return LocalizationManager.Text("App.Title");
            return input.Length <= 63 ? input : input.Substring(0, 63);
        }

        private static Icon LoadApplicationIcon()
        {
            var resourceInfo = Application.GetResourceStream(new Uri("pack://application:,,,/NtfsAudit.App;component/Assets/OnlyRights.ico", UriKind.Absolute));
            if (resourceInfo == null || resourceInfo.Stream == null)
            {
                return (Icon)SystemIcons.Application.Clone();
            }

            using (resourceInfo.Stream)
            {
                return new Icon(resourceInfo.Stream);
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
                LocalizationManager.Text("Dialog.UnexportedDataMessage"),
                LocalizationManager.Text("Dialog.UnexportedData"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
            {
                e.Cancel = true;
            }
        }

        private void MainWindow_OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            var viewModel = DataContext as MainViewModel;
            if (viewModel == null || !viewModel.IsSettingsOpen || e == null || e.Key != Key.Escape)
            {
                return;
            }

            viewModel.CloseSettingsCommand.Execute(null);
            e.Handled = true;
        }
    }
}
