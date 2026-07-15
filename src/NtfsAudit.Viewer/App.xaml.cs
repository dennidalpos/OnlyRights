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
using System.Windows;
using NtfsAudit.App;
using NtfsAudit.App.Services;
using NtfsAudit.App.ViewModels;

namespace NtfsAudit.Viewer
{
    public partial class App : Application
    {
        internal const string SharedResourcesSource = "/NtfsAudit.App;component/Resources/SharedResources.xaml";

        protected override void OnStartup(StartupEventArgs e)
        {
            LocalizationManager.ApplyDefault();
            base.OnStartup(e);

            if (!IsAdministrator())
            {
                if (TryRelaunchElevated(e.Args))
                {
                    Shutdown();
                    return;
                }
                else
                {
                    MessageBox.Show(
                        LocalizationManager.Text("App.RequiresAdmin"),
                        LocalizationManager.Text("App.ViewerTitle"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    Shutdown();
                    return;
                }
            }

            EnsureSharedResourcesLoaded(Resources);
            var viewModel = new MainViewModel(true);
            var window = new MainWindow(viewModel)
            {
                Title = LocalizationManager.Text("App.ViewerTitle")
            };
            window.Show();

            _ = ViewerStartupCoordinator.TryImportFromArgsAsync(e.Args, viewModel.ImportAnalysisFromPathAsync);
        }

        internal static void EnsureSharedResourcesLoaded(ResourceDictionary resources)
        {
            if (resources == null)
            {
                throw new System.ArgumentNullException("resources");
            }

            foreach (ResourceDictionary dictionary in resources.MergedDictionaries)
            {
                if (dictionary != null
                    && dictionary.Source != null
                    && System.Uri.Compare(
                        dictionary.Source,
                        CreateSharedResourcesUri(),
                        System.UriComponents.SerializationInfoString,
                        System.UriFormat.SafeUnescaped,
                        System.StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return;
                }
            }

            resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = CreateSharedResourcesUri()
            });
        }

        private static System.Uri CreateSharedResourcesUri()
        {
            return new System.Uri(SharedResourcesSource, System.UriKind.Relative);
        }

        private static bool IsAdministrator()
        {
            using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
        }

        private static bool TryRelaunchElevated(string[] args)
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = System.Environment.ProcessPath ?? System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName,
                UseShellExecute = true,
                Verb = "runas",
                Arguments = string.Join(" ", args)
            };

            try
            {
                System.Diagnostics.Process.Start(processInfo);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
