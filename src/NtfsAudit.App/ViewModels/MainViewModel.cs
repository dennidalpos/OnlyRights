using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Forms;
using Win32 = Microsoft.Win32;
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
        private string _rootPath;
        private int _maxDepth = 5;
        private bool _scanAllDepths = true;
        private bool _includeInherited = true;
        private bool _resolveIdentities = true;
        private bool _excludeServiceAccounts;
        private bool _excludeAdminAccounts;
        private bool _expandGroups = true;
        private bool _usePowerShell = true;
        private bool _exportOnComplete;
        private string _progressText = "Pronto";
        private bool _isIndeterminate;
        private double _progressValue;
        private int _processedCount;
        private int _remainingCount;
        private string _selectedFolderPath;
        private string _errorFilter;
        private bool _colorizeRights = true;

        public MainViewModel()
        {
            _cacheStore = new LocalCacheStore();
            _sidNameCache = new SidNameCache();
            _groupMembershipCache = new GroupMembershipCache(TimeSpan.FromHours(2));
            _excelExporter = new ExcelExporter();
            _analysisArchive = new AnalysisArchive();

            FolderTree = new ObservableCollection<FolderNodeViewModel>();
            GroupEntries = new ObservableCollection<AceEntry>();
            UserEntries = new ObservableCollection<AceEntry>();
            AllEntries = new ObservableCollection<AceEntry>();
            Errors = new ObservableCollection<ErrorEntry>();
            FilteredErrors = CollectionViewSource.GetDefaultView(Errors);
            FilteredErrors.Filter = FilterErrors;

            BrowseCommand = new RelayCommand(Browse);
            StartCommand = new RelayCommand(StartScan, () => CanStart);
            StopCommand = new RelayCommand(StopScan, () => CanStop);
            ExportCommand = new RelayCommand(Export, () => CanExport);
            ExportAnalysisCommand = new RelayCommand(ExportAnalysis, () => CanExport);
            ImportAnalysisCommand = new RelayCommand(ImportAnalysis, () => !_isScanning);

            LoadCache();
        }

        public ObservableCollection<FolderNodeViewModel> FolderTree { get; private set; }
        public ObservableCollection<AceEntry> GroupEntries { get; private set; }
        public ObservableCollection<AceEntry> UserEntries { get; private set; }
        public ObservableCollection<AceEntry> AllEntries { get; private set; }
        public ObservableCollection<ErrorEntry> Errors { get; private set; }
        public ICollectionView FilteredErrors { get; private set; }

        public RelayCommand BrowseCommand { get; private set; }
        public RelayCommand StartCommand { get; private set; }
        public RelayCommand StopCommand { get; private set; }
        public RelayCommand ExportCommand { get; private set; }
        public RelayCommand ExportAnalysisCommand { get; private set; }
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

        public bool ExportOnComplete
        {
            get { return _exportOnComplete; }
            set
            {
                _exportOnComplete = value;
                OnPropertyChanged("ExportOnComplete");
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

        public bool IsIndeterminate
        {
            get { return _isIndeterminate; }
            set
            {
                _isIndeterminate = value;
                OnPropertyChanged("IsIndeterminate");
            }
        }

        public double ProgressValue
        {
            get { return _progressValue; }
            set
            {
                _progressValue = value;
                OnPropertyChanged("ProgressValue");
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

        public int RemainingCount
        {
            get { return _remainingCount; }
            set
            {
                _remainingCount = value;
                OnPropertyChanged("RemainingCount");
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

        public bool ColorizeRights
        {
            get { return _colorizeRights; }
            set
            {
                _colorizeRights = value;
                OnPropertyChanged("ColorizeRights");
            }
        }

        public bool CanStart { get { return !_isScanning && !string.IsNullOrWhiteSpace(RootPath); } }
        public bool CanStop { get { return _isScanning; } }
        public bool CanExport { get { return !_isScanning && _scanResult != null; } }

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
            var normalizedRoot = PathResolver.NormalizeRootPath(RootPath);
            if (!string.IsNullOrWhiteSpace(normalizedRoot) &&
                !string.Equals(normalizedRoot, RootPath, StringComparison.OrdinalIgnoreCase))
            {
                RootPath = normalizedRoot;
            }

            if (!Directory.Exists(RootPath))
            {
                ProgressText = "Percorso non valido";
                return;
            }

            _isScanning = true;
            IsIndeterminate = false;
            ProgressValue = 0;
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
                UsePowerShell = UsePowerShell,
                ExportOnComplete = ExportOnComplete
            };

            Task.Run(() => ExecuteScan(options, _cts.Token));
        }

        private void StopScan()
        {
            if (_cts != null)
            {
                _cts.Cancel();
            }
        }

        private void Export()
        {
            if (_scanResult == null) return;
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.ShowNewFolderButton = true;
                var result = dialog.ShowDialog();
                if (result != DialogResult.OK) return;
                var outputPath = BuildExportPath(dialog.SelectedPath, RootPath);
                _excelExporter.Export(_scanResult.TempDataPath, _scanResult.ErrorPath, outputPath);
                ProgressText = string.Format("Export completato: {0}", outputPath);
            }
        }

        private void ExportAnalysis()
        {
            if (_scanResult == null) return;
            var dialog = new Win32.SaveFileDialog
            {
                Filter = "Analisi NtfsAudit (*.ntaudit)|*.ntaudit",
                FileName = "analisi-ntfs-audit.ntaudit"
            };
            if (dialog.ShowDialog() != true) return;
            _analysisArchive.Export(_scanResult, RootPath, dialog.FileName);
            ProgressText = string.Format("Analisi esportata: {0}", dialog.FileName);
        }

        private void ImportAnalysis()
        {
            var dialog = new Win32.OpenFileDialog
            {
                Filter = "Analisi NtfsAudit (*.ntaudit)|*.ntaudit"
            };
            if (dialog.ShowDialog() != true) return;

            var imported = _analysisArchive.Import(dialog.FileName);
            _scanResult = imported.ScanResult;
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
            UpdateCommands();
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
                    _scanResult = result;
                    SaveCache();
                    LoadTree(result);
                    LoadErrors(result.ErrorPath);
                    SelectFolder(options.RootPath);
                });

                if (options.ExportOnComplete)
                {
                    RunOnUi(Export);
                }
            }
            catch (OperationCanceledException)
            {
                RunOnUi(() => { ProgressText = "Scansione annullata"; });
            }
            catch (Exception ex)
            {
                RunOnUi(() =>
                {
                    ProgressText = string.Format("Errore scansione: {0}", ex.Message);
                });
            }
            finally
            {
                RunOnUi(() =>
                {
                    _isScanning = false;
                    IsIndeterminate = false;
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
            var stage = string.IsNullOrWhiteSpace(progress.Stage) ? "Scansione" : progress.Stage;
            var path = string.IsNullOrWhiteSpace(progress.CurrentPath) ? string.Empty : string.Format(" | {0}", progress.CurrentPath);
            ProgressText = string.Format("{0}{1} | Cartelle processate: {2} | In coda: {3} | Errori: {4} | Tempo: {5}",
                stage,
                path,
                progress.Processed,
                progress.QueueCount,
                progress.Errors,
                progress.Elapsed.ToString("hh\\:mm\\:ss"));
            ProcessedCount = progress.Processed;
            RemainingCount = progress.QueueCount;
            var total = progress.Processed + progress.QueueCount;
            if (total > 0)
            {
                ProgressValue = (progress.Processed * 100.0) / total;
                IsIndeterminate = false;
            }
            else
            {
                IsIndeterminate = false;
                ProgressValue = 0;
            }
        }

        private void LoadTree(ScanResult result)
        {
            FolderTree.Clear();
            if (result == null || result.TreeMap == null || result.TreeMap.Count == 0)
            {
                return;
            }

            var provider = new FolderTreeProvider(result.TreeMap);
            var rootPath = RootPath;
            if (string.IsNullOrWhiteSpace(rootPath) || !result.TreeMap.ContainsKey(rootPath))
            {
                rootPath = result.TreeMap.Keys.First();
            }
            if (string.IsNullOrWhiteSpace(rootPath)) return;

            var rootName = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(rootName)) rootName = rootPath;
            var rootNode = new FolderNodeViewModel(rootPath, rootName, provider);
            rootNode.IsExpanded = true;
            rootNode.IsSelected = true;
            FolderTree.Add(rootNode);
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

        private void ClearResults()
        {
            FolderTree.Clear();
            GroupEntries.Clear();
            UserEntries.Clear();
            AllEntries.Clear();
            Errors.Clear();
            SelectedFolderPath = string.Empty;
            ProcessedCount = 0;
            RemainingCount = 0;
        }

        private void UpdateCommands()
        {
            OnPropertyChanged("CanStart");
            OnPropertyChanged("CanStop");
            OnPropertyChanged("CanExport");
            StartCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
            ExportCommand.RaiseCanExecuteChanged();
            ExportAnalysisCommand.RaiseCanExecuteChanged();
            ImportAnalysisCommand.RaiseCanExecuteChanged();
        }

        private string BuildExportPath(string folder, string rootPath)
        {
            var safeRoot = rootPath ?? string.Empty;
            var baseName = Path.GetFileName(safeRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "Root";
            var timestamp = DateTime.Now.ToString("dd-MM-yyyy-HH-mm");
            var fileName = string.Format("{0}_{1}.xlsx", baseName, timestamp);
            return Path.Combine(folder, fileName);
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
