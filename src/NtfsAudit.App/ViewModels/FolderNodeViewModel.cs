using System.Collections.ObjectModel;
using System.ComponentModel;
using NtfsAudit.App.Services;

namespace NtfsAudit.App.ViewModels
{
    public class FolderNodeViewModel : INotifyPropertyChanged
    {
        private static readonly FolderNodeViewModel Placeholder = new FolderNodeViewModel();
        private readonly FolderTreeProvider _treeProvider;
        private bool _isExpanded;
        private bool _childrenLoaded;
        private bool _isSelected;
        private readonly bool _isPlaceholder;

        public FolderNodeViewModel(string path, string displayName, FolderTreeProvider treeProvider, bool hasExplicitPermissions, bool isInheritanceDisabled)
        {
            Path = path;
            DisplayName = displayName;
            _treeProvider = treeProvider;
            HasExplicitPermissions = hasExplicitPermissions;
            IsInheritanceDisabled = isInheritanceDisabled;
            Children = new ObservableCollection<FolderNodeViewModel>();
            if (_treeProvider != null && _treeProvider.HasChildren(path))
            {
                Children.Add(Placeholder);
            }
        }

        private FolderNodeViewModel()
        {
            Path = string.Empty;
            DisplayName = string.Empty;
            _isPlaceholder = true;
            Children = new ObservableCollection<FolderNodeViewModel>();
        }

        public string Path { get; private set; }
        public string DisplayName { get; private set; }
        public ObservableCollection<FolderNodeViewModel> Children { get; private set; }
        public bool HasExplicitPermissions { get; private set; }
        public bool IsInheritanceDisabled { get; private set; }

        public bool IsExpanded
        {
            get { return _isExpanded; }
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                OnPropertyChanged("IsExpanded");
                if (_isExpanded && !_childrenLoaded)
                {
                    LoadChildren();
                }
            }
        }

        public bool IsSelected
        {
            get { return _isSelected; }
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                OnPropertyChanged("IsSelected");
            }
        }

        public bool IsPlaceholder
        {
            get { return _isPlaceholder; }
        }

        private void LoadChildren()
        {
            _childrenLoaded = true;
            if (Children.Count == 1 && Children[0]._isPlaceholder)
            {
                Children.Clear();
            }
            foreach (var child in _treeProvider.GetChildren(Path))
            {
                Children.Add(child);
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
