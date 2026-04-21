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
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using NtfsAudit.App.ViewModels;

namespace NtfsAudit.App.Views
{
    public partial class SettingsPanel : UserControl
    {
        private MainViewModel _boundViewModel;
        private bool _isSynchronizingPasswords;

        public SettingsPanel()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
            Loaded += (_, __) => SyncCredentialPasswordBoxes();
            IsVisibleChanged += SettingsPanel_OnIsVisibleChanged;
            Unloaded += (_, __) => DetachViewModelHandlers();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            DetachViewModelHandlers();
            AttachViewModelHandlers(e.NewValue as MainViewModel);
            SyncCredentialPasswordBoxes();
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

            if (string.Equals(e.PropertyName, "GlobalCredentialPassword", StringComparison.Ordinal))
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
            }
            finally
            {
                _isSynchronizingPasswords = false;
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

        private void SettingsPanel_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!(e.NewValue is bool isVisible) || !isVisible || !IsLoaded)
            {
                return;
            }

            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (OutputDirectoryTextBox.IsVisible && OutputDirectoryTextBox.IsEnabled)
                {
                    Keyboard.Focus(OutputDirectoryTextBox);
                    OutputDirectoryTextBox.SelectAll();
                }
            }), DispatcherPriority.Input);
        }
    }
}
