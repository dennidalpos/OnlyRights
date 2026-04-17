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
        public void MainWindow_ActionToolbar_SeparatesPrimarySecondaryAndLocaleLayout()
        {
            var root = LoadMainWindow();
            var actionToolbarLayout = FindElementByName(root, "ActionToolbarLayout");
            var primaryToolbar = FindElementByName(root, "PrimaryActionToolbar");
            var secondaryToolbar = FindElementByName(root, "SecondaryActionToolbar");
            var localeToolbarPanel = FindElementByName(root, "LocaleToolbarPanel");

            Assert.Equal("Grid", actionToolbarLayout.Name.LocalName);
            Assert.Equal(actionToolbarLayout, primaryToolbar.Parent);
            Assert.Equal("Grid", secondaryToolbar.Parent?.Name.LocalName);
            Assert.Equal(secondaryToolbar.Parent, localeToolbarPanel.Parent);
            Assert.DoesNotContain(localeToolbarPanel.Ancestors(), element => element.Name.LocalName == "WrapPanel");

            Assert.Equal(
                new[]
                {
                    "{Binding StartCommand}",
                    "{Binding StopCommand}",
                    "{Binding InstallServiceCommand}",
                    "{Binding UninstallServiceCommand}"
                },
                GetButtonCommands(primaryToolbar));

            Assert.Equal(
                new[]
                {
                    "{Binding ImportAnalysisCommand}",
                    "{Binding ExportCommand}",
                    "{Binding CleanupResidualFilesCommand}"
                },
                GetButtonCommands(secondaryToolbar));
        }

        private static IReadOnlyList<string> GetButtonCommands(XElement panel)
        {
            return panel.Elements().Where(element => element.Name.LocalName == "Button")
                .Select(element => (string)element.Attribute("Command"))
                .ToList();
        }

        private static XElement FindElementByName(XElement root, string elementName)
        {
            return root.Descendants()
                .Single(element => string.Equals((string)element.Attribute(XamlNamespace + "Name"), elementName, StringComparison.Ordinal));
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
