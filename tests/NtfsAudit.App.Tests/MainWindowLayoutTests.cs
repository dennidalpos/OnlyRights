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
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class MainWindowLayoutTests
    {
        private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

        [Fact]
        public void MainWindow_UsesCompactPrimarySurfaceAndDedicatedSettingsPanel()
        {
            var root = LoadMainWindow();
            var buttonCommands = root.Descendants()
                .Where(element => element.Name.LocalName == "Button")
                .Select(element => (string)element.Attribute("Command"))
                .Where(command => !string.IsNullOrWhiteSpace(command))
                .ToList();

            Assert.Contains("{Binding StartCommand}", buttonCommands);
            Assert.Contains("{Binding StopCommand}", buttonCommands);
            Assert.Contains("{Binding ImportAnalysisCommand}", buttonCommands);
            Assert.Contains("{Binding ExportCommand}", buttonCommands);
            Assert.Contains("{Binding ToggleSettingsCommand}", buttonCommands);

            Assert.DoesNotContain(root.Descendants(), element => element.Name.LocalName == "ScanSidebar");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "SettingsPanel");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "FolderTreePanel");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "ResultsPanel");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "TextBlock" && (string)element.Attribute("Text") == "{Binding ProgressText}");
        }

        private static XElement LoadMainWindow()
        {
            var root = FindRepositoryRoot();
            return XDocument.Load(Path.Combine(root, "src", "NtfsAudit.App", "MainWindow.xaml")).Root;
        }

        private static string FindRepositoryRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "NtfsAudit.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Repository root not found.");
        }
    }
}
