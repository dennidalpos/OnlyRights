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
using System.IO;
using NtfsAudit.App;
using NtfsAudit.App.ViewModels;

namespace NtfsAudit.Viewer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var viewModel = new MainViewModel(true);
            var window = new MainWindow(viewModel)
            {
                Title = "NTFS Audit Viewer"
            };
            window.Show();

            if (e.Args != null && e.Args.Length > 0)
            {
                var archivePath = e.Args[0];
                if (!string.IsNullOrWhiteSpace(archivePath)
                    && (File.Exists(archivePath) || File.Exists(archivePath + ".ntaudit")))
                {
                    _ = viewModel.ImportAnalysisFromPathAsync(archivePath);
                }
            }
        }
    }
}
