using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
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
        private string _rootPath;
        private int _maxDepth = 5;
        private bool _scanAllDepths = true;
        private bool _includeInherited = true;
        private bool _resolveIdentities = true;
        private bool _excludeServiceAccounts;
        private bool _excludeAdminAccounts;
        private bool _expandGroups = true;
        private bool _usePowerShell = true;
        private string _progressText = "Pronto";
        private string _currentPathText;
        private int _processedCount;
        private int _errorCount;
        private string _elapsedText = "00:00:00";
        private string _selectedFolderPath;
        private string _errorFilter;
        private string _aclFilter;
        private bool _colorizeRights = true;
        private string _currentPathBackground = "Transparent";

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

        public ObservableCollection<FolderNodeViewModel> FolderTree { get; private set; }
        public ObservableCollection<AceEntry> GroupEntries { get; private set; }
        public ObservableCollection<AceEntry> UserEntries { get; private set; }
        public ObservableCollection<AceEntry> AllEntries { get; private set; }
        public ObservableCollection<ErrorEntry> Errors { get; private set; }
        public ICollectionView FilteredErrors { get; private set; }
        public ICollectionView FilteredGroupEntries { get; private set; }
        public ICollectionView FilteredUserEntries { get; private set; }
        public ICollectionView FilteredAllEntries { get; private set; }

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
                OnPropertyChanged("CanStart");
                StartCommand.RaiseCanExecuteChanged();
            }
        }

        public int MaxDepth
        {
            get { return _maxDepth; }
            set
            {
                _maxDepth = value;
                OnPropertyChanged("MaxDepth");
            }
        }

        public bool ScanAllDepths
        {
            get { return _scanAllDepths; }
            set
            {
                _scanAllDepths = value;
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
            }
        }

        public bool IsExpandGroupsEnabled
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

        public string ErrorFilter
        {
            get { return _errorFilter; }
            set
            {
                _errorFilter = value;
                OnPropertyChanged("ErrorFilter");
                FilteredErrors.Refresh();
            }
        }

        public string AclFilter
        {
            get { return _aclFilter; }
            set
            {
                _aclFilter = value;
                OnPropertyChanged("AclFilter");
                FilteredGroupEntries.Refresh();
                FilteredUserEntries.Refresh();
                FilteredAllEntries.Refresh();
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

            foreach (var entry in detail.GroupEntries) GroupEntries.Add(entry);
            foreach (var entry in detail.UserEntries) UserEntries.Add(entry);
            foreach (var entry in detail.AllEntries) AllEntries.Add(entry);
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
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.ShowNewFolderButton = false;
                var result = dialog.ShowDialog();
                if (result == DialogResult.OK)
                {
                    RootPath = dialog.SelectedPath;
                }
            }
        }

        private void StartScan()
        {
            if (_isViewerMode) return;
            var normalizedRoot = PathResolver.NormalizeRootPath(RootPath);
            if (!string.IsNullOrWhiteSpace(normalizedRoot) &&
                !string.Equals(normalizedRoot, RootPath, StringComparison.OrdinalIgnoreCase))
            {
                RootPath = normalizedRoot;
            }

            var ioRootPath = PathResolver.ToExtendedPath(RootPath);
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
                RootPath = RootPath,
                MaxDepth = ScanAllDepths ? int.MaxValue : MaxDepth,
                ScanAllDepths = ScanAllDepths,
                IncludeInherited = IncludeInherited,
                ResolveIdentities = ResolveIdentities,
                ExcludeServiceAccounts = ExcludeServiceAccounts,
                ExcludeAdminAccounts = ExcludeAdminAccounts,
                ExpandGroups = ResolveIdentities && ExpandGroups,
                UsePowerShell = UsePowerShell
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
                    "Errore export");
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
                "Errore export analisi");
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
                    ErrorFilter,
                    expandedPaths,
                    Errors,
                    dialog.FileName),
                string.Format("Export HTML completato: {0}", dialog.FileName),
                string.Format("Export HTML completato:\n{0}", dialog.FileName),
                "Errore export HTML");
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
                _scanResult = imported.ScanResult;
                ApplyDiffs(_scanResult);
                if (!ValidateImportedResult(_scanResult, out var validationMessage))
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

        private async Task RunExportActionAsync(Action exportAction, string progressMessage, string dialogMessage, string errorLabel)
        {
            try
            {
                SetBusy(true);
                await Task.Run(exportAction);
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
            if (result == null || result.TreeMap == null || result.TreeMap.Count == 0)
            {
                return;
            }

            var provider = new FolderTreeProvider(result.TreeMap, result.Details);
            var rootPath = RootPath;
            if (string.IsNullOrWhiteSpace(rootPath) || !result.TreeMap.ContainsKey(rootPath))
            {
                rootPath = result.TreeMap.Keys.First();
            }
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
                rootSummary != null && rootSummary.IsProtected);
            rootNode.IsExpanded = true;
            rootNode.IsSelected = true;
            FolderTree.Add(rootNode);
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
            if (!File.Exists(path)) return;
            foreach (var line in File.ReadLines(path))
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

        private bool FilterErrors(object item)
        {
            if (string.IsNullOrWhiteSpace(ErrorFilter)) return true;
            var error = item as ErrorEntry;
            if (error == null) return false;
            var pathMatch = !string.IsNullOrEmpty(error.Path) && error.Path.IndexOf(ErrorFilter, StringComparison.OrdinalIgnoreCase) >= 0;
            var messageMatch = !string.IsNullOrEmpty(error.Message) && error.Message.IndexOf(ErrorFilter, StringComparison.OrdinalIgnoreCase) >= 0;
            var typeMatch = !string.IsNullOrEmpty(error.ErrorType) && error.ErrorType.IndexOf(ErrorFilter, StringComparison.OrdinalIgnoreCase) >= 0;
            return pathMatch || messageMatch || typeMatch;
        }

        private bool FilterAclEntries(object item)
        {
            if (string.IsNullOrWhiteSpace(AclFilter)) return true;
            var entry = item as AceEntry;
            if (entry == null) return false;
            var term = AclFilter;
            return MatchesFilter(entry.PrincipalName, term)
                || MatchesFilter(entry.PrincipalSid, term)
                || MatchesFilter(entry.AllowDeny, term)
                || MatchesFilter(entry.RightsSummary, term);
        }

        private bool MatchesFilter(string value, string filter)
        {
            return !string.IsNullOrWhiteSpace(value)
                && value.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void ClearResults()
        {
            FolderTree.Clear();
            GroupEntries.Clear();
            UserEntries.Clear();
            AllEntries.Clear();
            Errors.Clear();
            SelectedFolderPath = string.Empty;
            ProcessedCount = 0;
            ErrorCount = 0;
            ElapsedText = "00:00:00";
            CurrentPathText = string.Empty;
            CurrentPathBackground = "Transparent";
            AclFilter = string.Empty;
        }

        private void UpdateCommands()
        {
            OnPropertyChanged("IsScanning");
            OnPropertyChanged("IsNotScanning");
            OnPropertyChanged("IsScanConfigEnabled");
            OnPropertyChanged("HasScanResult");
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
