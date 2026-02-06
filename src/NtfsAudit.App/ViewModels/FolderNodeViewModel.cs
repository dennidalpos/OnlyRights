using System.Collections.ObjectModel;
using System.ComponentModel;
using NtfsAudit.App.Services;

namespace NtfsAudit.App.ViewModels
{
    public class FolderNodeViewModel : INotifyPropertyChanged
    {
        private readonly FolderTreeProvider _treeProvider;
        private bool _isExpanded;
        private bool _childrenLoaded;
        private bool _isSelected;

        public FolderNodeViewModel(string path, string displayName, FolderTreeProvider treeProvider)
        {
            Path = path;
            DisplayName = displayName;
            _treeProvider = treeProvider;
            Children = new ObservableCollection<FolderNodeViewModel>();
        }

        public string Path { get; private set; }
        public string DisplayName { get; private set; }
        public ObservableCollection<FolderNodeViewModel> Children { get; private set; }

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

        private void LoadChildren()
        {
            _childrenLoaded = true;
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
