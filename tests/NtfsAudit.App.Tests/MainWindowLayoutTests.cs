using NtfsAudit.App.Services;
using NtfsAudit.Core.Logging;
using NtfsAudit.Core.Export;
using NtfsAudit.Core.Cache;
using NtfsAudit.Core.Models;
using NtfsAudit.Core.Services;
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
            Assert.Contains("{Binding NewScanCommand}", buttonCommands);
            Assert.Contains("{Binding ImportAnalysisCommand}", buttonCommands);
            Assert.Contains("{Binding ExportCommand}", buttonCommands);
            Assert.Contains("{Binding ToggleSettingsCommand}", buttonCommands);
            Assert.DoesNotContain("{Binding CleanupResidualFilesCommand}", buttonCommands);

            Assert.DoesNotContain(root.Descendants(), element => element.Name.LocalName == "ScanSidebar");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "SettingsPanel");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "FolderTreePanel");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "ResultsPanel");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "TextBlock" && (string)element.Attribute("Text") == "{Binding ProgressText}");
        }

        [Fact]
        public void MainWindow_PrimaryToolbar_KeepsOnlyPrimaryActions()
        {
            var root = LoadMainWindow();
            // Toolbar buttons now use StackPanel > TextBlock children instead of Content attribute.
            // Collect all TextBlock Text values nested inside Button elements.
            var buttonTexts = root.Descendants()
                .Where(element => element.Name.LocalName == "Button")
                .SelectMany(button => button.Descendants())
                .Where(element => element.Name.LocalName == "TextBlock")
                .Select(element => (string)element.Attribute("Text"))
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();

            Assert.Contains("{DynamicResource Main.StartAnalysis}", buttonTexts);
            Assert.Contains("{DynamicResource Main.NewScan}", buttonTexts);
            Assert.Contains("{DynamicResource Main.ImportAnalysis}", buttonTexts);
            Assert.Contains("{DynamicResource Main.ExportExcel}", buttonTexts);
            Assert.Contains("{DynamicResource Main.Settings}", buttonTexts);
            Assert.DoesNotContain("{DynamicResource Main.CleanupResiduals}", buttonTexts);
        }

        [Fact]
        public void MainWindow_RootInput_PreservesHintsAndAutomationNames()
        {
            var root = LoadMainWindow();

            Assert.Equal("MainWindow_OnPreviewKeyDown", (string)root.Attribute("PreviewKeyDown"));
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Button" && (string)element.Attribute(XamlNamespace + "Name") == "SettingsToggleButton");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Button" && (string)element.Attribute(XamlNamespace + "Name") == null && (string)element.Attribute("AutomationProperties.Name") == "{DynamicResource Scan.RootBrowse.AutomationName}");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "TextBox" && (string)element.Attribute("AutomationProperties.Name") == "{DynamicResource Scan.RootPath.AutomationName}");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Button" && (string)element.Attribute("AutomationProperties.Name") == "{DynamicResource Scan.RootAdd.AutomationName}");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Button" && (string)element.Attribute("AutomationProperties.Name") == "{DynamicResource Scan.RemoveRoot.AutomationName}");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "TextBlock" && (string)element.Attribute("Text") == "{DynamicResource Scan.FoldersHint}");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "TextBlock" && (string)element.Attribute("Text") == "{DynamicResource Scan.StartHint}");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Button" && (string)element.Attribute("Command") == "{Binding CloseSettingsCommand}" && (string)element.Attribute("IsCancel") == "True");
        }

        [Fact]
        public void MainWindow_UsesSharedChromeForPrimarySurfaceAndWarningBadge()
        {
            var root = LoadMainWindow();

            Assert.Equal("pack://application:,,,/NtfsAudit.App;component/Assets/OnlyRights.ico", (string)root.Attribute("Icon"));
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Border" && (string)element.Attribute("Style") == "{StaticResource PanelBorderStyle}");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Border" && (string)element.Attribute("Style") == "{StaticResource WarningStatusBadgeBorderStyle}");
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
