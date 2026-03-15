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
    public partial class MainViewModel : INotifyPropertyChanged
    {
        private readonly LocalCacheStore _cacheStore;
        private readonly SidNameCache _sidNameCache;
        private readonly GroupMembershipCache _groupMembershipCache;
        private readonly ExcelExporter _excelExporter;
        private readonly AnalysisArchive _analysisArchive;
        private readonly AnalysisSqliteStore _analysisSqliteStore;
        private readonly RuntimeCleanupService _runtimeCleanupService;
        private readonly ServiceRuntimeStatusPresenter _serviceRuntimeStatusPresenter;
        private ScanResult _scanResult;
        private CancellationTokenSource _cts;
        private bool _isScanning;
        private bool _isBusy;
        private bool _isViewerMode;
        private DispatcherTimer _scanTimer;
        private DispatcherTimer _serviceStatusTimer;
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
        private bool _suspendUiPreferencePersistence;
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
        private const int MaxErrorsToLoad = 10000;
        private bool _errorsTruncated;
        private string _serviceRuntimeStatusText = "Servizio: in attesa";
        private bool _isServiceRuntimeRunning;
        private bool _isServiceInstalled;
        private string _serviceBadgeText = "Servizio non installato";
        private string _serviceBadgeBackground = "#FF9E9E9E";
        private const string ServiceName = "NtfsAuditWorker";

        public MainViewModel(bool viewerMode = false)
        {
            _isViewerMode = viewerMode;
            _cacheStore = new LocalCacheStore();
            _sidNameCache = new SidNameCache();
            _groupMembershipCache = new GroupMembershipCache(TimeSpan.FromHours(2));
            _excelExporter = new ExcelExporter();
            _analysisArchive = new AnalysisArchive();
            _analysisSqliteStore = new AnalysisSqliteStore();
            _runtimeCleanupService = new RuntimeCleanupService();
            _serviceRuntimeStatusPresenter = new ServiceRuntimeStatusPresenter();

            FolderTree = new ObservableCollection<FolderNodeViewModel>();
            ScanRoots = new ObservableCollection<string>();
            ScanRoots.CollectionChanged += (_, __) => OnScanRootsCollectionChanged();
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
            InstallServiceCommand = new RelayCommand(InstallService, () => !_isViewerMode && !IsBusy && !IsServiceInstalled);
            UninstallServiceCommand = new RelayCommand(UninstallService, () => !_isViewerMode && !IsBusy && IsServiceInstalled);
            StartCommand = new RelayCommand(StartScan, () => CanStart);
            StopCommand = new RelayCommand(StopScan, () => CanStop);
            ExportCommand = new RelayCommand(Export, () => CanExport);
            ImportAnalysisCommand = new RelayCommand(ImportAnalysis, () => !_isScanning && !IsBusy);
            ResetTreeFiltersCommand = new RelayCommand(ResetTreeFilters, () => HasScanResult);
            CleanupResidualFilesCommand = new RelayCommand(CleanupResidualFiles, () => !_isViewerMode && !IsBusy);
            SaveScanRootSetCommand = new RelayCommand(SaveScanRootSet, () => !_isViewerMode && !IsBusy && ScanRoots.Count > 0);
            LoadScanRootSetCommand = new RelayCommand(LoadScanRootSet, () => !_isViewerMode && !IsBusy);

            LoadCache();
            InitializeScanTimer();
            InitializeServiceStatusMonitor();
        }

        public bool IsElevated
        {
            get { return _isElevated; }
        }

        public bool IsNotElevated
        {
            get { return !_isElevated; }
        }

        public bool IsServiceRuntimeRunning
        {
            get { return _isServiceRuntimeRunning; }
            private set
            {
                _isServiceRuntimeRunning = value;
                OnPropertyChanged("IsServiceRuntimeRunning");
                OnPropertyChanged("StatusText");
                OnPropertyChanged("StatusBrush");
                OnPropertyChanged("CanStop");
                if (StopCommand != null)
                {
                    StopCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsServiceInstalled
        {
            get { return _isServiceInstalled; }
            private set
            {
                _isServiceInstalled = value;
                OnPropertyChanged("IsServiceInstalled");
                if (InstallServiceCommand != null)
                {
                    InstallServiceCommand.RaiseCanExecuteChanged();
                }
                if (UninstallServiceCommand != null)
                {
                    UninstallServiceCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string ServiceRuntimeStatusText
        {
            get { return _serviceRuntimeStatusText; }
            private set
            {
                _serviceRuntimeStatusText = value;
                OnPropertyChanged("ServiceRuntimeStatusText");
            }
        }

        public string ServiceBadgeText
        {
            get { return _serviceBadgeText; }
            private set
            {
                _serviceBadgeText = value;
                OnPropertyChanged("ServiceBadgeText");
            }
        }

        public string ServiceBadgeBackground
        {
            get { return _serviceBadgeBackground; }
            private set
            {
                _serviceBadgeBackground = value;
                OnPropertyChanged("ServiceBadgeBackground");
            }
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
        public RelayCommand ImportAnalysisCommand { get; private set; }
        public RelayCommand ResetTreeFiltersCommand { get; private set; }
        public RelayCommand CleanupResidualFilesCommand { get; private set; }
        public RelayCommand SaveScanRootSetCommand { get; private set; }
        public RelayCommand LoadScanRootSetCommand { get; private set; }

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
                SaveUiPreferences();
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
            set { _treeFilterExplicitOnly = value; OnPropertyChanged("TreeFilterExplicitOnly"); ReloadTreeWithFilters(); SaveUiPreferences(); }
        }

        public bool TreeFilterInheritanceDisabledOnly
        {
            get { return _treeFilterInheritanceDisabledOnly; }
            set { _treeFilterInheritanceDisabledOnly = value; OnPropertyChanged("TreeFilterInheritanceDisabledOnly"); ReloadTreeWithFilters(); SaveUiPreferences(); }
        }

        public bool TreeFilterDiffOnly
        {
            get { return _treeFilterDiffOnly; }
            set { _treeFilterDiffOnly = value; OnPropertyChanged("TreeFilterDiffOnly"); ReloadTreeWithFilters(); SaveUiPreferences(); }
        }

        public bool TreeFilterExplicitDenyOnly
        {
            get { return _treeFilterExplicitDenyOnly; }
            set { _treeFilterExplicitDenyOnly = value; OnPropertyChanged("TreeFilterExplicitDenyOnly"); ReloadTreeWithFilters(); SaveUiPreferences(); }
        }

        public bool TreeFilterBaselineMismatchOnly
        {
            get { return _treeFilterBaselineMismatchOnly; }
            set { _treeFilterBaselineMismatchOnly = value; OnPropertyChanged("TreeFilterBaselineMismatchOnly"); ReloadTreeWithFilters(); SaveUiPreferences(); }
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
                SaveUiPreferences();
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
                SaveUiPreferences();
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
                if (_isScanning || IsServiceRuntimeRunning) return "RUNNING";
                if (_scanResult == null) return "IDLE";
                return "FINISHED";
            }
        }
        public string StatusBrush
        {
            get
            {
                if (_isScanning || IsServiceRuntimeRunning) return "#FF2E7D32";
                if (_scanResult == null) return "#FFFFB300";
                return "#FF1565C0";
            }
        }

        public bool CanStart { get { return !_isViewerMode && !_isScanning && !IsBusy && (ScanRoots.Count > 0 || !string.IsNullOrWhiteSpace(RootPath)); } }
        public bool CanStop { get { return !_isViewerMode && !IsBusy && (_isScanning || IsServiceRuntimeRunning); } }
        public bool CanExport { get { return !_isViewerMode && !_isScanning && !IsBusy && _scanResult != null; } }
        public bool HasUnexportedData { get { return !_isViewerMode && _scanResult != null && !_hasExported; } }

        public event PropertyChangedEventHandler PropertyChanged;

        public void SelectFolder(string path)
        {
            if (_scanResult == null || string.IsNullOrWhiteSpace(path)) return;
            var detail = GetFolderDetail(path);
            if (detail == null) return;

            SelectedFolderPath = path;
            GroupEntries.Clear();
            UserEntries.Clear();
            AllEntries.Clear();
            ShareEntries.Clear();
            EffectiveEntries.Clear();

            foreach (var entry in detail.AllEntries)
            {
                AllEntries.Add(entry);
                if (entry.PermissionLayer != PermissionLayer.Ntfs)
                {
                    continue;
                }

                if (string.Equals(entry.PrincipalType, "Group", StringComparison.OrdinalIgnoreCase))
                {
                    GroupEntries.Add(entry);
                }
                else
                {
                    UserEntries.Add(entry);
                }
            }
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
            SummaryFilesCount = detail.HasFileEntries
                ? entries.Count(entry => string.Equals(entry.ResourceType, "File", StringComparison.OrdinalIgnoreCase))
                : 0;
            SummaryBaselineAdded = detail.BaselineSummary == null ? 0 : detail.BaselineSummary.Added.Count;
            SummaryBaselineRemoved = detail.BaselineSummary == null ? 0 : detail.BaselineSummary.Removed.Count;
        }

        public Task<ResolvedPrincipal[]> GetGroupMembersAsync(string groupSid)
        {
            if (string.IsNullOrWhiteSpace(groupSid)) return Task.FromResult(new ResolvedPrincipal[0]);
            return Task.Run(() =>
            {
                var resolver = CreateResolver(UsePowerShell);
                var members = resolver.GetGroupMembers(groupSid);
                return (members ?? new List<ResolvedPrincipal>()).ToArray();
            });
        }

        public Task<ResolvedPrincipal[]> GetUserGroupsAsync(string userSid)
        {
            if (string.IsNullOrWhiteSpace(userSid)) return Task.FromResult(new ResolvedPrincipal[0]);
            return Task.Run(() =>
            {
                var resolver = CreateResolver(UsePowerShell);
                var groups = resolver.GetUserGroups(userSid);
                return (groups ?? new List<ResolvedPrincipal>()).ToArray();
            });
        }
    }
}
