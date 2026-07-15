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
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;

#nullable enable

namespace NtfsAudit.App.ViewModels
{
    public sealed class ResultHierarchyNodeViewModel : INotifyPropertyChanged
    {
        private readonly Func<Task<ResultHierarchyNodeViewModel[]>>? _loader;
        private bool _hasLoadedChildren;
        private bool _isExpanded;
        private bool _isLoading;

        public ResultHierarchyNodeViewModel(
            string title,
            string subtitle,
            string kindLabel,
            string kindBackground,
            string accentText = "",
            string accentBackground = "#FFCFD8DC",
            Func<Task<ResultHierarchyNodeViewModel[]>>? loader = null)
        {
            Title = title ?? string.Empty;
            Subtitle = subtitle ?? string.Empty;
            KindLabel = kindLabel ?? string.Empty;
            KindBackground = kindBackground ?? "#FF546E7A";
            AccentText = accentText ?? string.Empty;
            AccentBackground = accentBackground ?? "#FFCFD8DC";
            _loader = loader;
            Children = new ObservableCollection<ResultHierarchyNodeViewModel>();

            if (_loader != null)
            {
                Children.Add(new ResultHierarchyNodeViewModel("Loading", string.Empty, string.Empty, "#000000") { IsPlaceholder = true });
            }
        }

        public string Title { get; }
        public string Subtitle { get; }
        public string KindLabel { get; }
        public string KindBackground { get; }
        public string AccentText { get; }
        public string AccentBackground { get; }
        public bool IsPlaceholder { get; private set; }
        public ObservableCollection<ResultHierarchyNodeViewModel> Children { get; }
        public bool HasAccent
        {
            get { return !string.IsNullOrWhiteSpace(AccentText); }
        }

        public bool HasExplicitNtfs { get; set; }
        public bool HasDenyExplicit { get; set; }
        public bool HasFileEntries { get; set; }
        public bool IsProtected { get; set; }
        public bool HasDiff { get; set; }
        public bool HasBaselineMismatch { get; set; }

        public bool IsExpanded
        {
            get { return _isExpanded; }
            set
            {
                if (_isExpanded == value)
                {
                    return;
                }

                _isExpanded = value;
                OnPropertyChanged("IsExpanded");
                if (_isExpanded)
                {
                    EnsureChildrenLoaded();
                }
            }
        }

        public bool IsLoading
        {
            get { return _isLoading; }
            private set
            {
                if (_isLoading == value)
                {
                    return;
                }

                _isLoading = value;
                OnPropertyChanged("IsLoading");
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private async void EnsureChildrenLoaded()
        {
            if (_hasLoadedChildren || _loader == null)
            {
                return;
            }

            _hasLoadedChildren = true;
            IsLoading = true;
            try
            {
                var children = await _loader().ConfigureAwait(true);
                Children.Clear();
                foreach (var child in (children ?? Array.Empty<ResultHierarchyNodeViewModel>()).Where(node => node != null))
                {
                    Children.Add(child);
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
