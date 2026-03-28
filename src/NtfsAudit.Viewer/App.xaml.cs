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
using NtfsAudit.App.ViewModels;

namespace NtfsAudit.Viewer
{
    public partial class App : Application
    {
        internal const string SharedResourcesSource = "/NtfsAudit.App;component/Resources/SharedResources.xaml";

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            EnsureSharedResourcesLoaded(Resources);
            var viewModel = new MainViewModel(true);
            var window = new MainWindow(viewModel)
            {
                Title = "NTFS Audit Viewer"
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
    }
}
