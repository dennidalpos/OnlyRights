using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Forms;
using System.Windows.Threading;
using Win32 = Microsoft.Win32;
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
        private readonly HtmlExporter _htmlExporter;
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
        private int _errorCount;
        private string _elapsedText = "00:00:00";
        private string _selectedFolderPath;
        private string _aclFilter;
        private bool _colorizeRights = true;
        private ObservableCollection<string> _dfsTargets = new ObservableCollection<string>();
        private string _selectedDfsTarget;
        private bool _filterEveryone;
        private bool _filterAuthenticatedUsers;
        private bool _filterDenyOnly;
        private bool _filterInheritanceDisabled;
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

        public MainViewModel(bool viewerMode = false)
        {
            _isViewerMode = viewerMode;
            _cacheStore = new LocalCacheStore();
            _sidNameCache = new SidNameCache();
            _groupMembershipCache = new GroupMembershipCache(TimeSpan.FromHours(2));
            _excelExporter = new ExcelExporter();
            _htmlExporter = new HtmlExporter();
            _analysisArchive = new AnalysisArchive();

            FolderTree = new ObservableCollection<FolderNodeViewModel>();
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
            StartCommand = new RelayCommand(StartScan, () => CanStart);
            StopCommand = new RelayCommand(StopScan, () => CanStop);
            ExportCommand = new RelayCommand(Export, () => CanExport);
            ExportAnalysisCommand = new RelayCommand(ExportAnalysis, () => CanExport);
            ExportHtmlCommand = new RelayCommand(ExportHtml, () => CanExport);
            ImportAnalysisCommand = new RelayCommand(ImportAnalysis, () => !_isScanning && !IsBusy);

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
        public RelayCommand StartCommand { get; private set; }
        public RelayCommand StopCommand { get; private set; }
        public RelayCommand ExportCommand { get; private set; }
        public RelayCommand ExportAnalysisCommand { get; private set; }
        public RelayCommand ExportHtmlCommand { get; private set; }
        public RelayCommand ImportAnalysisCommand { get; private set; }

        public string RootPath
        {
            get { return _rootPath; }
            set
            {
                _rootPath = value;
                OnPropertyChanged("RootPath");
                UpdateDfsTargets();
                OnPropertyChanged("CanStart");
                StartCommand.RaiseCanExecuteChanged();
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

        public bool FilterEveryone
        {
            get { return _filterEveryone; }
            set
            {
                _filterEveryone = value;
                OnPropertyChanged("FilterEveryone");
                RefreshAclFilters();
            }
        }

        public bool FilterAuthenticatedUsers
        {
            get { return _filterAuthenticatedUsers; }
            set
            {
                _filterAuthenticatedUsers = value;
                OnPropertyChanged("FilterAuthenticatedUsers");
                RefreshAclFilters();
            }
        }

        public bool FilterDenyOnly
        {
            get { return _filterDenyOnly; }
            set
            {
                _filterDenyOnly = value;
                OnPropertyChanged("FilterDenyOnly");
                RefreshAclFilters();
            }
        }

        public bool FilterInheritanceDisabled
        {
            get { return _filterInheritanceDisabled; }
            set
            {
                _filterInheritanceDisabled = value;
                OnPropertyChanged("FilterInheritanceDisabled");
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
        public bool IsViewerMode { get { return _isViewerMode; } }
        public bool IsNotViewerMode { get { return !_isViewerMode; } }
        public bool IsScanConfigEnabled { get { return !_isViewerMode && !_isScanning; } }
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

        public bool CanStart { get { return !_isViewerMode && !_isScanning && !IsBusy && !string.IsNullOrWhiteSpace(RootPath); } }
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
            if (IsElevated)
            {
                return;
            }
            var mappedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var networkKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey("Network"))
            {
                if (networkKey != null)
                {
                    foreach (var driveLetter in networkKey.GetSubKeyNames())
                    {
                        using (var driveKey = networkKey.OpenSubKey(driveLetter))
                        {
                            var remotePath = driveKey?.GetValue("RemotePath") as string;
                            if (!string.IsNullOrWhiteSpace(remotePath))
                            {
                                mappedPaths.Add(remotePath);
                            }
                        }
                    }
                }
            }

            foreach (var remotePath in mappedPaths)
            {
                TryAddPlace(dialog, remotePath);
            }

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Network)
                {
                    continue;
                }

                var uncPath = PathResolver.NormalizeRootPath(drive.Name, false);
                if (TryAddPlace(dialog, uncPath))
                {
                    continue;
                }

                TryAddPlace(dialog, drive.Name);
            }
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
            var inputRoot = string.IsNullOrWhiteSpace(SelectedDfsTarget) ? RootPath : SelectedDfsTarget;
            if (!string.IsNullOrWhiteSpace(inputRoot) && inputRoot.StartsWith(@"\\", StringComparison.Ordinal))
            {
                var normalizedRoot = PathResolver.NormalizeRootPath(inputRoot, string.IsNullOrWhiteSpace(SelectedDfsTarget));
                if (!string.IsNullOrWhiteSpace(normalizedRoot) &&
                    !string.Equals(normalizedRoot, inputRoot, StringComparison.OrdinalIgnoreCase))
                {
                    inputRoot = normalizedRoot;
                }
            }

            var ioRootPath = PathResolver.ToExtendedPath(inputRoot);
            if (!Directory.Exists(ioRootPath))
            {
                ProgressText = "Percorso non valido";
                return;
            }

            _isScanning = true;
            CurrentPathText = string.Empty;
            ElapsedText = "00:00:00";
            StartElapsedTimer();
            UpdateCommands();
            ClearResults();

            _cts = new CancellationTokenSource();
            var options = new ScanOptions
            {
                RootPath = inputRoot,
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
                CompareBaseline = EnableAdvancedAudit && CompareBaseline
            };

            Task.Run(() => ExecuteScan(options, _cts.Token));
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
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.ShowNewFolderButton = true;
                var result = dialog.ShowDialog();
                if (result != DialogResult.OK) return;
                var outputPath = BuildExportPath(dialog.SelectedPath, RootPath, "xlsx");
                await RunExportActionAsync(
                    () => _excelExporter.Export(_scanResult.TempDataPath, _scanResult.ErrorPath, outputPath),
                    string.Format("Export completato: {0}", outputPath),
                    string.Format("Export completato:\n{0}", outputPath),
                    "Errore export",
                    outputPath);
            }
        }

        private async void ExportAnalysis()
        {
            if (_isViewerMode) return;
            if (_scanResult == null) return;
            var dialog = new Win32.SaveFileDialog
            {
                Filter = "Analisi NtfsAudit (*.ntaudit)|*.ntaudit",
                FileName = BuildExportFileName(RootPath, "ntaudit")
            };
            if (dialog.ShowDialog() != true) return;
            await RunExportActionAsync(
                () => _analysisArchive.Export(_scanResult, RootPath, dialog.FileName),
                string.Format("Analisi esportata: {0}", dialog.FileName),
                string.Format("Analisi esportata:\n{0}", dialog.FileName),
                "Errore export analisi",
                dialog.FileName);
        }

        private async void ExportHtml()
        {
            if (_isViewerMode) return;
            if (_scanResult == null) return;
            var dialog = new Win32.SaveFileDialog
            {
                Filter = "HTML (*.html)|*.html",
                FileName = BuildExportFileName(RootPath, "html")
            };
            if (dialog.ShowDialog() != true) return;
            var expandedPaths = GetExpandedPaths();
            await RunExportActionAsync(
                () => _htmlExporter.Export(
                    _scanResult,
                    RootPath,
                    SelectedFolderPath,
                    ColorizeRights,
                    AclFilter,
                    FilterEveryone,
                    FilterAuthenticatedUsers,
                    FilterDenyOnly,
                    FilterInheritanceDisabled,
                    expandedPaths,
                    Errors,
                    dialog.FileName),
                string.Format("Export HTML completato: {0}", dialog.FileName),
                string.Format("Export HTML completato:\n{0}", dialog.FileName),
                "Errore export HTML",
                dialog.FileName);
        }

        private async void ImportAnalysis()
        {
            var dialog = new Win32.OpenFileDialog
            {
                Filter = "Analisi NtfsAudit (*.ntaudit)|*.ntaudit"
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
                RootPath = string.IsNullOrWhiteSpace(imported.RootPath) ? RootPath : imported.RootPath;
                ClearResults();
                LoadTree(_scanResult);
                LoadErrors(_scanResult.ErrorPath);
                if (!string.IsNullOrWhiteSpace(RootPath) && _scanResult.TreeMap.Count > 0 && !_scanResult.TreeMap.ContainsKey(RootPath))
                {
                    RootPath = _scanResult.TreeMap.Keys.First();
                }
                var root = RootPath;
                if (string.IsNullOrWhiteSpace(root) && _scanResult.TreeMap.Count > 0)
                {
                    root = _scanResult.TreeMap.Keys.First();
                    RootPath = root;
                }
                SelectFolder(root);
                ProgressText = string.Format("Analisi importata: {0}", dialog.FileName);
                WpfMessageBox.Show(string.Format("Analisi importata:\n{0}", dialog.FileName), "Import completato", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
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

        private async Task RunExportActionAsync(Action exportAction, string progressMessage, string dialogMessage, string errorLabel, string outputPath = null)
        {
            try
            {
                SetBusy(true);
                await Task.Run(exportAction);
                EnsureExportOutput(outputPath);
                _hasExported = true;
                ProgressText = progressMessage;
                WpfMessageBox.Show(dialogMessage, "Export completato", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
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

        private void EnsureExportOutput(string outputPath)
        {
            if (string.IsNullOrWhiteSpace(outputPath)) return;
            var ioPath = PathResolver.ToExtendedPath(outputPath);
            if (!File.Exists(ioPath))
            {
                throw new IOException("Il file export non è stato creato.");
            }
        }

        private void ExecuteScan(ScanOptions options, CancellationToken token)
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

            }
            catch (OperationCanceledException)
            {
                RunOnUi(() =>
                {
                    ProgressText = "Scansione annullata";
                    CurrentPathText = string.Empty;
                });
            }
            catch (Exception ex)
            {
                RunOnUi(() =>
                {
                    ProgressText = string.Format("Errore scansione: {0}", ex.Message);
                    CurrentPathText = string.Empty;
                });
            }
            finally
            {
                RunOnUi(() =>
                {
                    _isScanning = false;
                    StopElapsedTimer();
                    UpdateCommands();
                });
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
            if (result == null)
            {
                return;
            }

            var treeMap = result.TreeMap;
            if (treeMap == null || treeMap.Count == 0)
            {
                treeMap = BuildTreeMapFromDetails(result.Details, RootPath);
            }
            if (treeMap == null || treeMap.Count == 0)
            {
                treeMap = BuildTreeMapFromExportRecords(result.TempDataPath, RootPath);
            }
            if (treeMap == null || treeMap.Count == 0)
            {
                return;
            }

            var provider = new FolderTreeProvider(treeMap, result.Details);
            var rootPath = ResolveTreeRoot(treeMap, RootPath);
            if (string.IsNullOrWhiteSpace(rootPath)) return;

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
                rootDetail != null && rootDetail.HasExplicitShare);
            rootNode.IsExpanded = true;
            rootNode.IsSelected = true;
            FolderTree.Add(rootNode);
        }

        private string ResolveTreeRoot(Dictionary<string, List<string>> treeMap, string preferredRoot)
        {
            if (treeMap == null || treeMap.Count == 0)
            {
                return preferredRoot;
            }
            if (!string.IsNullOrWhiteSpace(preferredRoot) && treeMap.ContainsKey(preferredRoot))
            {
                return preferredRoot;
            }

            var childSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in treeMap)
            {
                foreach (var child in entry.Value)
                {
                    if (!string.IsNullOrWhiteSpace(child))
                    {
                        childSet.Add(child);
                    }
                }
            }

            var roots = treeMap.Keys
                .Where(key => !childSet.Contains(key))
                .OrderBy(key => key.Length)
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
                    var path = string.IsNullOrWhiteSpace(record.TargetPath) ? record.FolderPath : record.TargetPath;
                    if (string.IsNullOrWhiteSpace(path)) continue;
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
            FilteredGroupEntries.Refresh();
            FilteredUserEntries.Refresh();
            FilteredAllEntries.Refresh();
            FilteredShareEntries.Refresh();
            FilteredEffectiveEntries.Refresh();
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
            if (FilterDenyOnly && !string.Equals(entry.AllowDeny, "Deny", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            if (FilterInheritanceDisabled && !entry.IsInheritanceDisabled)
            {
                return false;
            }
            if (FilterEveryone || FilterAuthenticatedUsers)
            {
                var matchesPrincipal =
                    (FilterEveryone && IsEveryone(entry.PrincipalSid, entry.PrincipalName)) ||
                    (FilterAuthenticatedUsers && IsAuthenticatedUsers(entry.PrincipalSid, entry.PrincipalName));
                if (!matchesPrincipal)
                {
                    return false;
                }
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
                || MatchesFilter(entry.AuditSummary, term)
                || MatchesFilter(entry.ResourceType, term)
                || MatchesFilter(entry.TargetPath, term)
                || MatchesFilter(entry.Owner, term)
                || MatchesFilter(entry.ShareName, term)
                || MatchesFilter(entry.ShareServer, term)
                || MatchesFilter(entry.RiskLevel, term)
                || MatchesFilter(entry.Source, term)
                || MatchesMemberFilter(entry.MemberNames, term);
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
            GroupEntries.Clear();
            UserEntries.Clear();
            AllEntries.Clear();
            ShareEntries.Clear();
            EffectiveEntries.Clear();
            Errors.Clear();
            SelectedFolderPath = string.Empty;
            ProcessedCount = 0;
            ErrorCount = 0;
            ElapsedText = "00:00:00";
            CurrentPathText = string.Empty;
            CurrentPathBackground = "Transparent";
            AclFilter = string.Empty;
            FilterEveryone = false;
            FilterAuthenticatedUsers = false;
            FilterDenyOnly = false;
            FilterInheritanceDisabled = false;
            UpdateSummary(null);
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
            OnPropertyChanged("IsBusy");
            OnPropertyChanged("IsNotBusy");
            StartCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
            ExportCommand.RaiseCanExecuteChanged();
            ExportAnalysisCommand.RaiseCanExecuteChanged();
            ExportHtmlCommand.RaiseCanExecuteChanged();
            ImportAnalysisCommand.RaiseCanExecuteChanged();
        }

        private HashSet<string> GetExpandedPaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in FolderTree)
            {
                CollectExpandedPaths(node, paths);
            }
            return paths;
        }

        private void CollectExpandedPaths(FolderNodeViewModel node, HashSet<string> paths)
        {
            if (node == null || node.IsPlaceholder) return;
            if (node.IsExpanded && !string.IsNullOrWhiteSpace(node.Path))
            {
                paths.Add(node.Path);
            }
            foreach (var child in node.Children)
            {
                CollectExpandedPaths(child, paths);
            }
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
            var baseName = Path.GetFileName(safeRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "Root";
            var timestamp = DateTime.Now.ToString("dd-MM-yyyy-HH-mm");
            return string.Format("{0}_{1}.{2}", baseName, timestamp, extension);
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
        }

        private void SaveCache()
        {
            var cachePath = _cacheStore.GetCacheFilePath("sid-cache.json");
            _sidNameCache.Save(cachePath);
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
