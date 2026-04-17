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
        private readonly ScanBatchExecutionService _scanBatchExecutionService;
        private readonly AnalysisSqliteStore _analysisSqliteStore;
        private readonly RuntimeCleanupService _runtimeCleanupService;
        private readonly ServiceRuntimeStatusPresenter _serviceRuntimeStatusPresenter;
        private readonly ScanCredentialStore _scanCredentialStore;
        private readonly ServiceScheduleFileStore _serviceScheduleStore;
        private readonly ServiceSchedulePlanner _serviceSchedulePlanner;
        private ScanResult _scanResult;
        private CancellationTokenSource _cts;
        private bool _isScanning;
        private bool _isBusy;
        private bool _isViewerMode;
        private bool _isSettingsOpen;
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
        private string _globalCredentialUserName;
        private string _globalCredentialPassword;
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
        private bool _readOwnerAndSacl;
        private bool _compareBaseline = true;
        private ScanPathCompatibilityEvaluation _pathCompatibility = new ScanPathCompatibilityEvaluation();
        private string _progressText = LocalizationManager.Text("Common.Ready");
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
        private bool _selectedInheritanceDisabled;
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
        private string _serviceRuntimeStatusText = LocalizationManager.Text("Service.Waiting");
        private bool _isServiceRuntimeRunning;
        private bool _isServiceInstalled;
        private string _serviceBadgeText = LocalizationManager.Text("Service.NotInstalledBadge");
        private string _serviceBadgeBackground = "#FF9E9E9E";
        private string _serviceNextRunText = "-";
        private string _serviceScheduleSummaryText = "-";
        private LocaleOption _selectedLocale;
        private ObservableCollection<ResultHierarchyNodeViewModel> _resultHierarchy = new ObservableCollection<ResultHierarchyNodeViewModel>();
        private ObservableCollection<ServiceScheduleItemViewModel> _serviceSchedules = new ObservableCollection<ServiceScheduleItemViewModel>();
        private ServiceScheduleItemViewModel _selectedServiceSchedule;
        private string _scheduleName = string.Empty;
        private ServiceScheduleFrequencyKind _scheduleFrequencyKind = ServiceScheduleFrequencyKind.Daily;
        private DateTime _scheduleOneShotDate = DateTime.Today;
        private DateTime _scheduleTimeOfDay = DateTime.Today.AddHours(9);
        private string _scheduleTimeText = DateTime.Today.AddHours(9).ToString("HH:mm");
        private string _scheduleEditorFeedbackText = string.Empty;
        private DayOfWeek _scheduleDayOfWeek = DayOfWeek.Monday;
        private int _scheduleDayOfMonth = 1;
        private bool _scheduleEnabled = true;
        private const string ServiceName = "NtfsAuditWorker";

        public MainViewModel(bool viewerMode = false)
            : this(viewerMode, null)
        {
        }

        internal MainViewModel(ScanCredentialStore scanCredentialStore, bool viewerMode = false)
            : this(viewerMode, scanCredentialStore)
        {
        }

        private MainViewModel(bool viewerMode, ScanCredentialStore scanCredentialStore)
        {
            _isViewerMode = viewerMode;
            _cacheStore = new LocalCacheStore();
            _sidNameCache = new SidNameCache();
            _groupMembershipCache = new GroupMembershipCache(TimeSpan.FromHours(2));
            _excelExporter = new ExcelExporter();
            _analysisArchive = new AnalysisArchive();
            _scanBatchExecutionService = new ScanBatchExecutionService(_analysisArchive);
            _analysisSqliteStore = new AnalysisSqliteStore();
            _runtimeCleanupService = new RuntimeCleanupService();
            _serviceRuntimeStatusPresenter = new ServiceRuntimeStatusPresenter();
            _scanCredentialStore = scanCredentialStore ?? new ScanCredentialStore();
            _serviceScheduleStore = new ServiceScheduleFileStore();
            _serviceSchedulePlanner = new ServiceSchedulePlanner();
            _selectedLocale = LocalizationManager.ResolveLocale(LocalizationManager.CurrentLocale);
            LocalizationManager.LocaleChanged += OnLocaleChanged;

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
            SaveGlobalCredentialCommand = new RelayCommand(SaveGlobalCredential, () => !_isViewerMode && !IsBusy);
            ClearGlobalCredentialCommand = new RelayCommand(ClearGlobalCredential, () => !_isViewerMode && !IsBusy && HasGlobalCredential);
            ApplyCompatibleScanOptionsCommand = new RelayCommand(ApplyCompatibleScanOptions, () => !_isViewerMode && !IsBusy);
            ToggleSettingsCommand = new RelayCommand(ToggleSettings, () => !_isViewerMode);
            CloseSettingsCommand = new RelayCommand(CloseSettings, () => !_isViewerMode && IsSettingsOpen);
            RefreshSchedulesCommand = new RelayCommand(RefreshServiceSchedules, () => !_isViewerMode);
            StartServiceRuntimeCommand = new RelayCommand(StartServiceRuntime, () => !_isViewerMode && IsServiceInstalled);
            StopServiceRuntimeCommand = new RelayCommand(StopServiceRuntime, () => !_isViewerMode && IsServiceInstalled);
            NewScheduleCommand = new RelayCommand(PrepareNewSchedule, () => CanManageServiceSchedules);
            SaveScheduleCommand = new RelayCommand(SaveScheduleDefinition, () => CanSaveSchedule);
            DeleteScheduleCommand = new RelayCommand(DeleteScheduleDefinition, () => CanDeleteSchedule);

            LoadCache();
            LoadCredentialSettings();
            RefreshCompatibilityState();
            RefreshServiceSchedules();
            InitializeScanTimer();
            InitializeServiceStatusMonitor();
        }
    }
}
