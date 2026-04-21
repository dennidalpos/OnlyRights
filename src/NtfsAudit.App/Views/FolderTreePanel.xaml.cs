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
using System.Windows;
using System.Windows.Controls;
using NtfsAudit.App.ViewModels;

namespace NtfsAudit.App.Views
{
    public partial class FolderTreePanel : UserControl
    {
        public FolderTreePanel()
        {
            InitializeComponent();
        }

        private void TreeView_OnSelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            var viewModel = DataContext as MainViewModel;
            var node = e.NewValue as FolderNodeViewModel;
            if (viewModel != null && node != null)
            {
                viewModel.SelectFolder(node.SelectionPath);
            }
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
