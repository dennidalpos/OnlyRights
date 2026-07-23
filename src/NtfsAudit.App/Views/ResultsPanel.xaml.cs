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
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NtfsAudit.Core.Models;
using NtfsAudit.App.Services;
using NtfsAudit.Core.Services;
using NtfsAudit.App.ViewModels;

namespace NtfsAudit.App.Views
{
    public partial class ResultsPanel : UserControl
    {
        public ResultsPanel()
        {
            InitializeComponent();
        }

        private async void GroupEntries_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var entry = GetSelectedEntry(sender);
            if (entry == null) return;
            await ExecutePrincipalLookupAsync(() => ShowGroupMembers(entry), LocalizationManager.Text("Dialog.GroupDetails"));
        }

        private async void GroupEntries_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            var entry = GetSelectedEntry(sender);
            if (entry == null) return;
            e.Handled = true;
            await ExecutePrincipalLookupAsync(() => ShowGroupMembers(entry), LocalizationManager.Text("Dialog.GroupDetails"));
        }

        private async void UserEntries_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var entry = GetSelectedEntry(sender);
            if (entry == null) return;
            await ExecutePrincipalLookupAsync(() => ShowUserGroups(entry), LocalizationManager.Text("Dialog.UserDetails"));
        }

        private async void UserEntries_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
            {
                return;
            }

            var entry = GetSelectedEntry(sender);
            if (entry == null) return;
            e.Handled = true;
            await ExecutePrincipalLookupAsync(() => ShowUserGroups(entry), LocalizationManager.Text("Dialog.UserDetails"));
        }

        private async Task ExecutePrincipalLookupAsync(Func<Task> operation, string contextTitle)
        {
            try
            {
                await operation();
            }
            catch (Exception ex)
            {
                var message = LocalizationManager.Format("Dialog.LookupErrorMessage", ex.Message);
                var owner = Window.GetWindow(this);
                if (owner != null)
                {
                    MessageBox.Show(owner, message, contextTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show(message, contextTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private AceEntry GetSelectedEntry(object sender)
        {
            var grid = sender as DataGrid;
            return grid == null ? null : grid.SelectedItem as AceEntry;
        }

        private async Task ShowGroupMembers(AceEntry entry)
        {
            var owner = Window.GetWindow(this);
            if (string.IsNullOrWhiteSpace(entry.PrincipalSid))
            {
                if (owner != null)
                {
                    MessageBox.Show(owner, LocalizationManager.Text("Dialog.GroupSidUnavailable"), LocalizationManager.Text("Dialog.GroupDetails"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(LocalizationManager.Text("Dialog.GroupSidUnavailable"), LocalizationManager.Text("Dialog.GroupDetails"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            var viewModel = DataContext as MainViewModel;
            if (viewModel == null) return;
            var members = await viewModel.GetGroupMembersAsync(entry.PrincipalSid);
            var title = LocalizationManager.Format("Dialog.GroupMembersTitle", entry.PrincipalName);
            var window = new PrincipalDetailsWindow(title, members);
            if (owner != null)
            {
                window.Owner = owner;
            }

            window.ShowDialog();
        }

        private async Task ShowUserGroups(AceEntry entry)
        {
            var owner = Window.GetWindow(this);
            if (string.IsNullOrWhiteSpace(entry.PrincipalSid))
            {
                if (owner != null)
                {
                    MessageBox.Show(owner, LocalizationManager.Text("Dialog.UserSidUnavailable"), LocalizationManager.Text("Dialog.UserDetails"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(LocalizationManager.Text("Dialog.UserSidUnavailable"), LocalizationManager.Text("Dialog.UserDetails"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            var viewModel = DataContext as MainViewModel;
            if (viewModel == null) return;
            var groups = await viewModel.GetUserGroupsAsync(entry.PrincipalSid);
            var title = LocalizationManager.Format("Dialog.UserGroupsTitle", entry.PrincipalName);
            var window = new PrincipalDetailsWindow(title, groups);
            if (owner != null)
            {
                window.Owner = owner;
            }

            window.ShowDialog();
        }
    }
}
