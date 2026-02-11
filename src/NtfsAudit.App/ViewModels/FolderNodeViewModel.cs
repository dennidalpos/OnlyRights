using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using NtfsAudit.App.Models;
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
        private FolderStatus _status;
        private string _statusTooltip;
        private ObservableCollection<string> _topReasons;

        public FolderNodeViewModel(
            string path,
            string displayName,
            FolderTreeProvider treeProvider,
            bool hasExplicitPermissions,
            bool isInheritanceDisabled,
            int explicitAddedCount,
            int explicitRemovedCount,
            int denyExplicitCount,
            bool isProtected,
            int baselineAddedCount,
            int baselineRemovedCount,
            bool hasExplicitNtfs,
            bool hasExplicitShare,
            bool hasHighRisk,
            bool hasMediumRisk,
            bool hasLowRisk,
            FolderExplanation explanation)
        {
            Path = path;
            DisplayName = displayName;
            _treeProvider = treeProvider;
            HasExplicitPermissions = hasExplicitPermissions;
            IsInheritanceDisabled = isInheritanceDisabled;
            ExplicitAddedCount = explicitAddedCount;
            ExplicitRemovedCount = explicitRemovedCount;
            DenyExplicitCount = denyExplicitCount;
            IsProtected = isProtected;
            BaselineAddedCount = baselineAddedCount;
            BaselineRemovedCount = baselineRemovedCount;
            HasExplicitNtfs = hasExplicitNtfs;
            HasExplicitShare = hasExplicitShare;
            HasHighRisk = hasHighRisk;
            HasMediumRisk = hasMediumRisk;
            HasLowRisk = hasLowRisk;
            ApplyExplanation(explanation);
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
            _status = FolderStatus.Same;
            _statusTooltip = string.Empty;
            _topReasons = new ObservableCollection<string>();
            Children = new ObservableCollection<FolderNodeViewModel>();
        }

        public string Path { get; private set; }
        public string DisplayName { get; private set; }
        public ObservableCollection<FolderNodeViewModel> Children { get; private set; }
        public bool HasExplicitPermissions { get; private set; }
        public bool IsInheritanceDisabled { get; private set; }
        public int ExplicitAddedCount { get; private set; }
        public int ExplicitRemovedCount { get; private set; }
        public int DenyExplicitCount { get; private set; }
        public bool IsProtected { get; private set; }
        public int BaselineAddedCount { get; private set; }
        public int BaselineRemovedCount { get; private set; }
        public bool HasExplicitNtfs { get; private set; }
        public bool HasExplicitShare { get; private set; }
        public bool HasHighRisk { get; private set; }
        public bool HasMediumRisk { get; private set; }
        public bool HasLowRisk { get; private set; }
        public bool HasExplicitAdded { get { return ExplicitAddedCount > 0; } }
        public bool HasExplicitRemoved { get { return ExplicitRemovedCount > 0; } }
        public bool HasDenyExplicit { get { return DenyExplicitCount > 0; } }
        public bool HasBaselineAdded { get { return BaselineAddedCount > 0; } }
        public bool HasBaselineRemoved { get { return BaselineRemovedCount > 0; } }
        public string ExplicitAddedLabel { get { return string.Format("A+{0}", ExplicitAddedCount); } }
        public string ExplicitRemovedLabel { get { return string.Format("R-{0}", ExplicitRemovedCount); } }
        public string DenyExplicitLabel { get { return "D"; } }
        public string BaselineAddedLabel { get { return string.Format("B+{0}", BaselineAddedCount); } }
        public string BaselineRemovedLabel { get { return string.Format("B-{0}", BaselineRemovedCount); } }
        public string ExplicitNtfsLabel { get { return "N"; } }
        public string ExplicitShareLabel { get { return "S"; } }
        public FolderStatus Status { get { return _status; } }
        public ObservableCollection<string> TopReasons { get { return _topReasons; } }
        public bool IsDifferentFromParent { get { return _status != FolderStatus.Same; } }
        public string StatusTooltip { get { return _statusTooltip; } }
        public string StatusBadgeText
        {
            get
            {
                switch (_status)
                {
                    case FolderStatus.MorePermissive:
                        return "↑";
                    case FolderStatus.MoreRestrictive:
                        return "↓";
                    case FolderStatus.BrokenInheritance:
                        return "BRK";
                    case FolderStatus.DenyPresent:
                        return "DENY";
                    case FolderStatus.Unknown:
                        return "UNK";
                    default:
                        return "=";
                }
            }
        }

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

        private void ApplyExplanation(FolderExplanation explanation)
        {
            if (explanation == null)
            {
                _status = FolderStatus.Same;
                _topReasons = new ObservableCollection<string>();
                _statusTooltip = "Uguale al padre";
                return;
            }

            _status = explanation.Status;
            _topReasons = new ObservableCollection<string>((explanation.Reasons ?? new System.Collections.Generic.List<string>()).Take(3));
            _statusTooltip = string.IsNullOrWhiteSpace(explanation.Summary)
                ? "Stato permessi"
                : string.Format("{0}\n{1}", explanation.Summary, string.Join("\n", _topReasons));
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
