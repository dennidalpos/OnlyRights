using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Forms;
using WinForms = System.Windows.Forms;
using System.Windows.Threading;
using Win32 = Microsoft.Win32;
using Newtonsoft.Json;
using WpfMessageBox = System.Windows.MessageBox;
using NtfsAudit.App.Cache;
using NtfsAudit.App.Export;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;

namespace NtfsAudit.App.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        private readonly LocalCacheStore _cacheStore;
        private readonly SidNameCache _sidNameCache;
        private readonly GroupMembershipCache _groupMembershipCache;
        private readonly ExcelExporter _excelExporter;
        private readonly AnalysisArchive _analysisArchive;
        private ScanResult _scanResult;
        private CancellationTokenSource _cts;
        private bool _isScanning;
        private bool _isBusy;
        private bool _isViewerMode;
        private DispatcherTimer _scanTimer;
        private DateTime _scanStart;
        private bool _hasExported;
        private string _rootPath;
        private string _selectedScanRoot;
        private readonly Dictionary<string, string> _scanRootDfsTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _scanRootNamespacePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private ObservableCollection<string> _selectedScanRootDfsTargets = new ObservableCollection<string>();
        private string _selectedScanRootDfsTarget;
        private string _auditOutputDirectory;
        private bool _useWindowsServiceMode;
        private int _maxDepth = 5;
        private bool _scanAllDepths = true;
        private bool _includeInherited = true;
        private bool _resolveIdentities = true;
        private bool _excludeServiceAccounts;
        private bool _excludeAdminAccounts;
        private bool _expandGroups = true;
        private bool _usePowerShell = true;
        private bool _enableAdvancedAudit = true;
        private bool _computeEffectiveAccess = true;
        private bool _includeSharePermissions = true;
        private bool _includeFiles;
        private bool _readOwnerAndSacl = true;
        private bool _compareBaseline = true;
        private string _progressText = "Pronto";
        private string _currentPathText;
        private int _processedCount;
        private int _processedFilesCount;
        private int _errorCount;
        private string _elapsedText = "00:00:00";
        private string _selectedFolderPath;
        private string _selectedFolderName;
        private string _aclFilter;
        private bool _colorizeRights = true;
        private ObservableCollection<string> _dfsTargets = new ObservableCollection<string>();
        private string _selectedDfsTarget;
        private bool _showAllow = true;
        private bool _showDeny = true;
        private bool _showInherited = true;
        private bool _showExplicit = true;
        private bool _showProtected = true;
        private bool _showDisabled = true;
        private bool _showEveryone = true;
        private bool _showAuthenticatedUsers = true;
        private bool _showServiceAccounts = true;
        private bool _showAdminAccounts = true;
        private bool _showOtherPrincipals = true;
        private string _currentPathBackground = "Transparent";
        private int _summaryTotalEntries;
        private int _summaryHighRisk;
        private int _summaryMediumRisk;
        private int _summaryLowRisk;
        private int _summaryDenyCount;
        private int _summaryEveryoneCount;
        private int _summaryAuthUsersCount;
        private int _summaryFilesCount;
        private int _summaryBaselineAdded;
        private int _summaryBaselineRemoved;
        private bool _isElevated;
        private string _lastExportDirectory;
        private string _lastImportDirectory;
        private string _uiPreferencesPath;
        private Dictionary<string, List<string>> _fullTreeMap;
        private Dictionary<string, List<string>> _currentFilteredTreeMap;
        private bool _treeFilterExplicitOnly;
        private bool _treeFilterInheritanceDisabledOnly;
        private bool _treeFilterDiffOnly;
        private bool _treeFilterExplicitDenyOnly;
        private bool _treeFilterBaselineMismatchOnly;
        private bool _treeFilterFilesOnly = true;
        private bool _treeFilterFoldersOnly = true;
        private string _selectedPathKind = "Unknown";
        private string _selectedOwnerSummary = "-";
        private string _selectedInheritanceSummary = "-";
        private int _selectedTotalAceCount;
        private int _selectedExplicitAceCount;
        private int _selectedInheritedAceCount;
        private int _selectedDenyAceCount;
        private string _selectedPermissionLayers = "-";
        private string _selectedRiskSummary = "-";
        private string _selectedAcquisitionWarnings = "-";
        private string _selectedScannedAtText = "-";
        private const string ServiceName = "NtfsAuditWorker";

        public MainViewModel(bool viewerMode = false)
        {
            _isViewerMode = viewerMode;
            _cacheStore = new LocalCacheStore();
            _sidNameCache = new SidNameCache();
            _groupMembershipCache = new GroupMembershipCache(TimeSpan.FromHours(2));
            _excelExporter = new ExcelExporter();
            _analysisArchive = new AnalysisArchive();

            FolderTree = new ObservableCollection<FolderNodeViewModel>();
            ScanRoots = new ObservableCollection<string>();
            GroupEntries = new ObservableCollection<AceEntry>();
            UserEntries = new ObservableCollection<AceEntry>();
            AllEntries = new ObservableCollection<AceEntry>();
            ShareEntries = new ObservableCollection<AceEntry>();
            EffectiveEntries = new ObservableCollection<AceEntry>();
            Errors = new ObservableCollection<ErrorEntry>();
            Errors.CollectionChanged += (_, __) => ErrorCount = Errors.Count;
            FilteredErrors = CollectionViewSource.GetDefaultView(Errors);
            FilteredErrors.Filter = FilterErrors;
            FilteredGroupEntries = CollectionViewSource.GetDefaultView(GroupEntries);
            FilteredGroupEntries.Filter = FilterAclEntries;
            FilteredUserEntries = CollectionViewSource.GetDefaultView(UserEntries);
            FilteredUserEntries.Filter = FilterAclEntries;
            FilteredAllEntries = CollectionViewSource.GetDefaultView(AllEntries);
            FilteredAllEntries.Filter = FilterAclEntries;
            FilteredShareEntries = CollectionViewSource.GetDefaultView(ShareEntries);
            FilteredShareEntries.Filter = FilterAclEntries;
            FilteredEffectiveEntries = CollectionViewSource.GetDefaultView(EffectiveEntries);
            FilteredEffectiveEntries.Filter = FilterAclEntries;

            _isElevated = IsProcessElevated();

            BrowseCommand = new RelayCommand(Browse);
            AddScanRootCommand = new RelayCommand(AddScanRoot, () => !_isViewerMode && !string.IsNullOrWhiteSpace(RootPath));
            RemoveScanRootCommand = new RelayCommand(RemoveScanRoot, () => !_isViewerMode && !string.IsNullOrWhiteSpace(SelectedScanRoot));
            BrowseOutputDirectoryCommand = new RelayCommand(BrowseOutputDirectory);
            InstallServiceCommand = new RelayCommand(InstallService, () => !_isViewerMode && !IsBusy);
            UninstallServiceCommand = new RelayCommand(UninstallService, () => !_isViewerMode && !IsBusy);
            StartCommand = new RelayCommand(StartScan, () => CanStart);
            StopCommand = new RelayCommand(StopScan, () => CanStop);
            ExportCommand = new RelayCommand(Export, () => CanExport);
            ExportAnalysisCommand = new RelayCommand(ExportAnalysis, () => CanExport);
            ImportAnalysisCommand = new RelayCommand(ImportAnalysis, () => !_isScanning && !IsBusy);
            ResetTreeFiltersCommand = new RelayCommand(ResetTreeFilters, () => HasScanResult);

            LoadCache();
            InitializeScanTimer();
        }

        public bool IsElevated
        {
            get { return _isElevated; }
        }

        public bool IsNotElevated
        {
            get { return !_isElevated; }
        }

        public ObservableCollection<FolderNodeViewModel> FolderTree { get; private set; }
        public ObservableCollection<string> ScanRoots { get; private set; }
        public ObservableCollection<AceEntry> GroupEntries { get; private set; }
        public ObservableCollection<AceEntry> UserEntries { get; private set; }
        public ObservableCollection<AceEntry> AllEntries { get; private set; }
        public ObservableCollection<AceEntry> ShareEntries { get; private set; }
        public ObservableCollection<AceEntry> EffectiveEntries { get; private set; }
        public ObservableCollection<ErrorEntry> Errors { get; private set; }
        public ICollectionView FilteredErrors { get; private set; }
        public ICollectionView FilteredGroupEntries { get; private set; }
        public ICollectionView FilteredUserEntries { get; private set; }
        public ICollectionView FilteredAllEntries { get; private set; }
        public ICollectionView FilteredShareEntries { get; private set; }
        public ICollectionView FilteredEffectiveEntries { get; private set; }

        public RelayCommand BrowseCommand { get; private set; }
        public RelayCommand AddScanRootCommand { get; private set; }
        public RelayCommand RemoveScanRootCommand { get; private set; }
        public RelayCommand BrowseOutputDirectoryCommand { get; private set; }
        public RelayCommand InstallServiceCommand { get; private set; }
        public RelayCommand UninstallServiceCommand { get; private set; }
        public RelayCommand StartCommand { get; private set; }
        public RelayCommand StopCommand { get; private set; }
        public RelayCommand ExportCommand { get; private set; }
        public RelayCommand ExportAnalysisCommand { get; private set; }
        public RelayCommand ImportAnalysisCommand { get; private set; }
        public RelayCommand ResetTreeFiltersCommand { get; private set; }

        public string RootPath
        {
            get { return _rootPath; }
            set
            {
                _rootPath = value;
                OnPropertyChanged("RootPath");
                UpdateDfsTargets();
                OnPropertyChanged("CanStart");
                AddScanRootCommand.RaiseCanExecuteChanged();
                StartCommand.RaiseCanExecuteChanged();
            }
        }

        public string SelectedScanRoot
        {
            get { return _selectedScanRoot; }
            set
            {
                _selectedScanRoot = value;
                OnPropertyChanged("SelectedScanRoot");
                LoadSelectedScanRootDfsTargets();
                RemoveScanRootCommand.RaiseCanExecuteChanged();
            }
        }

        public ObservableCollection<string> SelectedScanRootDfsTargets
        {
            get { return _selectedScanRootDfsTargets; }
            private set
            {
                _selectedScanRootDfsTargets = value;
                OnPropertyChanged("SelectedScanRootDfsTargets");
                OnPropertyChanged("HasSelectedScanRootDfsTargets");
            }
        }

        public bool HasSelectedScanRootDfsTargets
        {
            get { return SelectedScanRootDfsTargets != null && SelectedScanRootDfsTargets.Count > 0; }
        }

        public string SelectedScanRootDfsTarget
        {
            get { return _selectedScanRootDfsTarget; }
            set
            {
                _selectedScanRootDfsTarget = value;
                if (!string.IsNullOrWhiteSpace(SelectedScanRoot))
                {
                    var key = GetScanRootKey(SelectedScanRoot);
                    string namespacePath;
                    if (_scanRootNamespacePaths.TryGetValue(key, out namespacePath) && !string.IsNullOrWhiteSpace(value))
                    {
                        var newKey = GetScanRootKey(value);
                        var index = ScanRoots.IndexOf(SelectedScanRoot);
                        if (index >= 0 && !string.Equals(newKey, key, StringComparison.OrdinalIgnoreCase))
                        {
                            var duplicate = ScanRoots
                                .Where((_, idx) => idx != index)
                                .Any(path => string.Equals(GetScanRootKey(path), newKey, StringComparison.OrdinalIgnoreCase));
                            if (duplicate)
                            {
                                WpfMessageBox.Show("Il target DFS selezionato è già presente in elenco.", "Target DFS", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                                return;
                            }

                            ScanRoots[index] = value;
                            _scanRootNamespacePaths.Remove(key);
                            _scanRootNamespacePaths[newKey] = namespacePath;
                            _scanRootDfsTargets.Remove(key);
                            _scanRootDfsTargets[newKey] = value;
                            _selectedScanRoot = value;
                            OnPropertyChanged("SelectedScanRoot");
                            LoadSelectedScanRootDfsTargets();
                            OnPropertyChanged("SelectedScanRootDfsTarget");
                            OnPropertyChanged("CanStart");
                            StartCommand.RaiseCanExecuteChanged();
                            return;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(value))
                    {
                        _scanRootDfsTargets.Remove(key);
                    }
                    else
                    {
                        _scanRootDfsTargets[key] = value;
                    }
                }
                OnPropertyChanged("SelectedScanRootDfsTarget");
            }
        }

        public string AuditOutputDirectory
        {
            get { return _auditOutputDirectory; }
            set
            {
                _auditOutputDirectory = value;
                OnPropertyChanged("AuditOutputDirectory");
            }
        }

        public bool UseWindowsServiceMode
        {
            get { return _useWindowsServiceMode; }
            set
            {
                _useWindowsServiceMode = value;
                OnPropertyChanged("UseWindowsServiceMode");
            }
        }

        public int MaxDepth
        {
            get { return _maxDepth; }
            set
            {
                _maxDepth = ClampMaxDepth(value);
                OnPropertyChanged("MaxDepth");
            }
        }

        public bool ScanAllDepths
        {
            get { return _scanAllDepths; }
            set
            {
                _scanAllDepths = value;
                if (!_scanAllDepths)
                {
                    MaxDepth = ClampMaxDepth(_maxDepth);
                }
                OnPropertyChanged("ScanAllDepths");
                OnPropertyChanged("IsMaxDepthEnabled");
            }
        }

        public bool IsMaxDepthEnabled
        {
            get { return !_scanAllDepths; }
        }

        public bool IncludeInherited
        {
            get { return _includeInherited; }
            set
            {
                _includeInherited = value;
                OnPropertyChanged("IncludeInherited");
            }
        }

        public bool ResolveIdentities
        {
            get { return _resolveIdentities; }
            set
            {
                _resolveIdentities = value;
                OnPropertyChanged("ResolveIdentities");
                OnPropertyChanged("IsExpandGroupsEnabled");
                OnPropertyChanged("IsIdentityOptionsEnabled");
                ApplyIdentityDependencies();
            }
        }

        public bool IsExpandGroupsEnabled
        {
            get { return _resolveIdentities; }
        }

        public bool IsIdentityOptionsEnabled
        {
            get { return _resolveIdentities; }
        }

        public bool ExcludeServiceAccounts
        {
            get { return _excludeServiceAccounts; }
            set
            {
                _excludeServiceAccounts = value;
                OnPropertyChanged("ExcludeServiceAccounts");
            }
        }

        public bool ExcludeAdminAccounts
        {
            get { return _excludeAdminAccounts; }
            set
            {
                _excludeAdminAccounts = value;
                OnPropertyChanged("ExcludeAdminAccounts");
            }
        }

        public bool ExpandGroups
        {
            get { return _expandGroups; }
            set
            {
                _expandGroups = value;
                OnPropertyChanged("ExpandGroups");
            }
        }

        public bool UsePowerShell
        {
            get { return _usePowerShell; }
            set
            {
                _usePowerShell = value;
                OnPropertyChanged("UsePowerShell");
            }
        }

        public bool EnableAdvancedAudit
        {
            get { return _enableAdvancedAudit; }
            set
            {
                _enableAdvancedAudit = value;
                OnPropertyChanged("EnableAdvancedAudit");
                OnPropertyChanged("IsAdvancedAuditEnabled");
                ApplyAdvancedAuditDependencies();
            }
        }

        public bool IsAdvancedAuditEnabled
        {
            get { return _enableAdvancedAudit; }
        }

        public bool ComputeEffectiveAccess
        {
            get { return _computeEffectiveAccess; }
            set
            {
                _computeEffectiveAccess = value;
                OnPropertyChanged("ComputeEffectiveAccess");
            }
        }

        public bool IncludeSharePermissions
        {
            get { return _includeSharePermissions; }
            set
            {
                _includeSharePermissions = value;
                OnPropertyChanged("IncludeSharePermissions");
            }
        }

        public bool IncludeFiles
        {
            get { return _includeFiles; }
            set
            {
                _includeFiles = value;
                OnPropertyChanged("IncludeFiles");
            }
        }

        public bool ReadOwnerAndSacl
        {
            get { return _readOwnerAndSacl; }
            set
            {
                _readOwnerAndSacl = value;
                OnPropertyChanged("ReadOwnerAndSacl");
            }
        }

        public bool CompareBaseline
        {
            get { return _compareBaseline; }
            set
            {
                _compareBaseline = value;
                OnPropertyChanged("CompareBaseline");
            }
        }

        public string ProgressText
        {
            get { return _progressText; }
            set
            {
                _progressText = value;
                OnPropertyChanged("ProgressText");
            }
        }

        public string CurrentPathText
        {
            get { return _currentPathText; }
            set
            {
                _currentPathText = value;
                OnPropertyChanged("CurrentPathText");
            }
        }

        public int ProcessedCount
        {
            get { return _processedCount; }
            set
            {
                _processedCount = value;
                OnPropertyChanged("ProcessedCount");
            }
        }

        public int ProcessedFilesCount
        {
            get { return _processedFilesCount; }
            set
            {
                _processedFilesCount = value;
                OnPropertyChanged("ProcessedFilesCount");
            }
        }

        public int ErrorCount
        {
            get { return _errorCount; }
            set
            {
                _errorCount = value;
                OnPropertyChanged("ErrorCount");
            }
        }

        public string ElapsedText
        {
            get { return _elapsedText; }
            set
            {
                _elapsedText = value;
                OnPropertyChanged("ElapsedText");
            }
        }

        public string CurrentPathBackground
        {
            get { return _currentPathBackground; }
            set
            {
                _currentPathBackground = value;
                OnPropertyChanged("CurrentPathBackground");
            }
        }

        public string SelectedFolderPath
        {
            get { return _selectedFolderPath; }
            set
            {
                _selectedFolderPath = value;
                OnPropertyChanged("SelectedFolderPath");
                SelectedFolderName = GetFolderName(_selectedFolderPath);
            }
        }

        public string SelectedFolderName
        {
            get { return _selectedFolderName; }
            private set
            {
                _selectedFolderName = value;
                OnPropertyChanged("SelectedFolderName");
            }
        }

        public string AclFilter
        {
            get { return _aclFilter; }
            set
            {
                _aclFilter = value;
                OnPropertyChanged("AclFilter");
                RefreshAclFilters();
            }
        }

        public bool ColorizeRights
        {
            get { return _colorizeRights; }
            set
            {
                _colorizeRights = value;
                OnPropertyChanged("ColorizeRights");
            }
        }

        public ObservableCollection<string> DfsTargets
        {
            get { return _dfsTargets; }
            private set
            {
                _dfsTargets = value ?? new ObservableCollection<string>();
                OnPropertyChanged("DfsTargets");
                OnPropertyChanged("HasDfsTargets");
                OnPropertyChanged("DfsTargetBackground");
            }
        }

        public bool HasDfsTargets
        {
            get { return _dfsTargets != null && _dfsTargets.Count > 0; }
        }

        public string DfsTargetBackground
        {
            get { return HasDfsTargets ? "#FFE3F2FD" : "White"; }
        }

        public string SelectedDfsTarget
        {
            get { return _selectedDfsTarget; }
            set
            {
                _selectedDfsTarget = value;
                OnPropertyChanged("SelectedDfsTarget");
            }
        }

        public bool ShowAllow
        {
            get { return _showAllow; }
            set
            {
                _showAllow = value;
                if (!_showAllow && !_showDeny)
                {
                    _showDeny = true;
                    OnPropertyChanged("ShowDeny");
                }
                OnPropertyChanged("ShowAllow");
                RefreshAclFilters();
            }
        }

        public bool ShowDeny
        {
            get { return _showDeny; }
            set
            {
                _showDeny = value;
                if (!_showDeny && !_showAllow)
                {
                    _showAllow = true;
                    OnPropertyChanged("ShowAllow");
                }
                OnPropertyChanged("ShowDeny");
                RefreshAclFilters();
            }
        }

        public bool ShowInherited
        {
            get { return _showInherited; }
            set
            {
                _showInherited = value;
                if (!_showInherited && !_showExplicit)
                {
                    _showExplicit = true;
                    OnPropertyChanged("ShowExplicit");
                }
                OnPropertyChanged("ShowInherited");
                RefreshAclFilters();
            }
        }

        public bool ShowExplicit
        {
            get { return _showExplicit; }
            set
            {
                _showExplicit = value;
                if (!_showExplicit && !_showInherited)
                {
                    _showInherited = true;
                    OnPropertyChanged("ShowInherited");
                }
                OnPropertyChanged("ShowExplicit");
                RefreshAclFilters();
            }
        }

        public bool ShowProtected
        {
            get { return _showProtected; }
            set
            {
                _showProtected = value;
                OnPropertyChanged("ShowProtected");
                RefreshAclFilters();
            }
        }

        public bool ShowDisabled
        {
            get { return _showDisabled; }
            set
            {
                _showDisabled = value;
                OnPropertyChanged("ShowDisabled");
                RefreshAclFilters();
            }
        }

        public bool ShowEveryone
        {
            get { return _showEveryone; }
            set
            {
                _showEveryone = value;
                EnsureAtLeastOnePrincipalCategoryEnabled();
                OnPropertyChanged("ShowEveryone");
                RefreshAclFilters();
            }
        }

        public bool ShowAuthenticatedUsers
        {
            get { return _showAuthenticatedUsers; }
            set
            {
                _showAuthenticatedUsers = value;
                EnsureAtLeastOnePrincipalCategoryEnabled();
                OnPropertyChanged("ShowAuthenticatedUsers");
                RefreshAclFilters();
            }
        }

        public bool ShowServiceAccounts
        {
            get { return _showServiceAccounts; }
            set
            {
                _showServiceAccounts = value;
                EnsureAtLeastOnePrincipalCategoryEnabled();
                OnPropertyChanged("ShowServiceAccounts");
                RefreshAclFilters();
            }
        }

        public bool ShowAdminAccounts
        {
            get { return _showAdminAccounts; }
            set
            {
                _showAdminAccounts = value;
                EnsureAtLeastOnePrincipalCategoryEnabled();
                OnPropertyChanged("ShowAdminAccounts");
                RefreshAclFilters();
            }
        }

        public bool ShowOtherPrincipals
        {
            get { return _showOtherPrincipals; }
            set
            {
                _showOtherPrincipals = value;
                EnsureAtLeastOnePrincipalCategoryEnabled();
                OnPropertyChanged("ShowOtherPrincipals");
                RefreshAclFilters();
            }
        }

        public int SummaryTotalEntries
        {
            get { return _summaryTotalEntries; }
            set
            {
                _summaryTotalEntries = value;
                OnPropertyChanged("SummaryTotalEntries");
            }
        }

        public int SummaryHighRisk
        {
            get { return _summaryHighRisk; }
            set
            {
                _summaryHighRisk = value;
                OnPropertyChanged("SummaryHighRisk");
            }
        }

        public int SummaryMediumRisk
        {
            get { return _summaryMediumRisk; }
            set
            {
                _summaryMediumRisk = value;
                OnPropertyChanged("SummaryMediumRisk");
            }
        }

        public int SummaryLowRisk
        {
            get { return _summaryLowRisk; }
            set
            {
                _summaryLowRisk = value;
                OnPropertyChanged("SummaryLowRisk");
            }
        }

        public int SummaryDenyCount
        {
            get { return _summaryDenyCount; }
            set
            {
                _summaryDenyCount = value;
                OnPropertyChanged("SummaryDenyCount");
            }
        }

        public int SummaryEveryoneCount
        {
            get { return _summaryEveryoneCount; }
            set
            {
                _summaryEveryoneCount = value;
                OnPropertyChanged("SummaryEveryoneCount");
            }
        }

        public int SummaryAuthUsersCount
        {
            get { return _summaryAuthUsersCount; }
            set
            {
                _summaryAuthUsersCount = value;
                OnPropertyChanged("SummaryAuthUsersCount");
            }
        }

        public int SummaryFilesCount
        {
            get { return _summaryFilesCount; }
            set
            {
                _summaryFilesCount = value;
                OnPropertyChanged("SummaryFilesCount");
            }
        }

        public int SummaryBaselineAdded
        {
            get { return _summaryBaselineAdded; }
            set
            {
                _summaryBaselineAdded = value;
                OnPropertyChanged("SummaryBaselineAdded");
            }
        }

        public int SummaryBaselineRemoved
        {
            get { return _summaryBaselineRemoved; }
            set
            {
                _summaryBaselineRemoved = value;
                OnPropertyChanged("SummaryBaselineRemoved");
            }
        }

        public bool IsScanning { get { return _isScanning; } }
        public bool IsNotScanning { get { return !_isScanning; } }
        public bool CanImportAnalysis { get { return !_isScanning && !IsBusy; } }
        public bool IsViewerMode { get { return _isViewerMode; } }
        public bool IsNotViewerMode { get { return !_isViewerMode; } }
        public bool IsScanConfigEnabled { get { return !_isViewerMode && !_isScanning; } }
        public bool TreeFilterExplicitOnly
        {
            get { return _treeFilterExplicitOnly; }
            set { _treeFilterExplicitOnly = value; OnPropertyChanged("TreeFilterExplicitOnly"); ReloadTreeWithFilters(); }
        }

        public bool TreeFilterInheritanceDisabledOnly
        {
            get { return _treeFilterInheritanceDisabledOnly; }
            set { _treeFilterInheritanceDisabledOnly = value; OnPropertyChanged("TreeFilterInheritanceDisabledOnly"); ReloadTreeWithFilters(); }
        }

        public bool TreeFilterDiffOnly
        {
            get { return _treeFilterDiffOnly; }
            set { _treeFilterDiffOnly = value; OnPropertyChanged("TreeFilterDiffOnly"); ReloadTreeWithFilters(); }
        }

        public bool TreeFilterExplicitDenyOnly
        {
            get { return _treeFilterExplicitDenyOnly; }
            set { _treeFilterExplicitDenyOnly = value; OnPropertyChanged("TreeFilterExplicitDenyOnly"); ReloadTreeWithFilters(); }
        }

        public bool TreeFilterBaselineMismatchOnly
        {
            get { return _treeFilterBaselineMismatchOnly; }
            set { _treeFilterBaselineMismatchOnly = value; OnPropertyChanged("TreeFilterBaselineMismatchOnly"); ReloadTreeWithFilters(); }
        }

        public bool TreeFilterFilesOnly
        {
            get { return _treeFilterFilesOnly; }
            set
            {
                _treeFilterFilesOnly = value;
                OnPropertyChanged("TreeFilterFilesOnly");
                if (!value && !_treeFilterFoldersOnly)
                {
                    _treeFilterFoldersOnly = true;
                    OnPropertyChanged("TreeFilterFoldersOnly");
                }
                ReloadTreeWithFilters();
            }
        }

        public bool TreeFilterFoldersOnly
        {
            get { return _treeFilterFoldersOnly; }
            set
            {
                _treeFilterFoldersOnly = value;
                OnPropertyChanged("TreeFilterFoldersOnly");
                if (!value && !_treeFilterFilesOnly)
                {
                    _treeFilterFilesOnly = true;
                    OnPropertyChanged("TreeFilterFilesOnly");
                }
                ReloadTreeWithFilters();
            }
        }

        public string SelectedPathKind { get { return _selectedPathKind; } private set { _selectedPathKind = value; OnPropertyChanged("SelectedPathKind"); } }
        public string SelectedOwnerSummary { get { return _selectedOwnerSummary; } private set { _selectedOwnerSummary = value; OnPropertyChanged("SelectedOwnerSummary"); } }
        public string SelectedInheritanceSummary { get { return _selectedInheritanceSummary; } private set { _selectedInheritanceSummary = value; OnPropertyChanged("SelectedInheritanceSummary"); } }
        public int SelectedTotalAceCount { get { return _selectedTotalAceCount; } private set { _selectedTotalAceCount = value; OnPropertyChanged("SelectedTotalAceCount"); } }
        public int SelectedExplicitAceCount { get { return _selectedExplicitAceCount; } private set { _selectedExplicitAceCount = value; OnPropertyChanged("SelectedExplicitAceCount"); } }
        public int SelectedInheritedAceCount { get { return _selectedInheritedAceCount; } private set { _selectedInheritedAceCount = value; OnPropertyChanged("SelectedInheritedAceCount"); } }
        public int SelectedDenyAceCount { get { return _selectedDenyAceCount; } private set { _selectedDenyAceCount = value; OnPropertyChanged("SelectedDenyAceCount"); } }
        public string SelectedPermissionLayers { get { return _selectedPermissionLayers; } private set { _selectedPermissionLayers = value; OnPropertyChanged("SelectedPermissionLayers"); } }
        public string SelectedRiskSummary { get { return _selectedRiskSummary; } private set { _selectedRiskSummary = value; OnPropertyChanged("SelectedRiskSummary"); } }
        public string SelectedAcquisitionWarnings { get { return _selectedAcquisitionWarnings; } private set { _selectedAcquisitionWarnings = value; OnPropertyChanged("SelectedAcquisitionWarnings"); } }
        public string SelectedScannedAtText { get { return _selectedScannedAtText; } private set { _selectedScannedAtText = value; OnPropertyChanged("SelectedScannedAtText"); } }

        public bool HasScanResult { get { return _scanResult != null; } }
        public bool IsBusy { get { return _isBusy; } }
        public bool IsNotBusy { get { return !_isBusy; } }
        public string StatusText
        {
            get
            {
                if (_isScanning) return "RUNNING";
                if (_scanResult == null) return "IDLE";
                return "STOPPED";
            }
        }
        public string StatusBrush
        {
            get
            {
                if (_isScanning) return "#FF2E7D32";
                if (_scanResult == null) return "#FFFFB300";
                return "#FFC62828";
            }
        }

        public bool CanStart { get { return !_isViewerMode && !_isScanning && !IsBusy && (ScanRoots.Count > 0 || !string.IsNullOrWhiteSpace(RootPath)); } }
        public bool CanStop { get { return !_isViewerMode && _isScanning && !IsBusy; } }
        public bool CanExport { get { return !_isViewerMode && !_isScanning && !IsBusy && _scanResult != null; } }
        public bool HasUnexportedData { get { return !_isViewerMode && _scanResult != null && !_hasExported; } }

        public event PropertyChangedEventHandler PropertyChanged;

        public void SelectFolder(string path)
        {
            if (_scanResult == null || string.IsNullOrWhiteSpace(path)) return;
            FolderDetail detail;
            if (!_scanResult.Details.TryGetValue(path, out detail)) return;

            SelectedFolderPath = path;
            GroupEntries.Clear();
            UserEntries.Clear();
            AllEntries.Clear();
            ShareEntries.Clear();
            EffectiveEntries.Clear();

            foreach (var entry in detail.GroupEntries) GroupEntries.Add(entry);
            foreach (var entry in detail.UserEntries) UserEntries.Add(entry);
            foreach (var entry in detail.AllEntries) AllEntries.Add(entry);
            foreach (var entry in detail.ShareEntries) ShareEntries.Add(entry);
            foreach (var entry in detail.EffectiveEntries) EffectiveEntries.Add(entry);
            UpdateSummary(detail);
            UpdateSelectedFolderInfo(path, detail);
        }

        private void UpdateSummary(FolderDetail detail)
        {
            if (detail == null || detail.AllEntries == null)
            {
                SummaryTotalEntries = 0;
                SummaryHighRisk = 0;
                SummaryMediumRisk = 0;
                SummaryLowRisk = 0;
                SummaryDenyCount = 0;
                SummaryEveryoneCount = 0;
                SummaryAuthUsersCount = 0;
                SummaryFilesCount = 0;
                SummaryBaselineAdded = 0;
                SummaryBaselineRemoved = 0;
                return;
            }

            var entries = detail.AllEntries;
            SummaryTotalEntries = entries.Count;
            SummaryHighRisk = entries.Count(entry => string.Equals(entry.RiskLevel, "Alto", StringComparison.OrdinalIgnoreCase));
            SummaryMediumRisk = entries.Count(entry => string.Equals(entry.RiskLevel, "Medio", StringComparison.OrdinalIgnoreCase));
            SummaryLowRisk = entries.Count(entry => string.Equals(entry.RiskLevel, "Basso", StringComparison.OrdinalIgnoreCase));
            SummaryDenyCount = entries.Count(entry => string.Equals(entry.AllowDeny, "Deny", StringComparison.OrdinalIgnoreCase));
            SummaryEveryoneCount = entries.Count(entry => IsEveryone(entry.PrincipalSid, entry.PrincipalName));
            SummaryAuthUsersCount = entries.Count(entry => IsAuthenticatedUsers(entry.PrincipalSid, entry.PrincipalName));
            SummaryFilesCount = entries.Count(entry => string.Equals(entry.ResourceType, "File", StringComparison.OrdinalIgnoreCase));
            SummaryBaselineAdded = detail.BaselineSummary == null ? 0 : detail.BaselineSummary.Added.Count;
            SummaryBaselineRemoved = detail.BaselineSummary == null ? 0 : detail.BaselineSummary.Removed.Count;
        }

        public Task<ResolvedPrincipal[]> GetGroupMembersAsync(string groupSid)
        {
            if (string.IsNullOrWhiteSpace(groupSid)) return Task.FromResult(new ResolvedPrincipal[0]);
            return Task.Run(() =>
            {
                var resolver = CreateResolver(UsePowerShell);
                return resolver.GetGroupMembers(groupSid).ToArray();
            });
        }

        public Task<ResolvedPrincipal[]> GetUserGroupsAsync(string userSid)
        {
            if (string.IsNullOrWhiteSpace(userSid)) return Task.FromResult(new ResolvedPrincipal[0]);
            return Task.Run(() =>
            {
                var resolver = CreateResolver(UsePowerShell);
                return resolver.GetUserGroups(userSid).ToArray();
            });
        }

        private void Browse()
        {
            if (_isViewerMode) return;
            if (TryPickFolder(out var selectedPath))
            {
                RootPath = selectedPath;
            }
        }

        private void BrowseOutputDirectory()
        {
            if (_isViewerMode) return;
            var previousRoot = RootPath;
            RootPath = AuditOutputDirectory;
            if (TryPickFolder(out var selectedPath))
            {
                AuditOutputDirectory = selectedPath;
            }
            RootPath = previousRoot;
        }

        private void AddScanRoot()
        {
            if (string.IsNullOrWhiteSpace(RootPath)) return;
            var rootToAdd = RootPath;
            var namespacePath = string.Empty;
            var targets = PathResolver.GetDfsTargets(RootPath);
            if (targets != null && targets.Count > 0)
            {
                namespacePath = RootPath;
                var selectedTarget = PromptDfsTargetSelection(RootPath, targets);
                if (string.IsNullOrWhiteSpace(selectedTarget))
                {
                    return;
                }
                rootToAdd = selectedTarget;
            }

            var normalizedRoot = GetScanRootKey(rootToAdd);
            if (ScanRoots.Any(path => string.Equals(GetScanRootKey(path), normalizedRoot, StringComparison.OrdinalIgnoreCase))) return;

            ScanRoots.Add(rootToAdd);
            SelectedScanRoot = rootToAdd;
            if (!string.IsNullOrWhiteSpace(namespacePath))
            {
                _scanRootNamespacePaths[normalizedRoot] = namespacePath;
                _scanRootDfsTargets[normalizedRoot] = rootToAdd;
            }
            OnPropertyChanged("CanStart");
            StartCommand.RaiseCanExecuteChanged();
        }

        private void RemoveScanRoot()
        {
            if (string.IsNullOrWhiteSpace(SelectedScanRoot)) return;
            var key = GetScanRootKey(SelectedScanRoot);
            _scanRootDfsTargets.Remove(key);
            _scanRootNamespacePaths.Remove(key);
            ScanRoots.Remove(SelectedScanRoot);
            SelectedScanRoot = ScanRoots.Count > 0 ? ScanRoots[0] : null;
            OnPropertyChanged("CanStart");
            StartCommand.RaiseCanExecuteChanged();
        }

        private string PromptDfsTargetSelection(string namespacePath, IList<string> targets)
        {
            if (targets == null || targets.Count == 0) return null;
            if (targets.Count == 1) return targets[0];

            using (var form = new WinForms.Form())
            using (var combo = new WinForms.ComboBox())
            using (var okButton = new WinForms.Button())
            using (var cancelButton = new WinForms.Button())
            using (var label = new WinForms.Label())
            {
                form.Text = "Seleziona target DFS";
                form.FormBorderStyle = WinForms.FormBorderStyle.FixedDialog;
                form.StartPosition = WinForms.FormStartPosition.CenterScreen;
                form.ClientSize = new System.Drawing.Size(760, 130);
                form.MaximizeBox = false;
                form.MinimizeBox = false;

                label.Text = string.Format("Namespace: {0}", namespacePath);
                label.AutoSize = false;
                label.SetBounds(12, 10, 736, 22);

                combo.DropDownStyle = WinForms.ComboBoxStyle.DropDownList;
                combo.SetBounds(12, 38, 736, 24);
                foreach (var target in targets) combo.Items.Add(target);
                combo.SelectedIndex = 0;

                okButton.Text = "OK";
                okButton.SetBounds(592, 84, 75, 30);
                okButton.DialogResult = WinForms.DialogResult.OK;

                cancelButton.Text = "Annulla";
                cancelButton.SetBounds(673, 84, 75, 30);
                cancelButton.DialogResult = WinForms.DialogResult.Cancel;

                form.Controls.Add(label);
                form.Controls.Add(combo);
                form.Controls.Add(okButton);
                form.Controls.Add(cancelButton);
                form.AcceptButton = okButton;
                form.CancelButton = cancelButton;

                var result = form.ShowDialog();
                if (result != WinForms.DialogResult.OK || combo.SelectedItem == null)
                {
                    return null;
                }

                return combo.SelectedItem.ToString();
            }
        }

        private void LoadSelectedScanRootDfsTargets()
        {
            if (string.IsNullOrWhiteSpace(SelectedScanRoot))
            {
                SelectedScanRootDfsTargets = new ObservableCollection<string>();
                SelectedScanRootDfsTarget = string.Empty;
                return;
            }

            var selectedKey = GetScanRootKey(SelectedScanRoot);
            string namespacePath;
            var sourcePath = _scanRootNamespacePaths.TryGetValue(selectedKey, out namespacePath) && !string.IsNullOrWhiteSpace(namespacePath)
                ? namespacePath
                : SelectedScanRoot;
            var targets = PathResolver.GetDfsTargets(sourcePath) ?? new List<string>();
            SelectedScanRootDfsTargets = new ObservableCollection<string>(targets);
            if (SelectedScanRootDfsTargets.Count == 0)
            {
                SelectedScanRootDfsTarget = string.Empty;
                return;
            }

            string target;
            if (_scanRootDfsTargets.TryGetValue(selectedKey, out target)
                && SelectedScanRootDfsTargets.Any(item => string.Equals(item, target, StringComparison.OrdinalIgnoreCase)))
            {
                SelectedScanRootDfsTarget = target;
                return;
            }

            if (SelectedScanRootDfsTargets.Any(item => string.Equals(item, SelectedScanRoot, StringComparison.OrdinalIgnoreCase)))
            {
                SelectedScanRootDfsTarget = SelectedScanRoot;
                return;
            }

            SelectedScanRootDfsTarget = SelectedScanRootDfsTargets[0];
        }

        private string GetEffectiveScanRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return root;
            string target;
            return _scanRootDfsTargets.TryGetValue(GetScanRootKey(root), out target) && !string.IsNullOrWhiteSpace(target)
                ? target
                : root;
        }

        private static string GetScanRootKey(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return string.Empty;
            return root.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private void InstallService()
        {
            try
            {
                var serviceCommand = ResolveServiceInstallCommand();
                if (string.IsNullOrWhiteSpace(serviceCommand))
                {
                    WpfMessageBox.Show("NtfsAudit.Service.exe (o NtfsAudit.Service.dll) non trovato. Compila/publisha il progetto service e copia l'output vicino all'app, oppure usa una build che includa il service.", "Installazione servizio", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    return;
                }

                var createArguments = string.Format("create {0} binPath= {1} start= auto", ServiceName, BuildServiceBinPathForSc(serviceCommand));
                var createResult = ExecuteScCommand(createArguments, "create", false);
                if (createResult.ExitCode == 1073)
                {
                    ExecuteScCommand(string.Format("config {0} binPath= {1} start= auto", ServiceName, BuildServiceBinPathForSc(serviceCommand)), "config");
                }
                else if (createResult.ExitCode != 0)
                {
                    ThrowScOperationFailed("create", createResult);
                }

                ExecuteScCommand(string.Format("description {0} \"Servizio scansione NTFS Audit\"", ServiceName), "description");
                ExecuteScCommand(string.Format("start {0}", ServiceName), "start", false);
                ProgressText = "Servizio Windows installato.";
                WpfMessageBox.Show("Servizio Windows installato correttamente.", "Installazione servizio", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(string.Format("Errore installazione servizio: {0}", ex.Message), "Installazione servizio", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void UninstallService()
        {
            try
            {
                var stopResult = ExecuteScCommand(string.Format("stop {0}", ServiceName), "stop", false);
                if (stopResult.ExitCode != 0 && stopResult.ExitCode != 1060 && stopResult.ExitCode != 1062)
                {
                    ThrowScOperationFailed("stop", stopResult);
                }

                var deleteResult = ExecuteScCommand(string.Format("delete {0}", ServiceName), "delete", false);
                if (deleteResult.ExitCode != 0 && deleteResult.ExitCode != 1060)
                {
                    ThrowScOperationFailed("delete", deleteResult);
                }

                ProgressText = "Servizio Windows disinstallato.";
                WpfMessageBox.Show("Servizio Windows disinstallato correttamente.", "Disinstallazione servizio", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show(string.Format("Errore disinstallazione servizio: {0}", ex.Message), "Disinstallazione servizio", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private static ScCommandResult ExecuteScCommand(string arguments, string operation = null, bool throwOnError = true)
        {
            var result = RunScCommand(arguments, false);
            if (result.ExitCode == 0)
            {
                return result;
            }

            var initialErrorText = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
            if (result.ExitCode == 5 || (!string.IsNullOrWhiteSpace(initialErrorText) && initialErrorText.IndexOf("accesso negato", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                var elevated = RunScCommand(arguments, true);
                if (elevated.ExitCode == 0)
                {
                    return elevated;
                }

                if (string.IsNullOrWhiteSpace(elevated.Error) && string.IsNullOrWhiteSpace(elevated.Output) && !string.IsNullOrWhiteSpace(initialErrorText))
                {
                    elevated.Error = initialErrorText;
                }

                result = elevated;
            }

            if (throwOnError)
            {
                ThrowScOperationFailed(operation, result);
            }

            return result;
        }

        private static void ThrowScOperationFailed(string operation, ScCommandResult result)
        {
            var errorText = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
            if (string.IsNullOrWhiteSpace(errorText))
            {
                errorText = BuildScFallbackError(result.ExitCode);
            }

            var op = string.IsNullOrWhiteSpace(operation) ? "sc" : operation;
            throw new InvalidOperationException(string.Format("Operazione servizio '{0}' non riuscita (exit code {1}): {2}", op, result.ExitCode, errorText));
        }

        private static string BuildScFallbackError(int exitCode)
        {
            switch (exitCode)
            {
                case 5:
                    return "Accesso negato. Esegui NtfsAudit.App come amministratore e conferma il prompt UAC.";
                case 1060:
                    return "Il servizio specificato non esiste come servizio installato.";
                case 1073:
                    return "Il servizio esiste già.";
                case -1:
                    return "Impossibile avviare sc.exe o richiesta UAC annullata.";
                default:
                    return "Errore sconosciuto durante esecuzione di sc.exe";
            }
        }

        private static ScCommandResult RunScCommand(string arguments, bool runAsAdmin)
        {
            var scPath = ResolveScExecutablePath();
            if (runAsAdmin)
            {
                var elevated = RunScCommandElevatedWithPowerShell(scPath, arguments);
                if (elevated != null)
                {
                    return elevated;
                }

                return RunScCommandElevatedDirect(scPath, arguments);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = scPath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return new ScCommandResult { ExitCode = -1, Error = "Impossibile avviare sc.exe" };
                    }

                    process.WaitForExit();
                    return new ScCommandResult
                    {
                        ExitCode = process.ExitCode,
                        Output = process.StandardOutput.ReadToEnd(),
                        Error = process.StandardError.ReadToEnd()
                    };
                }
            }
            catch (Exception ex)
            {
                return new ScCommandResult { ExitCode = -1, Error = ex.Message };
            }
        }

        private static ScCommandResult RunScCommandElevatedWithPowerShell(string scPath, string arguments)
        {
            var powerShellPath = ResolvePowerShellExecutablePath();
            if (string.IsNullOrWhiteSpace(powerShellPath))
            {
                return null;
            }

            var escapedScPath = EscapePowerShellSingleQuoted(scPath);
            var escapedArguments = EscapePowerShellSingleQuoted(arguments);
            var psArguments = string.Format(
                "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"& {{ $p = Start-Process -FilePath '{0}' -ArgumentList '{1}' -Verb RunAs -Wait -PassThru; exit $p.ExitCode }}\"",
                escapedScPath,
                escapedArguments);

            var startInfo = new ProcessStartInfo
            {
                FileName = powerShellPath,
                Arguments = psArguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return new ScCommandResult { ExitCode = -1, Error = "Impossibile avviare PowerShell per elevare sc.exe" };
                    }

                    process.WaitForExit();
                    return new ScCommandResult
                    {
                        ExitCode = process.ExitCode,
                        Output = process.StandardOutput.ReadToEnd(),
                        Error = process.StandardError.ReadToEnd()
                    };
                }
            }
            catch
            {
                return null;
            }
        }

        private static ScCommandResult RunScCommandElevatedDirect(string scPath, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = scPath,
                Arguments = arguments,
                UseShellExecute = true,
                CreateNoWindow = false,
                Verb = "runas"
            };

            try
            {
                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                    {
                        return new ScCommandResult { ExitCode = -1, Error = "Impossibile avviare sc.exe in elevazione" };
                    }

                    process.WaitForExit();
                    return new ScCommandResult
                    {
                        ExitCode = process.ExitCode,
                        Output = string.Empty,
                        Error = string.Empty
                    };
                }
            }
            catch (Exception ex)
            {
                return new ScCommandResult { ExitCode = -1, Error = ex.Message };
            }
        }

        private static string EscapePowerShellSingleQuoted(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("'", "''");
        }

        private static string ResolveScExecutablePath()
        {
            var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (!string.IsNullOrWhiteSpace(systemDir))
            {
                var scPath = Path.Combine(systemDir, "sc.exe");
                if (File.Exists(scPath))
                {
                    return scPath;
                }
            }

            var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(windir))
            {
                var sysnative = Path.Combine(windir, "sysnative", "sc.exe");
                if (File.Exists(sysnative))
                {
                    return sysnative;
                }
            }

            return "sc.exe";
        }

        private static string ResolvePowerShellExecutablePath()
        {
            var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
            if (!string.IsNullOrWhiteSpace(systemDir))
            {
                var powershellPath = Path.Combine(systemDir, "WindowsPowerShell", "v1.0", "powershell.exe");
                if (File.Exists(powershellPath))
                {
                    return powershellPath;
                }
            }

            var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(windir))
            {
                return Path.Combine(windir, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            }

            return "powershell.exe";
        }

        private sealed class ScCommandResult
        {
            public int ExitCode { get; set; }
            public string Output { get; set; }
            public string Error { get; set; }
        }

        private string ResolveServiceInstallCommand()
        {
            var appBase = AppDomain.CurrentDomain.BaseDirectory;
            var candidates = new List<string>
            {
                Path.Combine(appBase, "NtfsAudit.Service.exe"),
                Path.Combine(appBase, "NtfsAudit.Service", "NtfsAudit.Service.exe"),
                Path.Combine(appBase, "Service", "NtfsAudit.Service.exe"),
                Path.Combine(appBase, "NtfsAudit.Service.dll"),
                Path.Combine(appBase, "NtfsAudit.Service", "NtfsAudit.Service.dll"),
                Path.Combine(appBase, "Service", "NtfsAudit.Service.dll")
            };

            var parent = Directory.GetParent(appBase);
            for (var i = 0; i < 4 && parent != null; i++)
            {
                candidates.Add(Path.Combine(parent.FullName, "NtfsAudit.Service", "bin", "Release", "net8.0-windows", "NtfsAudit.Service.exe"));
                candidates.Add(Path.Combine(parent.FullName, "NtfsAudit.Service", "bin", "Debug", "net8.0-windows", "NtfsAudit.Service.exe"));
                candidates.Add(Path.Combine(parent.FullName, "NtfsAudit.Service", "bin", "Release", "net8.0-windows", "NtfsAudit.Service.dll"));
                candidates.Add(Path.Combine(parent.FullName, "NtfsAudit.Service", "bin", "Debug", "net8.0-windows", "NtfsAudit.Service.dll"));
                parent = parent.Parent;
            }

            var serviceBinary = candidates.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path));
            if (string.IsNullOrWhiteSpace(serviceBinary)) return null;
            return serviceBinary;
        }

        private string BuildServiceBinPathForSc(string serviceCommand)
        {
            if (string.IsNullOrWhiteSpace(serviceCommand))
            {
                throw new InvalidOperationException("Percorso servizio non valido.");
            }

            if (serviceCommand.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return QuoteForSc(serviceCommand);
            }

            var dotnetHost = ResolveDotnetHostPath();
            // Per sc.exe il valore binPath con due token (host + dll) deve essere una singola stringa,
            // con virgolette interne escaped e valore esterno quotato.
            return string.Format("\"\\\"{0}\\\" \\\"{1}\\\"\"",
                SanitizeForSc(dotnetHost),
                SanitizeForSc(serviceCommand));
        }

        private static string SanitizeForSc(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return value.Replace("\"", string.Empty);
        }

        private static string QuoteForSc(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "\"\"";
            return string.Format("\"{0}\"", value.Replace("\"", string.Empty));
        }

        private static string ResolveDotnetHostPath()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var preferred = string.IsNullOrWhiteSpace(programFiles)
                ? null
                : Path.Combine(programFiles, "dotnet", "dotnet.exe");
            if (!string.IsNullOrWhiteSpace(preferred) && File.Exists(preferred))
            {
                return preferred;
            }

            return "dotnet.exe";
        }

        private bool TryPickFolder(out string selectedPath)
        {
            selectedPath = null;
            try
            {
                var dialog = (IFileDialog)new FileOpenDialog();
                dialog.SetTitle("Seleziona cartella");
                dialog.GetOptions(out var options);
                options |= (uint)(FileDialogOptions.PickFolders
                    | FileDialogOptions.ForceFileSystem
                    | FileDialogOptions.PathMustExist
                    | FileDialogOptions.NoChangeDirectory);
                dialog.SetOptions(options);
                AddNetworkPlaces(dialog);

                if (!string.IsNullOrWhiteSpace(RootPath))
                {
                    if (SHCreateItemFromParsingName(RootPath, IntPtr.Zero, typeof(IShellItem).GUID, out var folder) == 0)
                    {
                        dialog.SetFolder(folder);
                    }
                }

                var result = dialog.Show(IntPtr.Zero);
                if (result == HResultCanceled)
                {
                    return false;
                }
                if (result != 0)
                {
                    return false;
                }
                dialog.GetResult(out var item);
                item.GetDisplayName(ShellItemDisplayName.FileSystemPath, out var pszString);
                selectedPath = Marshal.PtrToStringUni(pszString);
                Marshal.FreeCoTaskMem(pszString);
                return !string.IsNullOrWhiteSpace(selectedPath);
            }
            catch
            {
                return false;
            }
        }

        private void AddNetworkPlaces(IFileDialog dialog)
        {
            return;
        }

        private bool TryAddPlace(IFileDialog dialog, string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            if (SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItem).GUID, out var place) == 0)
            {
                dialog.AddPlace(place, (int)FileDialogAddPlace.Bottom);
                return true;
            }

            return false;
        }

        private const int HResultCanceled = unchecked((int)0x800704C7);

        [Flags]
        private enum FileDialogOptions : uint
        {
            PickFolders = 0x00000020,
            ForceFileSystem = 0x00000040,
            NoChangeDirectory = 0x00000008,
            PathMustExist = 0x00000800
        }

        private enum FileDialogAddPlace
        {
            Top = 0,
            Bottom = 1
        }

        private enum ShellItemDisplayName : uint
        {
            FileSystemPath = 0x80058000
        }

        [ComImport]
        [Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
        private class FileOpenDialog
        {
        }

        [ComImport]
        [Guid("42F85136-DB7E-439C-85F1-E4075D135FC8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFileDialog
        {
            [PreserveSig]
            int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
        }

        [ComImport]
        [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(ShellItemDisplayName sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            out IShellItem ppv);

        private void StartScan()
        {
            if (_isViewerMode) return;
            var roots = ScanRoots.Count > 0
                ? ScanRoots.Select(GetEffectiveScanRoot).ToList()
                : new List<string> { string.IsNullOrWhiteSpace(SelectedDfsTarget) ? RootPath : SelectedDfsTarget };
            roots = roots.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (roots.Count == 0)
            {
                ProgressText = "Aggiungi almeno una cartella da analizzare.";
                return;
            }

            var invalidRoot = roots.FirstOrDefault(path => !Directory.Exists(PathResolver.ToExtendedPath(path)));
            if (!string.IsNullOrWhiteSpace(invalidRoot))
            {
                ProgressText = string.Format("Percorso non valido: {0}", invalidRoot);
                return;
            }

            if (!string.IsNullOrWhiteSpace(AuditOutputDirectory) && !Directory.Exists(PathResolver.ToExtendedPath(AuditOutputDirectory)))
            {
                Directory.CreateDirectory(PathResolver.ToExtendedPath(AuditOutputDirectory));
            }

            if (UseWindowsServiceMode)
            {
                EnqueueServiceScan(roots);
                return;
            }

            _isScanning = true;
            CurrentPathText = string.Empty;
            ElapsedText = "00:00:00";
            StartElapsedTimer();
            UpdateCommands();
            ClearResults();

            _cts = new CancellationTokenSource();
            var optionsTemplate = new ScanOptions
            {
                MaxDepth = ScanAllDepths ? int.MaxValue : MaxDepth,
                ScanAllDepths = ScanAllDepths,
                IncludeInherited = IncludeInherited,
                ResolveIdentities = ResolveIdentities,
                ExcludeServiceAccounts = ResolveIdentities && ExcludeServiceAccounts,
                ExcludeAdminAccounts = ResolveIdentities && ExcludeAdminAccounts,
                ExpandGroups = ResolveIdentities && ExpandGroups,
                UsePowerShell = ResolveIdentities && UsePowerShell,
                EnableAdvancedAudit = EnableAdvancedAudit,
                ComputeEffectiveAccess = EnableAdvancedAudit && ComputeEffectiveAccess,
                IncludeSharePermissions = EnableAdvancedAudit && IncludeSharePermissions,
                IncludeFiles = EnableAdvancedAudit && IncludeFiles,
                ReadOwnerAndSacl = EnableAdvancedAudit && ReadOwnerAndSacl,
                CompareBaseline = EnableAdvancedAudit && CompareBaseline,
                OutputDirectory = AuditOutputDirectory
            };

            Task.Run(() => ExecuteBatchScan(roots, optionsTemplate, _cts.Token));
        }

        private void EnqueueServiceScan(List<string> roots)
        {
            try
            {
                var jobsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NtfsAudit", "jobs");
                Directory.CreateDirectory(jobsRoot);
                var optionsTemplate = new ScanOptions
                {
                    MaxDepth = ScanAllDepths ? int.MaxValue : MaxDepth,
                    ScanAllDepths = ScanAllDepths,
                    IncludeInherited = IncludeInherited,
                    ResolveIdentities = ResolveIdentities,
                    ExcludeServiceAccounts = ResolveIdentities && ExcludeServiceAccounts,
                    ExcludeAdminAccounts = ResolveIdentities && ExcludeAdminAccounts,
                    ExpandGroups = ResolveIdentities && ExpandGroups,
                    UsePowerShell = ResolveIdentities && UsePowerShell,
                    EnableAdvancedAudit = EnableAdvancedAudit,
                    ComputeEffectiveAccess = EnableAdvancedAudit && ComputeEffectiveAccess,
                    IncludeSharePermissions = EnableAdvancedAudit && IncludeSharePermissions,
                    IncludeFiles = EnableAdvancedAudit && IncludeFiles,
                    ReadOwnerAndSacl = EnableAdvancedAudit && ReadOwnerAndSacl,
                    CompareBaseline = EnableAdvancedAudit && CompareBaseline,
                    OutputDirectory = AuditOutputDirectory
                };
                var job = new ServiceScanJob
                {
                    JobId = Guid.NewGuid().ToString("N"),
                    CreatedAtUtc = DateTime.UtcNow,
                    ScanOptions = roots.Select(root => CloneOptions(optionsTemplate, root)).ToList()
                };
                var jobFile = Path.Combine(jobsRoot, string.Format("job_{0}.json", job.JobId));
                File.WriteAllText(jobFile, JsonConvert.SerializeObject(job, Formatting.Indented));
                ExecuteScCommand(string.Format("start {0}", ServiceName), "start", false);
                ProgressText = "Job inviato al servizio Windows. La scansione continua anche dopo il logout utente.";
            }
            catch (Exception ex)
            {
                ProgressText = string.Format("Errore invio job al servizio: {0}", ex.Message);
                WpfMessageBox.Show(ProgressText, "Servizio Windows", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
        }

        private void ExecuteBatchScan(List<string> roots, ScanOptions optionsTemplate, CancellationToken token)
        {
            var aggregateResult = new ScanResult
            {
                RootPath = roots == null || roots.Count == 0 ? RootPath : roots[0],
                Details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase),
                TreeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase),
                ScanOptions = CloneOptions(optionsTemplate, roots == null || roots.Count == 0 ? RootPath : roots[0]),
                ScannedAtUtc = DateTime.UtcNow
            };

            try
            {
                foreach (var root in roots)
                {
                    token.ThrowIfCancellationRequested();
                    var options = CloneOptions(optionsTemplate, root);
                    var result = ExecuteScan(options, token);
                    if (result == null)
                    {
                        continue;
                    }

                    if (result.TreeMap == null || result.TreeMap.Count == 0)
                    {
                        result.TreeMap = BuildTreeMapFromDetails(result.Details, result.RootPath);
                    }

                    MergeScanResult(aggregateResult, result);

                    if (!string.IsNullOrWhiteSpace(options.OutputDirectory))
                    {
                        var safeName = BuildScanNameFromRoot(root);
                        var outputFile = Path.Combine(options.OutputDirectory, string.Format("{0}_{1}.ntaudit", safeName, DateTime.Now.ToString("yyyy_MM_dd_HH_mm")));
                        _analysisArchive.Export(result, root, outputFile);
                    }
                }
            }
            finally
            {
                RunOnUi(() =>
                {
                    if (aggregateResult.Details != null && aggregateResult.Details.Count > 0)
                    {
                        _scanResult = aggregateResult;
                        _hasExported = false;
                        _fullTreeMap = null;
                        LoadTree(_scanResult);
                        var rootToSelect = ResolveTreeRoot(_scanResult.TreeMap, _scanResult.RootPath);
                        if (!string.IsNullOrWhiteSpace(rootToSelect))
                        {
                            RootPath = rootToSelect;
                            SelectFolder(rootToSelect);
                        }
                    }

                    _isScanning = false;
                    StopElapsedTimer();
                    UpdateCommands();
                });
            }
        }

        private static void MergeScanResult(ScanResult aggregate, ScanResult current)
        {
            if (aggregate == null || current == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(current.TempDataPath)) aggregate.TempDataPath = current.TempDataPath;
            if (!string.IsNullOrWhiteSpace(current.ErrorPath)) aggregate.ErrorPath = current.ErrorPath;
            if (!string.IsNullOrWhiteSpace(current.RootPath) && string.IsNullOrWhiteSpace(aggregate.RootPath)) aggregate.RootPath = current.RootPath;
            if (aggregate.RootPathKind == PathKind.Unknown && current.RootPathKind != PathKind.Unknown) aggregate.RootPathKind = current.RootPathKind;
            if (current.ScannedAtUtc != default(DateTime)) aggregate.ScannedAtUtc = current.ScannedAtUtc;

            if (aggregate.Details == null) aggregate.Details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase);
            if (current.Details != null)
            {
                foreach (var detailPair in current.Details)
                {
                    if (detailPair.Value == null) continue;
                    FolderDetail existing;
                    if (!aggregate.Details.TryGetValue(detailPair.Key, out existing))
                    {
                        aggregate.Details[detailPair.Key] = detailPair.Value;
                        continue;
                    }

                    MergeFolderDetail(existing, detailPair.Value);
                }
            }

            if (aggregate.TreeMap == null) aggregate.TreeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            if (current.TreeMap != null)
            {
                MergeTreeMap(aggregate.TreeMap, current.TreeMap);
            }
        }

        private static void MergeFolderDetail(FolderDetail target, FolderDetail source)
        {
            if (target == null || source == null)
            {
                return;
            }

            target.AllEntries.AddRange(source.AllEntries);
            target.GroupEntries.AddRange(source.GroupEntries);
            target.UserEntries.AddRange(source.UserEntries);
            target.ShareEntries.AddRange(source.ShareEntries);
            target.EffectiveEntries.AddRange(source.EffectiveEntries);
            target.HasExplicitPermissions = target.HasExplicitPermissions || source.HasExplicitPermissions;
            target.HasExplicitNtfs = target.HasExplicitNtfs || source.HasExplicitNtfs;
            target.HasExplicitShare = target.HasExplicitShare || source.HasExplicitShare;
            target.IsInheritanceDisabled = target.IsInheritanceDisabled || source.IsInheritanceDisabled;
            if (source.DiffSummary != null) target.DiffSummary = source.DiffSummary;
            if (source.BaselineSummary != null) target.BaselineSummary = source.BaselineSummary;
        }

        private static void MergeTreeMap(Dictionary<string, List<string>> target, Dictionary<string, List<string>> source)
        {
            if (target == null || source == null)
            {
                return;
            }

            foreach (var node in source)
            {
                List<string> children;
                if (!target.TryGetValue(node.Key, out children) || children == null)
                {
                    target[node.Key] = node.Value == null ? new List<string>() : new List<string>(node.Value);
                    continue;
                }

                if (node.Value == null)
                {
                    continue;
                }

                foreach (var child in node.Value)
                {
                    if (!children.Contains(child, StringComparer.OrdinalIgnoreCase))
                    {
                        children.Add(child);
                    }
                }
            }
        }

        private static ScanOptions CloneOptions(ScanOptions template, string root)
        {
            return new ScanOptions
            {
                RootPath = root,
                OutputDirectory = template.OutputDirectory,
                MaxDepth = template.MaxDepth,
                ScanAllDepths = template.ScanAllDepths,
                IncludeInherited = template.IncludeInherited,
                ResolveIdentities = template.ResolveIdentities,
                ExcludeServiceAccounts = template.ExcludeServiceAccounts,
                ExcludeAdminAccounts = template.ExcludeAdminAccounts,
                ExpandGroups = template.ExpandGroups,
                UsePowerShell = template.UsePowerShell,
                EnableAdvancedAudit = template.EnableAdvancedAudit,
                ComputeEffectiveAccess = template.ComputeEffectiveAccess,
                IncludeSharePermissions = template.IncludeSharePermissions,
                IncludeFiles = template.IncludeFiles,
                ReadOwnerAndSacl = template.ReadOwnerAndSacl,
                CompareBaseline = template.CompareBaseline
            };
        }

        private static string SanitizeFileName(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }
            return value;
        }

        private static string BuildScanNameFromRoot(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) return "scan";
            var normalized = root.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(normalized)) return "scan";

            string name;
            if (normalized.StartsWith("\\", StringComparison.Ordinal))
            {
                var segments = normalized.Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
                name = segments.Length > 0 ? segments[segments.Length - 1] : string.Empty;
            }
            else
            {
                name = Path.GetFileName(normalized);
                if (string.IsNullOrWhiteSpace(name) && normalized.Length >= 2 && normalized[1] == ':')
                {
                    name = normalized.Substring(0, 1);
                }
            }

            name = SanitizeFileName(name);
            return string.IsNullOrWhiteSpace(name) ? "scan" : name;
        }

        private void StopScan()
        {
            if (_isViewerMode) return;
            if (_cts != null)
            {
                _cts.Cancel();
            }
        }

        private async void Export()
        {
            if (_isViewerMode) return;
            if (_scanResult == null) return;
            if (!TryEnsureScanDataAvailableForExport("Export non disponibile")) return;
            var dialog = new Win32.SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
                FileName = BuildExportFileName(RootPath, "xlsx"),
                InitialDirectory = ResolveInitialDirectory(_lastExportDirectory, RootPath)
            };
            if (dialog.ShowDialog() != true) return;
            var outputPath = dialog.FileName;
            try
            {
                SetBusy(true);
                var result = await Task.Run(() => _excelExporter.Export(_scanResult.TempDataPath, _scanResult.ErrorPath, outputPath));
                EnsureExportOutput(outputPath);
                _hasExported = true;
                UpdateLastDirectory(ref _lastExportDirectory, outputPath);
                var warningMessage = BuildExcelWarningMessage(result);
                if (!string.IsNullOrWhiteSpace(warningMessage))
                {
                    WpfMessageBox.Show(
                        string.Format("ATTENZIONE: {0}", warningMessage),
                        "Export con avvisi",
                        System.Windows.MessageBoxButton.OK,
                        System.Windows.MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                ProgressText = string.Format("Errore export: {0}", ex.Message);
                WpfMessageBox.Show(ProgressText, "Errore export", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void ExportAnalysis()
        {
            if (_isViewerMode) return;
            if (_scanResult == null) return;
            if (!TryEnsureScanDataAvailableForExport("Export analisi non disponibile")) return;
            var dialog = new Win32.SaveFileDialog
            {
                Filter = "Analisi NtfsAudit (*.ntaudit)|*.ntaudit",
                FileName = BuildExportFileName(RootPath, "ntaudit"),
                InitialDirectory = ResolveInitialDirectory(_lastExportDirectory, RootPath)
            };
            if (dialog.ShowDialog() != true) return;
            await RunExportActionAsync(
                () => _analysisArchive.Export(_scanResult, RootPath, dialog.FileName),
                "Errore export analisi",
                dialog.FileName);
        }

        private async void ImportAnalysis()
        {
            if (IsBusy) return;
            var dialog = new Win32.OpenFileDialog
            {
                Filter = "Analisi NtfsAudit (*.ntaudit)|*.ntaudit",
                InitialDirectory = ResolveInitialDirectory(_lastImportDirectory, RootPath)
            };
            if (dialog.ShowDialog() != true) return;
            try
            {
                SetBusy(true);
                var imported = await Task.Run(() => _analysisArchive.Import(dialog.FileName));
                var importedResult = imported.ScanResult;
                if (importedResult == null)
                {
                    ProgressText = "Analisi importata non valida.";
                    return;
                }
                if (importedResult.Details == null)
                {
                    importedResult.Details = new Dictionary<string, FolderDetail>(StringComparer.OrdinalIgnoreCase);
                }
                if (importedResult.TreeMap == null)
                {
                    importedResult.TreeMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                }

                ApplyDiffs(importedResult);
                if (!ValidateImportedResult(importedResult, out var validationMessage))
                {
                    var confirm = WpfMessageBox.Show(
                        string.Format("{0}\n\nVuoi procedere comunque?", validationMessage),
                        "Import analisi",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Warning);
                    if (confirm != System.Windows.MessageBoxResult.Yes)
                    {
                        ProgressText = "Import annullato dall'utente.";
                        return;
                    }
                }
                _scanResult = importedResult;
                _hasExported = false;
                ApplyImportedOptions(imported.ScanOptions);
                if (!string.IsNullOrWhiteSpace(imported.RootPath))
                {
                    RootPath = imported.RootPath;
                }
                ClearResults();
                LoadTree(_scanResult);
                LoadErrors(_scanResult.ErrorPath);
                var root = _fullTreeMap == null || _fullTreeMap.Count == 0
                    ? RootPath
                    : ResolveTreeRoot(_fullTreeMap, _scanResult.RootPath);

                if (string.IsNullOrWhiteSpace(root) && _scanResult.TreeMap != null && _scanResult.TreeMap.Count > 0)
                {
                    root = ResolveTreeRoot(_scanResult.TreeMap, _scanResult.RootPath);
                }

                if (!string.IsNullOrWhiteSpace(root))
                {
                    RootPath = root;
                    SelectFolder(root);
                }
                UpdateLastDirectory(ref _lastImportDirectory, dialog.FileName);
                UpdateCommands();
            }
            catch (Exception ex)
            {
                ProgressText = string.Format("Errore import analisi: {0}", ex.Message);
                WpfMessageBox.Show(ProgressText, "Errore import", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private bool TryEnsureScanDataAvailableForExport(string messageTitle)
        {
            if (_scanResult == null)
            {
                return false;
            }

            var ioTempDataPath = PathResolver.ToExtendedPath(_scanResult.TempDataPath);
            if (string.IsNullOrWhiteSpace(_scanResult.TempDataPath) || !File.Exists(ioTempDataPath))
            {
                ProgressText = "Export non disponibile: file dati scansione mancante.";
                WpfMessageBox.Show(ProgressText, messageTitle, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private async Task RunExportActionAsync(Action exportAction, string errorLabel, string outputPath = null)
        {
            try
            {
                SetBusy(true);
                await Task.Run(exportAction);
                EnsureExportOutput(outputPath);
                _hasExported = true;
                UpdateLastDirectory(ref _lastExportDirectory, outputPath);
            }
            catch (Exception ex)
            {
                ProgressText = string.Format("{0}: {1}", errorLabel, ex.Message);
                WpfMessageBox.Show(ProgressText, errorLabel, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private string BuildExcelWarningMessage(ExcelExportResult result)
        {
            if (result == null)
            {
                return null;
            }

            if (result.WasSplit)
            {
                return string.Format(
                    "Il dataset supera il limite di 1.048.576 righe per foglio. Creati {0} fogli Users e {1} fogli Groups.",
                    result.UserSheetCount,
                    result.GroupSheetCount);
            }

            return null;
        }

        private void EnsureExportOutput(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath)) return;
            var ioPath = PathResolver.ToExtendedPath(outputPath);
            if (!File.Exists(ioPath))
            {
                throw new IOException("Il file export non è stato creato.");
            }
            var info = new FileInfo(ioPath);
            if (info.Length == 0)
            {
                throw new IOException("Il file export risulta vuoto.");
            }
        }

        private ScanResult ExecuteScan(ScanOptions options, CancellationToken token)
        {
            try
            {
                var adResolver = CreateResolver(options.UsePowerShell);
                var identityResolver = new IdentityResolver(_sidNameCache, adResolver);
                var groupExpansion = new GroupExpansionService(adResolver, _groupMembershipCache);
                var scanService = new ScanService(identityResolver, groupExpansion);

                var progress = new Progress<ScanProgress>(scanProgress =>
                {
                    RunOnUi(() => UpdateProgress(scanProgress));
                });
                var result = scanService.Run(options, progress, token);

                RunOnUi(() =>
                {
                    ApplyDiffs(result);
                    _scanResult = result;
                    _hasExported = false;
                    SaveCache();
                    LoadTree(result);
                    LoadErrors(result.ErrorPath);
                    ErrorCount = Errors.Count;
                    SelectFolder(options.RootPath);
                });

                return result;
            }
            catch (OperationCanceledException)
            {
                RunOnUi(() =>
                {
                    ProgressText = "Scansione annullata";
                    CurrentPathText = string.Empty;
                });
                throw;
            }
            catch (Exception ex)
            {
                RunOnUi(() =>
                {
                    ProgressText = string.Format("Errore scansione: {0}", ex.Message);
                    CurrentPathText = string.Empty;
                });
                return null;
            }
        }

        private IAdResolver CreateResolver(bool usePowerShell)
        {
            if (usePowerShell)
            {
                var path = FindPowerShell();
                var psResolver = new PowerShellAdResolver(path);
                var dsResolver = new DirectoryServicesResolver();
                if (psResolver.IsAvailable) return new CompositeAdResolver(psResolver, dsResolver);
                return dsResolver;
            }

            return new DirectoryServicesResolver();
        }

        private string FindPowerShell()
        {
            var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var candidate = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
            if (File.Exists(candidate)) return candidate;
            var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrWhiteSpace(windir))
            {
                return Path.Combine(windir, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
            }

            return "powershell.exe";
        }

        private static bool IsProcessElevated()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                if (identity == null) return false;
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private void UpdateProgress(ScanProgress progress)
        {
            CurrentPathText = string.IsNullOrWhiteSpace(progress.CurrentPath) ? string.Empty : progress.CurrentPath;
            ProcessedCount = progress.Processed;
            ProcessedFilesCount = progress.FilesProcessed;
            ElapsedText = FormatElapsed(progress.Elapsed);
            ErrorCount = progress.Errors;
            if (string.Equals(progress.Stage, "Errore", StringComparison.OrdinalIgnoreCase))
            {
                CurrentPathBackground = "#FFFFCDD2";
            }
            else if (!string.IsNullOrWhiteSpace(progress.CurrentPath))
            {
                CurrentPathBackground = "#FFC8E6C9";
            }
            else
            {
                CurrentPathBackground = "Transparent";
            }
        }

        private void LoadTree(ScanResult result)
        {
            FolderTree.Clear();
            _currentFilteredTreeMap = null;
            if (result == null)
            {
                return;
            }

            if (_fullTreeMap == null || _fullTreeMap.Count == 0)
            {
                _fullTreeMap = ResolveFullTreeMap(result);
            }

            var treeMap = _fullTreeMap;
            if (treeMap == null || treeMap.Count == 0)
            {
                return;
            }

            var preferredRoot = ResolveTreeRoot(treeMap, RootPath);
            var filteredTreeMap = ApplyTreeFilters(treeMap, result.Details, preferredRoot);
            _currentFilteredTreeMap = filteredTreeMap;
            var provider = new FolderTreeProvider(filteredTreeMap, result.Details);
            var roots = ResolveTreeRoots(filteredTreeMap, preferredRoot);
            if (roots.Count == 0) return;

            foreach (var rootPath in roots)
            {
                var rootName = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(rootName)) rootName = rootPath;
                var rootDetail = result.Details != null && result.Details.TryGetValue(rootPath, out var detail) ? detail : null;
                var rootSummary = rootDetail == null ? null : rootDetail.DiffSummary;
                var rootNode = new FolderNodeViewModel(
                    rootPath,
                    rootName,
                    provider,
                    rootDetail != null && rootDetail.HasExplicitPermissions,
                    rootDetail != null && rootDetail.IsInheritanceDisabled,
                    rootSummary == null ? 0 : rootSummary.Added.Count(key => !key.IsInherited),
                    rootSummary == null ? 0 : rootSummary.Removed.Count,
                    rootSummary == null ? 0 : rootSummary.DenyExplicitCount,
                    rootSummary != null && rootSummary.IsProtected,
                    rootDetail == null || rootDetail.BaselineSummary == null ? 0 : rootDetail.BaselineSummary.Added.Count,
                    rootDetail == null || rootDetail.BaselineSummary == null ? 0 : rootDetail.BaselineSummary.Removed.Count,
                    rootDetail != null && rootDetail.HasExplicitNtfs,
                    rootDetail != null && rootDetail.HasExplicitShare,
                    rootDetail != null && rootDetail.AllEntries.Any(entry => string.Equals(entry.RiskLevel, "Alto", StringComparison.OrdinalIgnoreCase)),
                    rootDetail != null && rootDetail.AllEntries.Any(entry => string.Equals(entry.RiskLevel, "Medio", StringComparison.OrdinalIgnoreCase)),
                    rootDetail != null && rootDetail.AllEntries.Any(entry => string.Equals(entry.RiskLevel, "Basso", StringComparison.OrdinalIgnoreCase)));
                rootNode.IsExpanded = true;
                if (FolderTree.Count == 0)
                {
                    rootNode.IsSelected = true;
                }
                FolderTree.Add(rootNode);
            }
        }

        private void ReloadTreeWithFilters()
        {
            if (_scanResult == null || _fullTreeMap == null || _fullTreeMap.Count == 0) return;
            var preferredPath = SelectedFolderPath;
            var preferredRoot = ResolveTreeRoot(_fullTreeMap, _scanResult.RootPath);
            if (!string.IsNullOrWhiteSpace(preferredRoot)
                && !string.Equals(RootPath, preferredRoot, StringComparison.OrdinalIgnoreCase))
            {
                RootPath = preferredRoot;
            }
            LoadTree(_scanResult);
            if (_currentFilteredTreeMap == null || _currentFilteredTreeMap.Count == 0)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(preferredPath) && _currentFilteredTreeMap.ContainsKey(preferredPath))
            {
                SelectFolder(preferredPath);
                return;
            }

            var visibleRoot = ResolveTreeRoot(_currentFilteredTreeMap, RootPath);
            if (!string.IsNullOrWhiteSpace(visibleRoot))
            {
                SelectFolder(visibleRoot);
            }
        }

        private Dictionary<string, List<string>> ApplyTreeFilters(Dictionary<string, List<string>> treeMap, Dictionary<string, FolderDetail> details, string rootPath)
        {
            if (treeMap == null || treeMap.Count == 0) return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var filtered = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            var roots = ResolveTreeRoots(treeMap, rootPath);
            if (roots.Count == 0)
            {
                return filtered;
            }

            bool IncludeNode(string node, string root)
            {
                if (string.IsNullOrWhiteSpace(node)) return false;
                var direct = NodeMatchesTreeFilters(node, details);
                if (!treeMap.TryGetValue(node, out var children) || children == null || children.Count == 0)
                {
                    if (direct) filtered[node] = new List<string>();
                    return direct;
                }

                var includedChildren = new List<string>();
                foreach (var child in children)
                {
                    if (IncludeNode(child, root)) includedChildren.Add(child);
                }
                if (direct || includedChildren.Count > 0 || string.Equals(node, root, StringComparison.OrdinalIgnoreCase))
                {
                    filtered[node] = includedChildren;
                    return true;
                }
                return false;
            }

            foreach (var root in roots)
            {
                IncludeNode(root, root);
            }

            return filtered;
        }

        private List<string> ResolveTreeRoots(Dictionary<string, List<string>> treeMap, string preferredRoot)
        {
            var roots = new List<string>();
            if (treeMap == null || treeMap.Count == 0)
            {
                return roots;
            }

            var childSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in treeMap)
            {
                if (entry.Value == null) continue;
                foreach (var child in entry.Value)
                {
                    if (!string.IsNullOrWhiteSpace(child))
                    {
                        childSet.Add(NormalizeTreePath(child));
                    }
                }
            }

            roots = treeMap.Keys
                .Where(key => !childSet.Contains(NormalizeTreePath(key)))
                .OrderBy(key => NormalizeTreePath(key))
                .ToList();

            if (roots.Count == 0)
            {
                roots.AddRange(treeMap.Keys.OrderBy(key => NormalizeTreePath(key)));
            }

            if (!string.IsNullOrWhiteSpace(preferredRoot))
            {
                var normalizedPreferred = NormalizeTreePath(preferredRoot);
                var preferred = roots.FirstOrDefault(root => string.Equals(NormalizeTreePath(root), normalizedPreferred, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(preferred))
                {
                    roots.Remove(preferred);
                    roots.Insert(0, preferred);
                }
            }

            return roots;
        }

        private Dictionary<string, List<string>> ResolveFullTreeMap(ScanResult result)
        {
            if (result == null)
            {
                return null;
            }

            var treeMap = result.TreeMap;
            var preferredRoot = !string.IsNullOrWhiteSpace(result.RootPath) ? result.RootPath : RootPath;
            var detailsTreeMap = BuildTreeMapFromDetails(result.Details, preferredRoot);
            var treeMapCount = treeMap == null ? 0 : treeMap.Count;
            var detailsTreeMapCount = detailsTreeMap == null ? 0 : detailsTreeMap.Count;

            Debug.WriteLine(string.Format(
                "[TreeMap] import source counts => treeMap:{0}, detailsTreeMap:{1}, root:{2}",
                treeMapCount,
                detailsTreeMapCount,
                preferredRoot));

            if ((treeMap == null || treeMap.Count == 0) && detailsTreeMap != null && detailsTreeMap.Count > 0)
            {
                Debug.WriteLine("[TreeMap] using detailsTreeMap (treeMap missing or empty)");
                return detailsTreeMap;
            }

            // Compatibilità con analisi legacy: alcune esportazioni storiche contengono TreeMap parziali.
            if (treeMap != null && treeMap.Count > 0 && detailsTreeMap != null && detailsTreeMap.Count > treeMap.Count)
            {
                Debug.WriteLine("[TreeMap] using detailsTreeMap (legacy partial treeMap detected)");
                return detailsTreeMap;
            }

            if (treeMap != null && treeMap.Count > 0)
            {
                Debug.WriteLine("[TreeMap] using persisted treeMap");
                return treeMap;
            }

            Debug.WriteLine("[TreeMap] fallback to export records treeMap reconstruction");
            return BuildTreeMapFromExportRecords(result.TempDataPath, preferredRoot);
        }

        private bool NodeMatchesTreeFilters(string path, Dictionary<string, FolderDetail> details)
        {
            if (details == null || !details.TryGetValue(path, out var detail) || detail == null)
            {
                return !AnyTreeFilterEnabled();
            }

            var diff = detail.DiffSummary;
            var hasDiff = diff != null && (diff.Added.Count > 0 || diff.Removed.Count > 0);
            var hasExplicitDeny = diff != null && diff.DenyExplicitCount > 0;
            var hasBaselineMismatch = detail.BaselineSummary != null && (detail.BaselineSummary.Added.Count > 0 || detail.BaselineSummary.Removed.Count > 0);

            var hasTypeMatch = true;
            var hasFiles = detail.AllEntries.Any(e => IsFileResourceType(e.ResourceType));
            var hasFolders = detail.AllEntries.Any(e => IsFolderResourceType(e.ResourceType));
            if (TreeFilterFilesOnly && !TreeFilterFoldersOnly)
            {
                hasTypeMatch = hasFiles;
            }
            else if (TreeFilterFoldersOnly && !TreeFilterFilesOnly)
            {
                hasTypeMatch = hasFolders;
            }

            if (!hasTypeMatch)
            {
                return false;
            }

            var includeByCategory = false;
            var categoryFilterSelected = false;

            if (TreeFilterExplicitOnly)
            {
                categoryFilterSelected = true;
                includeByCategory = includeByCategory || detail.HasExplicitPermissions;
            }

            if (TreeFilterInheritanceDisabledOnly)
            {
                categoryFilterSelected = true;
                includeByCategory = includeByCategory || detail.IsInheritanceDisabled;
            }

            if (TreeFilterDiffOnly)
            {
                categoryFilterSelected = true;
                includeByCategory = includeByCategory || hasDiff;
            }

            if (TreeFilterExplicitDenyOnly)
            {
                categoryFilterSelected = true;
                includeByCategory = includeByCategory || hasExplicitDeny;
            }

            if (TreeFilterBaselineMismatchOnly)
            {
                categoryFilterSelected = true;
                includeByCategory = includeByCategory || hasBaselineMismatch;
            }

            return !categoryFilterSelected || includeByCategory;
        }

        private static bool IsFileResourceType(string resourceType)
        {
            return string.Equals(resourceType, "File", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsFolderResourceType(string resourceType)
        {
            return string.Equals(resourceType, "Folder", StringComparison.OrdinalIgnoreCase)
                || string.Equals(resourceType, "Cartella", StringComparison.OrdinalIgnoreCase)
                || string.Equals(resourceType, "Directory", StringComparison.OrdinalIgnoreCase);
        }

        private bool AnyTreeFilterEnabled()
        {
            return TreeFilterExplicitOnly
                || TreeFilterInheritanceDisabledOnly
                || TreeFilterDiffOnly
                || TreeFilterExplicitDenyOnly
                || TreeFilterBaselineMismatchOnly
                || (TreeFilterFilesOnly ^ TreeFilterFoldersOnly);
        }

        private void UpdateSelectedFolderInfo(string path, FolderDetail detail)
        {
            var entries = detail == null ? new List<AceEntry>() : detail.AllEntries;
            SelectedPathKind = PathResolver.DetectPathKind(path).ToString();
            SelectedOwnerSummary = entries.Select(e => e.Owner).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "-";
            SelectedInheritanceSummary = detail != null && detail.IsInheritanceDisabled ? "Ereditarietà disabilitata" : "Ereditarietà attiva";
            SelectedTotalAceCount = entries.Count;
            SelectedExplicitAceCount = entries.Count(e => !e.IsInherited);
            SelectedInheritedAceCount = entries.Count(e => e.IsInherited);
            SelectedDenyAceCount = entries.Count(e => string.Equals(e.AllowDeny, "Deny", StringComparison.OrdinalIgnoreCase));
            var layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (entries.Count > 0) layers.Add("NTFS");
            if (detail != null && detail.ShareEntries.Count > 0) layers.Add("Share");
            if (detail != null && detail.EffectiveEntries.Count > 0) layers.Add("Effective");
            if (string.Equals(SelectedPathKind, "Nfs", StringComparison.OrdinalIgnoreCase)) layers.Add("NFS");
            SelectedPermissionLayers = layers.Count == 0 ? "-" : string.Join(", ", layers);
            SelectedRiskSummary = string.Format("High: {0}, Medium: {1}, Low: {2}",
                entries.Count(e => string.Equals(e.RiskLevel, "Alto", StringComparison.OrdinalIgnoreCase)),
                entries.Count(e => string.Equals(e.RiskLevel, "Medio", StringComparison.OrdinalIgnoreCase)),
                entries.Count(e => string.Equals(e.RiskLevel, "Basso", StringComparison.OrdinalIgnoreCase)));
            var warning = entries.Select(e => e.AuditSummary).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v) && v.IndexOf("non", StringComparison.OrdinalIgnoreCase) >= 0);
            if (string.IsNullOrWhiteSpace(warning) && string.Equals(SelectedPathKind, "Nfs", StringComparison.OrdinalIgnoreCase))
            {
                warning = "Percorso NFS: alcune ACL potrebbero non essere disponibili in ambiente Windows.";
            }
            SelectedAcquisitionWarnings = string.IsNullOrWhiteSpace(warning) ? "-" : warning;
            SelectedScannedAtText = _scanResult != null && _scanResult.ScannedAtUtc != default(DateTime)
                ? _scanResult.ScannedAtUtc.ToLocalTime().ToString("dd-MM-yyyy HH:mm:ss")
                : "-";
        }

        private string ResolveTreeRoot(Dictionary<string, List<string>> treeMap, string preferredRoot)
        {
            if (treeMap == null || treeMap.Count == 0)
            {
                return preferredRoot;
            }
            if (!string.IsNullOrWhiteSpace(preferredRoot))
            {
                var normalizedPreferred = NormalizeTreePath(preferredRoot);
                var matchingRoot = treeMap.Keys.FirstOrDefault(
                    key => string.Equals(NormalizeTreePath(key), normalizedPreferred, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrWhiteSpace(matchingRoot))
                {
                    return matchingRoot;
                }
            }

            var childSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in treeMap)
            {
                foreach (var child in entry.Value)
                {
                    if (!string.IsNullOrWhiteSpace(child))
                    {
                        childSet.Add(NormalizeTreePath(child));
                    }
                }
            }

            var roots = treeMap.Keys
                .Where(key => !childSet.Contains(NormalizeTreePath(key)))
                .OrderBy(key => NormalizeTreePath(key).Length)
                .ToList();
            if (roots.Count > 0)
            {
                return roots[0];
            }

            return treeMap.Keys.First();
        }

        private Dictionary<string, List<string>> BuildTreeMapFromDetails(
            Dictionary<string, FolderDetail> details,
            string fallbackRoot)
        {
            if (details == null || details.Count == 0)
            {
                if (string.IsNullOrWhiteSpace(fallbackRoot))
                {
                    return null;
                }
                return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    [fallbackRoot] = new List<string>()
                };
            }

            var normalizedRoot = NormalizeTreePath(fallbackRoot);
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in details.Keys.Where(key => !string.IsNullOrWhiteSpace(key)))
            {
                var normalizedPath = NormalizeTreePath(path);
                if (!IsWithinRoot(normalizedPath, normalizedRoot))
                {
                    continue;
                }
                var current = path;
                if (!map.ContainsKey(current))
                {
                    map[current] = new List<string>();
                }

                while (true)
                {
                    var parent = SafeGetParentPath(current);
                    if (string.IsNullOrWhiteSpace(parent))
                    {
                        break;
                    }
                    var normalizedParent = NormalizeTreePath(parent);
                    if (!IsWithinRoot(normalizedParent, normalizedRoot))
                    {
                        break;
                    }
                    if (!map.ContainsKey(parent))
                    {
                        map[parent] = new List<string>();
                    }
                    if (!map[parent].Contains(current, StringComparer.OrdinalIgnoreCase))
                    {
                        map[parent].Add(current);
                    }
                    if (!string.IsNullOrWhiteSpace(normalizedRoot)
                        && string.Equals(normalizedParent, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                    current = parent;
                }
            }

            if (!string.IsNullOrWhiteSpace(fallbackRoot) && !map.ContainsKey(fallbackRoot))
            {
                map[fallbackRoot] = new List<string>();
            }

            return map;
        }

        private Dictionary<string, List<string>> BuildTreeMapFromExportRecords(string dataPath, string fallbackRoot)
        {
            if (string.IsNullOrWhiteSpace(dataPath))
            {
                return null;
            }
            var ioPath = PathResolver.ToExtendedPath(dataPath);
            if (!File.Exists(ioPath))
            {
                return null;
            }

            var normalizedRoot = NormalizeTreePath(fallbackRoot);
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var line in File.ReadLines(ioPath))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    ExportRecord record;
                    try
                    {
                        record = Newtonsoft.Json.JsonConvert.DeserializeObject<ExportRecord>(line);
                    }
                    catch
                    {
                        continue;
                    }
                    if (record == null) continue;
                    var path = record.FolderPath;
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    if (string.Equals(record.PrincipalType, "Meta", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(record.PrincipalName, "SCAN_OPTIONS", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    AddPathWithAncestors(map, path, normalizedRoot);
                }
            }
            catch
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(fallbackRoot) && !map.ContainsKey(fallbackRoot))
            {
                map[fallbackRoot] = new List<string>();
            }

            return map.Count == 0 ? null : map;
        }

        private void AddPathWithAncestors(Dictionary<string, List<string>> map, string path)
            => AddPathWithAncestors(map, path, NormalizeTreePath(RootPath));

        private void AddPathWithAncestors(Dictionary<string, List<string>> map, string path, string normalizedRoot)
        {
            if (!IsWithinRoot(NormalizeTreePath(path), normalizedRoot))
            {
                return;
            }
            var current = path;
            if (!map.ContainsKey(current))
            {
                map[current] = new List<string>();
            }

            while (true)
            {
                var parent = SafeGetParentPath(current);
                if (string.IsNullOrWhiteSpace(parent))
                {
                    break;
                }
                var normalizedParent = NormalizeTreePath(parent);
                if (!IsWithinRoot(normalizedParent, normalizedRoot))
                {
                    break;
                }
                if (!map.ContainsKey(parent))
                {
                    map[parent] = new List<string>();
                }
                if (!map[parent].Contains(current, StringComparer.OrdinalIgnoreCase))
                {
                    map[parent].Add(current);
                }
                if (!string.IsNullOrWhiteSpace(normalizedRoot)
                    && string.Equals(normalizedParent, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
                current = parent;
            }
        }

        private string SafeGetParentPath(string path)
        {
            try
            {
                var parent = Directory.GetParent(path);
                return parent == null ? null : parent.FullName;
            }
            catch
            {
                return null;
            }
        }

        private static string NormalizeTreePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            var normalized = PathResolver.FromExtendedPath(path);
            return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static bool IsWithinRoot(string candidate, string root)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }
            if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                ? root
                : root + Path.DirectorySeparatorChar;
            return candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
        }

        private void ApplyDiffs(ScanResult result)
        {
            if (result == null || result.Details == null) return;
            var diffService = new AclDiffService();
            diffService.ApplyDiffs(result.Details);
        }

        private void LoadErrors(string path)
        {
            Errors.Clear();
            if (string.IsNullOrWhiteSpace(path)) return;
            var ioPath = PathResolver.ToExtendedPath(path);
            if (!File.Exists(ioPath)) return;
            foreach (var line in File.ReadLines(ioPath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var error = Newtonsoft.Json.JsonConvert.DeserializeObject<ErrorEntry>(line);
                    if (error != null)
                    {
                        Errors.Add(error);
                    }
                }
                catch
                {
                }
            }
        }

        private void RefreshAclFilters()
        {
            SaveUiPreferences();
            FilteredGroupEntries.Refresh();
            FilteredUserEntries.Refresh();
            FilteredAllEntries.Refresh();
            FilteredShareEntries.Refresh();
            FilteredEffectiveEntries.Refresh();
        }

        private void EnsureAtLeastOnePrincipalCategoryEnabled()
        {
            if (_showEveryone || _showAuthenticatedUsers || _showServiceAccounts || _showAdminAccounts || _showOtherPrincipals)
            {
                return;
            }

            _showOtherPrincipals = true;
            OnPropertyChanged("ShowOtherPrincipals");
        }

        private bool FilterErrors(object item)
        {
            var error = item as ErrorEntry;
            if (error == null) return false;
            return true;
        }

        private bool FilterAclEntries(object item)
        {
            var entry = item as AceEntry;
            if (entry == null) return false;
            if (!ShowAllow && !ShowDeny)
            {
                return false;
            }
            if (!ShowInherited && !ShowExplicit)
            {
                return false;
            }
            if (!ShowEveryone && !ShowAuthenticatedUsers && !ShowServiceAccounts && !ShowAdminAccounts && !ShowOtherPrincipals)
            {
                return false;
            }
            var isDeny = IsDenyEntry(entry);
            var isAllow = IsAllowEntry(entry);
            if (!ShowAllow && isAllow)
            {
                return false;
            }
            if (!ShowDeny && isDeny)
            {
                return false;
            }
            if (!ShowInherited && entry.IsInherited)
            {
                return false;
            }
            if (!ShowExplicit && !entry.IsInherited)
            {
                return false;
            }
            if (!ShowProtected && entry.IsInheritanceDisabled)
            {
                return false;
            }
            if (!ShowDisabled && entry.IsDisabled)
            {
                return false;
            }

            var isEveryone = IsEveryone(entry.PrincipalSid, entry.PrincipalName);
            var isAuthUsers = IsAuthenticatedUsers(entry.PrincipalSid, entry.PrincipalName);
            var isService = entry.IsServiceAccount;
            var isAdmin = entry.IsAdminAccount;
            var isOther = !(isEveryone || isAuthUsers || isService || isAdmin);
            if (isEveryone && !ShowEveryone)
            {
                return false;
            }
            if (isAuthUsers && !ShowAuthenticatedUsers)
            {
                return false;
            }
            if (isService && !ShowServiceAccounts)
            {
                return false;
            }
            if (isAdmin && !ShowAdminAccounts)
            {
                return false;
            }
            if (isOther && !ShowOtherPrincipals)
            {
                return false;
            }
            if (string.IsNullOrWhiteSpace(AclFilter)) return true;
            var term = AclFilter.Trim();
            if (string.IsNullOrWhiteSpace(term)) return true;
            return MatchesFilter(entry.PrincipalName, term)
                || MatchesFilter(entry.PrincipalSid, term)
                || MatchesFilter(entry.PermissionLayer.ToString(), term)
                || MatchesFilter(entry.AllowDeny, term)
                || MatchesFilter(entry.RightsSummary, term)
                || MatchesFilter(entry.EffectiveRightsSummary, term)
                || MatchesFilter(entry.FolderPath, term)
                || MatchesFilter(GetFolderName(entry.FolderPath), term)
                || MatchesFilter(entry.AuditSummary, term)
                || MatchesFilter(entry.ResourceType, term)
                || MatchesFilter(entry.TargetPath, term)
                || MatchesFilter(entry.Owner, term)
                || MatchesFilter(entry.ShareName, term)
                || MatchesFilter(entry.ShareServer, term)
                || MatchesFilter(entry.RiskLevel, term)
                || MatchesFilter(entry.Source, term)
                || MatchesFilter(entry.PathKind.ToString(), term)
                || MatchesMemberFilter(entry.MemberNames, term);
        }

        private bool IsAllowEntry(AceEntry entry)
        {
            return entry != null && string.Equals(entry.AllowDeny, "Allow", StringComparison.OrdinalIgnoreCase);
        }

        private bool IsDenyEntry(AceEntry entry)
        {
            return entry != null && string.Equals(entry.AllowDeny, "Deny", StringComparison.OrdinalIgnoreCase);
        }

        private string GetFolderName(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) return string.Empty;
            var trimmed = folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            return string.IsNullOrWhiteSpace(name) ? folderPath : name;
        }

        private bool MatchesMemberFilter(IEnumerable<string> members, string filter)
        {
            if (members == null) return false;
            return members.Any(member => MatchesFilter(member, filter));
        }

        private bool MatchesFilter(string value, string filter)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsEveryone(string sid, string name)
        {
            return string.Equals(sid, "S-1-1-0", StringComparison.OrdinalIgnoreCase)
                || MatchesFilter(name, "Everyone")
                || MatchesFilter(name, "Tutti");
        }

        private bool IsAuthenticatedUsers(string sid, string name)
        {
            return string.Equals(sid, "S-1-5-11", StringComparison.OrdinalIgnoreCase)
                || MatchesFilter(name, "Authenticated Users")
                || MatchesFilter(name, "Utenti autenticati");
        }

        private void ClearResults()
        {
            FolderTree.Clear();
            _fullTreeMap = null;
            _currentFilteredTreeMap = null;
            GroupEntries.Clear();
            UserEntries.Clear();
            AllEntries.Clear();
            ShareEntries.Clear();
            EffectiveEntries.Clear();
            Errors.Clear();
            SelectedFolderPath = string.Empty;
            ProcessedCount = 0;
            ProcessedFilesCount = 0;
            ErrorCount = 0;
            ElapsedText = "00:00:00";
            CurrentPathText = string.Empty;
            CurrentPathBackground = "Transparent";
            AclFilter = string.Empty;
            ShowAllow = true;
            ShowDeny = true;
            ShowInherited = true;
            ShowExplicit = true;
            ShowProtected = true;
            ShowDisabled = true;
            ShowEveryone = true;
            ShowAuthenticatedUsers = true;
            ShowServiceAccounts = true;
            ShowAdminAccounts = true;
            ShowOtherPrincipals = true;
            ResetTreeFilters(false);
            UpdateSummary(null);
        }

        private void ResetTreeFilters()
        {
            ResetTreeFilters(true);
        }

        private void ResetTreeFilters(bool reloadTree)
        {
            _treeFilterExplicitOnly = false;
            _treeFilterInheritanceDisabledOnly = false;
            _treeFilterDiffOnly = false;
            _treeFilterExplicitDenyOnly = false;
            _treeFilterBaselineMismatchOnly = false;
            _treeFilterFilesOnly = true;
            _treeFilterFoldersOnly = true;
            OnPropertyChanged("TreeFilterExplicitOnly");
            OnPropertyChanged("TreeFilterInheritanceDisabledOnly");
            OnPropertyChanged("TreeFilterDiffOnly");
            OnPropertyChanged("TreeFilterExplicitDenyOnly");
            OnPropertyChanged("TreeFilterBaselineMismatchOnly");
            OnPropertyChanged("TreeFilterFilesOnly");
            OnPropertyChanged("TreeFilterFoldersOnly");

            if (_scanResult != null && _fullTreeMap != null && _fullTreeMap.Count > 0)
            {
                var root = ResolveTreeRoot(_fullTreeMap, _scanResult.RootPath);
                if (!string.IsNullOrWhiteSpace(root)
                    && !string.Equals(RootPath, root, StringComparison.OrdinalIgnoreCase))
                {
                    RootPath = root;
                }
            }

            if (reloadTree)
            {
                ReloadTreeWithFilters();
            }
        }

        private void UpdateDfsTargets()
        {
            var targets = PathResolver.GetDfsTargets(RootPath);
            var nextTargets = new ObservableCollection<string>(targets ?? new List<string>());
            var previousSelection = SelectedDfsTarget;
            DfsTargets = nextTargets;
            if (DfsTargets.Count == 0)
            {
                SelectedDfsTarget = string.Empty;
                return;
            }
            var selected = string.IsNullOrWhiteSpace(previousSelection)
                ? null
                : DfsTargets.FirstOrDefault(target => string.Equals(target, previousSelection, StringComparison.OrdinalIgnoreCase));
            SelectedDfsTarget = selected ?? DfsTargets[0];
            OnPropertyChanged("HasDfsTargets");
            OnPropertyChanged("DfsTargetBackground");
        }

        private void ApplyIdentityDependencies()
        {
            if (_resolveIdentities) return;
            if (_expandGroups)
            {
                _expandGroups = false;
                OnPropertyChanged("ExpandGroups");
            }
            if (_usePowerShell)
            {
                _usePowerShell = false;
                OnPropertyChanged("UsePowerShell");
            }
            if (_excludeServiceAccounts)
            {
                _excludeServiceAccounts = false;
                OnPropertyChanged("ExcludeServiceAccounts");
            }
            if (_excludeAdminAccounts)
            {
                _excludeAdminAccounts = false;
                OnPropertyChanged("ExcludeAdminAccounts");
            }
        }

        private void ApplyAdvancedAuditDependencies()
        {
            if (_enableAdvancedAudit) return;
            if (_computeEffectiveAccess)
            {
                _computeEffectiveAccess = false;
                OnPropertyChanged("ComputeEffectiveAccess");
            }
            if (_includeSharePermissions)
            {
                _includeSharePermissions = false;
                OnPropertyChanged("IncludeSharePermissions");
            }
            if (_includeFiles)
            {
                _includeFiles = false;
                OnPropertyChanged("IncludeFiles");
            }
            if (_readOwnerAndSacl)
            {
                _readOwnerAndSacl = false;
                OnPropertyChanged("ReadOwnerAndSacl");
            }
            if (_compareBaseline)
            {
                _compareBaseline = false;
                OnPropertyChanged("CompareBaseline");
            }
        }

        private void ApplyImportedOptions(ScanOptions options)
        {
            if (options == null) return;
            ScanAllDepths = options.ScanAllDepths;
            if (!options.ScanAllDepths && options.MaxDepth > 0)
            {
                MaxDepth = ClampMaxDepth(options.MaxDepth);
            }
            IncludeInherited = options.IncludeInherited;
            ResolveIdentities = options.ResolveIdentities;
            ExcludeServiceAccounts = options.ResolveIdentities && options.ExcludeServiceAccounts;
            ExcludeAdminAccounts = options.ResolveIdentities && options.ExcludeAdminAccounts;
            ExpandGroups = options.ResolveIdentities && options.ExpandGroups;
            UsePowerShell = options.ResolveIdentities && options.UsePowerShell;
            EnableAdvancedAudit = options.EnableAdvancedAudit;
            ComputeEffectiveAccess = options.EnableAdvancedAudit && options.ComputeEffectiveAccess;
            IncludeSharePermissions = options.EnableAdvancedAudit && options.IncludeSharePermissions;
            IncludeFiles = options.EnableAdvancedAudit && options.IncludeFiles;
            ReadOwnerAndSacl = options.EnableAdvancedAudit && options.ReadOwnerAndSacl;
            CompareBaseline = options.EnableAdvancedAudit && options.CompareBaseline;
        }

        private void UpdateCommands()
        {
            OnPropertyChanged("IsScanning");
            OnPropertyChanged("IsNotScanning");
            OnPropertyChanged("IsScanConfigEnabled");
            OnPropertyChanged("HasScanResult");
            OnPropertyChanged("HasUnexportedData");
            OnPropertyChanged("StatusText");
            OnPropertyChanged("StatusBrush");
            OnPropertyChanged("CanStart");
            OnPropertyChanged("CanStop");
            OnPropertyChanged("CanExport");
            OnPropertyChanged("CanImportAnalysis");
            OnPropertyChanged("IsBusy");
            OnPropertyChanged("IsNotBusy");
            StartCommand.RaiseCanExecuteChanged();
            AddScanRootCommand.RaiseCanExecuteChanged();
            RemoveScanRootCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
            ExportCommand.RaiseCanExecuteChanged();
            ExportAnalysisCommand.RaiseCanExecuteChanged();
            ImportAnalysisCommand.RaiseCanExecuteChanged();
            InstallServiceCommand.RaiseCanExecuteChanged();
            UninstallServiceCommand.RaiseCanExecuteChanged();
            ResetTreeFiltersCommand.RaiseCanExecuteChanged();
        }

        private string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalDays >= 1)
            {
                return elapsed.ToString(@"d\.hh\:mm\:ss");
            }

            return elapsed.ToString(@"hh\:mm\:ss");
        }

        private string BuildExportPath(string folder, string rootPath, string extension)
        {
            var fileName = BuildExportFileName(rootPath, extension);
            return Path.Combine(folder, fileName);
        }

        private string BuildExportFileName(string rootPath, string extension)
        {
            var safeRoot = rootPath ?? string.Empty;
            var baseName = BuildScanNameFromRoot(safeRoot);
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "Root";
            var timestamp = DateTime.Now.ToString("yyyy_MM_dd_HH_mm");
            return string.Format("{0}_{1}.{2}", baseName, timestamp, extension);
        }

        private string ResolveInitialDirectory(string preferredDirectory, string rootPath)
        {
            if (!string.IsNullOrWhiteSpace(preferredDirectory) && Directory.Exists(preferredDirectory))
            {
                return preferredDirectory;
            }

            if (!string.IsNullOrWhiteSpace(rootPath))
            {
                var candidate = rootPath;
                if (!Directory.Exists(candidate))
                {
                    candidate = Path.GetDirectoryName(rootPath);
                }

                if (!string.IsNullOrWhiteSpace(candidate) && Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        private void UpdateLastDirectory(ref string targetDirectory, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                targetDirectory = directory;
            }
        }

        private int ClampMaxDepth(int value)
        {
            return value < 1 ? 1 : value;
        }

        private void InitializeScanTimer()
        {
            _scanTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _scanTimer.Tick += (_, __) =>
            {
                if (!_isScanning) return;
                ElapsedText = FormatElapsed(DateTime.Now - _scanStart);
            };
        }

        private void StartElapsedTimer()
        {
            _scanStart = DateTime.Now;
            if (_scanTimer != null)
            {
                _scanTimer.Stop();
                _scanTimer.Start();
            }
        }

        private void StopElapsedTimer()
        {
            if (_scanTimer != null)
            {
                _scanTimer.Stop();
            }
        }

        private void SetBusy(bool isBusy)
        {
            if (_isBusy == isBusy) return;
            _isBusy = isBusy;
            UpdateCommands();
        }

        private bool ValidateImportedResult(ScanResult result, out string message)
        {
            if (result == null)
            {
                message = "Analisi importata non valida: dati mancanti.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(result.TempDataPath))
            {
                message = "Analisi importata non valida: file dati non presente.";
                return false;
            }
            var dataPath = PathResolver.ToExtendedPath(result.TempDataPath);
            if (!File.Exists(dataPath))
            {
                message = "Analisi importata non valida: file dati non trovato.";
                return false;
            }
            if (new FileInfo(dataPath).Length == 0)
            {
                message = "Analisi importata non valida: file dati vuoto.";
                return false;
            }
            if (result.Details == null || result.TreeMap == null)
            {
                message = "Analisi importata non valida: struttura dati incompleta (TreeMap/Details mancanti).";
                return false;
            }
            if (result.TreeMap.Count == 0)
            {
                message = "Analisi importata con albero cartelle vuoto.";
                return false;
            }
            if (result.Details.Count == 0)
            {
                message = "Analisi importata senza dettagli ACL.";
                return false;
            }
            message = null;
            return true;
        }

        private void LoadCache()
        {
            var cachePath = _cacheStore.GetCacheFilePath("sid-cache.json");
            _sidNameCache.Load(cachePath);
            _uiPreferencesPath = _cacheStore.GetCacheFilePath("ui-preferences.json");
            LoadUiPreferences();
        }

        private void SaveCache()
        {
            var cachePath = _cacheStore.GetCacheFilePath("sid-cache.json");
            _sidNameCache.Save(cachePath);
            SaveUiPreferences();
        }


        private void LoadUiPreferences()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_uiPreferencesPath) || !File.Exists(_uiPreferencesPath)) return;
                var json = File.ReadAllText(_uiPreferencesPath);
                var prefs = JsonConvert.DeserializeObject<UiPreferences>(json);
                if (prefs == null) return;

                ShowAllow = prefs.ShowAllow;
                ShowDeny = prefs.ShowDeny;
                ShowInherited = prefs.ShowInherited;
                ShowExplicit = prefs.ShowExplicit;
                ShowProtected = prefs.ShowProtected;
                ShowDisabled = prefs.ShowDisabled;
                ShowEveryone = prefs.ShowEveryone;
                ShowAuthenticatedUsers = prefs.ShowAuthenticatedUsers;
                ShowServiceAccounts = prefs.ShowServiceAccounts;
                ShowAdminAccounts = prefs.ShowAdminAccounts;
                ShowOtherPrincipals = prefs.ShowOtherPrincipals;
                TreeFilterExplicitOnly = prefs.TreeFilterExplicitOnly;
                TreeFilterInheritanceDisabledOnly = prefs.TreeFilterInheritanceDisabledOnly;
                TreeFilterDiffOnly = prefs.TreeFilterDiffOnly;
                TreeFilterExplicitDenyOnly = prefs.TreeFilterExplicitDenyOnly;
                TreeFilterBaselineMismatchOnly = prefs.TreeFilterBaselineMismatchOnly;
                TreeFilterFilesOnly = prefs.TreeFilterFilesOnly;
                TreeFilterFoldersOnly = prefs.TreeFilterFoldersOnly;
                AuditOutputDirectory = prefs.AuditOutputDirectory;
                UseWindowsServiceMode = prefs.UseWindowsServiceMode;
                ScanRoots.Clear();
                _scanRootDfsTargets.Clear();
                _scanRootNamespacePaths.Clear();
                if (prefs.ScanRoots != null)
                {
                    foreach (var root in prefs.ScanRoots.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        ScanRoots.Add(root);
                    }
                }
                if (prefs.ScanRootTargets != null)
                {
                    foreach (var target in prefs.ScanRootTargets.Where(item => item != null && !string.IsNullOrWhiteSpace(item.RootPath)))
                    {
                        var key = GetScanRootKey(target.RootPath);
                        _scanRootDfsTargets[key] = target.DfsTarget;
                        if (!string.IsNullOrWhiteSpace(target.NamespacePath))
                        {
                            _scanRootNamespacePaths[key] = target.NamespacePath;
                        }
                    }
                }
                if (ScanRoots.Count > 0)
                {
                    SelectedScanRoot = ScanRoots[0];
                }
            }
            catch
            {
            }
        }

        private void SaveUiPreferences()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(_uiPreferencesPath)) return;
                var prefs = new UiPreferences
                {
                    ShowAllow = ShowAllow,
                    ShowDeny = ShowDeny,
                    ShowInherited = ShowInherited,
                    ShowExplicit = ShowExplicit,
                    ShowProtected = ShowProtected,
                    ShowDisabled = ShowDisabled,
                    ShowEveryone = ShowEveryone,
                    ShowAuthenticatedUsers = ShowAuthenticatedUsers,
                    ShowServiceAccounts = ShowServiceAccounts,
                    ShowAdminAccounts = ShowAdminAccounts,
                    ShowOtherPrincipals = ShowOtherPrincipals,
                    TreeFilterExplicitOnly = TreeFilterExplicitOnly,
                    TreeFilterInheritanceDisabledOnly = TreeFilterInheritanceDisabledOnly,
                    TreeFilterDiffOnly = TreeFilterDiffOnly,
                    TreeFilterExplicitDenyOnly = TreeFilterExplicitDenyOnly,
                    TreeFilterBaselineMismatchOnly = TreeFilterBaselineMismatchOnly,
                    TreeFilterFilesOnly = TreeFilterFilesOnly,
                    TreeFilterFoldersOnly = TreeFilterFoldersOnly,
                    AuditOutputDirectory = AuditOutputDirectory,
                    UseWindowsServiceMode = UseWindowsServiceMode,
                    ScanRoots = ScanRoots.ToList(),
                    ScanRootTargets = _scanRootDfsTargets.Select(item => new ScanRootTargetPreference
                    {
                        RootPath = GetScanRootKey(item.Key),
                        DfsTarget = item.Value,
                        NamespacePath = _scanRootNamespacePaths.ContainsKey(item.Key) ? _scanRootNamespacePaths[item.Key] : null
                    }).ToList()
                };
                File.WriteAllText(_uiPreferencesPath, JsonConvert.SerializeObject(prefs, Formatting.Indented));
            }
            catch
            {
            }
        }

        private sealed class UiPreferences
        {
            public bool ShowAllow { get; set; } = true;
            public bool ShowDeny { get; set; } = true;
            public bool ShowInherited { get; set; } = true;
            public bool ShowExplicit { get; set; } = true;
            public bool ShowProtected { get; set; } = true;
            public bool ShowDisabled { get; set; } = true;
            public bool ShowEveryone { get; set; } = true;
            public bool ShowAuthenticatedUsers { get; set; } = true;
            public bool ShowServiceAccounts { get; set; } = true;
            public bool ShowAdminAccounts { get; set; } = true;
            public bool ShowOtherPrincipals { get; set; } = true;
            public bool TreeFilterExplicitOnly { get; set; }
            public bool TreeFilterInheritanceDisabledOnly { get; set; }
            public bool TreeFilterDiffOnly { get; set; }
            public bool TreeFilterExplicitDenyOnly { get; set; }
            public bool TreeFilterBaselineMismatchOnly { get; set; }
            public bool TreeFilterFilesOnly { get; set; } = true;
            public bool TreeFilterFoldersOnly { get; set; } = true;
            public string AuditOutputDirectory { get; set; }
            public bool UseWindowsServiceMode { get; set; }
            public List<string> ScanRoots { get; set; }
            public List<ScanRootTargetPreference> ScanRootTargets { get; set; }
        }

        private sealed class ScanRootTargetPreference
        {
            public string RootPath { get; set; }
            public string DfsTarget { get; set; }
            public string NamespacePath { get; set; }
        }

        private void OnPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private void RunOnUi(Action action)
        {
            var dispatcher = System.Windows.Application.Current == null ? null : System.Windows.Application.Current.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished || dispatcher.CheckAccess())
            {
                try
                {
                    action();
                }
                catch
                {
                }
                return;
            }
            try
            {
                dispatcher.Invoke(action);
            }
            catch
            {
            }
        }
    }
}
