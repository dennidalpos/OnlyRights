using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using NtfsAudit.App.Cache;
using NtfsAudit.App.Logging;

namespace NtfsAudit.App
{
    public partial class App : Application
    {
        private Logger _logger;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            InitializeLogger();
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            CleanupTemporaryFiles();
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

        private void CleanupTemporaryFiles()
        {
            try
            {
                var tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NtfsAudit");
                if (System.IO.Directory.Exists(tempRoot))
                {
                    System.IO.Directory.Delete(tempRoot, true);
                }
            }
            catch
            {
            }
        }
    }
}
