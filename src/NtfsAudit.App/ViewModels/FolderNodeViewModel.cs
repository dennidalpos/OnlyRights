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

        public FolderNodeViewModel(
            string path,
            string displayName,
            FolderTreeProvider treeProvider,
            bool hasExplicitPermissions,
            bool isInheritanceDisabled,
            int explicitCount,
            int explicitAddedCount,
            int explicitRemovedCount,
            int denyExplicitCount,
            int modifiedCount,
            bool isProtected)
        {
            Path = path;
            DisplayName = displayName;
            _treeProvider = treeProvider;
            HasExplicitPermissions = hasExplicitPermissions;
            IsInheritanceDisabled = isInheritanceDisabled;
            ExplicitCount = explicitCount;
            ExplicitAddedCount = explicitAddedCount;
            ExplicitRemovedCount = explicitRemovedCount;
            DenyExplicitCount = denyExplicitCount;
            ModifiedCount = modifiedCount;
            IsProtected = isProtected;
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
        public int ExplicitCount { get; private set; }
        public int ExplicitAddedCount { get; private set; }
        public int ExplicitRemovedCount { get; private set; }
        public int DenyExplicitCount { get; private set; }
        public int ModifiedCount { get; private set; }
        public bool IsProtected { get; private set; }
        public bool HasExplicitAce { get { return ExplicitCount > 0; } }
        public bool HasExplicitAdded { get { return ExplicitAddedCount > 0; } }
        public bool HasExplicitRemoved { get { return ExplicitRemovedCount > 0; } }
        public bool HasDenyExplicit { get { return DenyExplicitCount > 0; } }
        public bool HasModified { get { return ModifiedCount > 0; } }
        public string ExplicitCountLabel { get { return string.Format("E{0}", ExplicitCount); } }
        public string ExplicitAddedLabel { get { return string.Format("+{0}", ExplicitAddedCount); } }
        public string ExplicitRemovedLabel { get { return string.Format("-{0}", ExplicitRemovedCount); } }
        public string DenyExplicitLabel { get { return "D"; } }
        public string ModifiedLabel { get { return "M"; } }

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

        public void ExpandAll()
        {
            if (_isPlaceholder) return;
            if (!_childrenLoaded)
            {
                LoadChildren();
            }
            IsExpanded = true;
            foreach (var child in Children)
            {
                child.ExpandAll();
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
