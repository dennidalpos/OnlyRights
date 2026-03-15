using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NtfsAudit.App.Cache;
using NtfsAudit.App.Logging;
using NtfsAudit.App.Services;

namespace NtfsAudit.App
{
    public partial class App : Application
    {
        private Mutex _singleInstanceMutex;
        private Logger _logger;
        private const string SingleInstanceMutexName = "Global\\NtfsAudit.App.SingleInstance";

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            if (!EnsureSingleInstance())
            {
                Shutdown();
                return;
            }
            InitializeLogger();
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            if (_singleInstanceMutex != null)
            {
                _singleInstanceMutex.ReleaseMutex();
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }
        }

        private bool EnsureSingleInstance()
        {
            if (SingleInstanceCoordinator.TryAcquire(SingleInstanceMutexName, out _singleInstanceMutex))
            {
                return true;
            }

            MessageBox.Show("NTFS Audit è già in esecuzione.", "Istanza già attiva", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }

        private void InitializeLogger()
        {
            try
            {
                var cacheStore = new LocalCacheStore();
                _logger = new Logger(cacheStore.GetCacheFilePath("logs.txt"));
            }
            catch
            {
                _logger = null;
            }
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogUnhandled("DispatcherUnhandledException", e.Exception);
            MessageBox.Show("Errore inatteso. Controlla il file di log per i dettagli.", "Errore", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LogUnhandled("UnhandledException", e.ExceptionObject as Exception);
        }

        private void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs e)
        {
            LogUnhandled("UnobservedTaskException", e.Exception);
            e.SetObserved();
        }

        private void LogUnhandled(string source, Exception exception)
        {
            if (_logger == null) return;
            var message = exception == null
                ? string.Format("Unhandled error [{0}] (null exception)", source)
                : string.Format("Unhandled error [{0}]: {1}", source, exception);
            _logger.Error(message);
        }
    }
}
