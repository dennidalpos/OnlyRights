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
    public class UiResourceLayoutTests
    {
        private static readonly XNamespace XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

        [Fact]
        public void SharedResources_DefineModernScrollbarsAndSharedControlStyles()
        {
            var root = LoadXaml("src", "NtfsAudit.App", "Resources", "SharedResources.xaml");

            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style" && (string)element.Attribute(XamlNamespace + "Key") == "MinimalScrollBarThumbStyle");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "ControlTemplate" && (string)element.Attribute(XamlNamespace + "Key") == "VerticalScrollBarTemplate");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "ControlTemplate" && (string)element.Attribute(XamlNamespace + "Key") == "HorizontalScrollBarTemplate");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "ControlTemplate" && (string)element.Attribute(XamlNamespace + "Key") == "FilledActionButtonTemplate");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "ControlTemplate" && (string)element.Attribute(XamlNamespace + "Key") == "OutlinedActionButtonTemplate");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style" && (string)element.Attribute("TargetType") == "DataGrid");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style" && (string)element.Attribute("TargetType") == "TabControl");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style" && (string)element.Attribute(XamlNamespace + "Key") == "WarningStatusBadgeBorderStyle");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style" && (string)element.Attribute(XamlNamespace + "Key") == "CompactInfoCardBorderStyle");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style" && (string)element.Attribute(XamlNamespace + "Key") == "CompactInfoValueText");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style" && (string)element.Attribute(XamlNamespace + "Key") == "ExpanderHeaderText");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style" && (string)element.Attribute(XamlNamespace + "Key") == "ExpanderHeaderHintText");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style" && (string)element.Attribute(XamlNamespace + "Key") == "SemanticChipBorderStyle");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style" && (string)element.Attribute(XamlNamespace + "Key") == "LegendChipBorderStyle");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style" && (string)element.Attribute(XamlNamespace + "Key") == "TreeInlineChipBorderStyle");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style" && (string)element.Attribute(XamlNamespace + "Key") == "SemanticChipTextStyle");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style" && (string)element.Attribute(XamlNamespace + "Key") == "ResultsSectionExpanderStyle");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style" && (string)element.Attribute(XamlNamespace + "Key") == "TreeSectionExpanderStyle");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style" && (string)element.Attribute(XamlNamespace + "Key") == "ResultsTabItemStyle");
        }

        [Fact]
        public void SharedResources_UseCustomSettingsGroupBoxChrome()
        {
            var root = LoadXaml("src", "NtfsAudit.App", "Resources", "SharedResources.xaml");

            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style" && (string)element.Attribute(XamlNamespace + "Key") == "AppGroupBoxStyle");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "ContentPresenter" && (string)element.Attribute("ContentSource") == "Header");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Border" && (string)element.Attribute("CornerRadius") == "{StaticResource ControlCornerRadius}");
        }

        [Fact]
        public void TreeAndResultsPanels_KeepFiltersInsideExpanders()
        {
            var folderTreeRoot = LoadXaml("src", "NtfsAudit.App", "Views", "FolderTreePanel.xaml");
            var resultsRoot = LoadXaml("src", "NtfsAudit.App", "Views", "ResultsPanel.xaml");

            Assert.Contains(folderTreeRoot.Descendants(), element => element.Name.LocalName == "Expander" && (string)element.Attribute("Header") == "{DynamicResource Tree.LegendFilters}");
            Assert.Contains(folderTreeRoot.Descendants(), element => element.Name.LocalName == "Expander" && (string)element.Attribute("Style") == "{StaticResource TreeSectionExpanderStyle}");
            Assert.Contains(resultsRoot.Descendants(), element => element.Name.LocalName == "Expander" && (string)element.Attribute("Header") == "{DynamicResource Results.RightsLegend}");
            Assert.Contains(resultsRoot.Descendants(), element => element.Name.LocalName == "Expander" && (string)element.Attribute("Header") == "{DynamicResource Results.PersistentFilters}");
        }

        [Fact]
        public void SharedResources_UseCustomTooltipChromeAndForwardExpanderHeader()
        {
            var root = LoadXaml("src", "NtfsAudit.App", "Resources", "SharedResources.xaml");

            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style" && (string)element.Attribute("TargetType") == "ToolTip");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Setter"
                && (string)element.Attribute("Property") == "TextElement.Foreground"
                && (string)element.Attribute("Value") == "White");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "ControlTemplate" && (string)element.Attribute("TargetType") == "ToolTip");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Border"
                && (string)element.Attribute("TextElement.Foreground") == "{TemplateBinding Foreground}");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style"
                && (string)element.Attribute("TargetType") == "TextBlock"
                && element.Ancestors().Any(ancestor => ancestor.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal)));
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "ContentPresenter"
                && (string)element.Attribute("TextElement.Foreground") == "{TemplateBinding Foreground}");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "ToggleButton"
                && (string)element.Attribute("Content") == "{TemplateBinding Header}");
        }

        [Fact]
        public void SettingsPanel_RestoresSupportedUtilityActionsAndAccurateCleanupCopy()
        {
            var settingsRoot = LoadXaml("src", "NtfsAudit.App", "Views", "SettingsPanel.xaml");
            var buttonContents = settingsRoot.Descendants()
                .Where(element => element.Name.LocalName == "Button")
                .Select(element => (string)element.Attribute("Content"))
                .Where(content => !string.IsNullOrWhiteSpace(content))
                .ToList();

            Assert.Contains("{DynamicResource Scan.SaveFolderSet}", buttonContents);
            Assert.Contains("{DynamicResource Scan.LoadFolderSet}", buttonContents);
            Assert.Contains("{DynamicResource Scan.ApplyCompatibleOptions}", buttonContents);
            Assert.DoesNotContain(settingsRoot.Descendants(), element => element.Name.LocalName == "GroupBox" && (string)element.Attribute("Header") == "{DynamicResource Main.Settings}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "Border" && (string)element.Attribute("Style") == "{StaticResource PanelBorderStyle}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "TextBlock" && (string)element.Attribute("Text") == "{DynamicResource Main.CleanupResiduals.ToolTip}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "TextBox" && (string)element.Attribute("AutomationProperties.Name") == "{DynamicResource Scan.OutputDirectory.AutomationName}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "TextBox" && (string)element.Attribute("AutomationProperties.Name") == "{DynamicResource Scan.GlobalUser.AutomationName}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "PasswordBox" && (string)element.Attribute("AutomationProperties.Name") == "{DynamicResource Scan.GlobalPassword.AutomationName}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "TextBlock" && (string)element.Attribute("Text") == "{DynamicResource Scan.ServiceModeHint}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "TextBlock" && (string)element.Attribute("Text") == "{DynamicResource Scan.IdentityHint}");
        }

        [Fact]
        public void ResultsSummary_RemovesSecondaryMetricCards()
        {
            var resultsRoot = LoadXaml("src", "NtfsAudit.App", "Views", "ResultsPanel.xaml");
            var summaryWrapPanel = resultsRoot.Descendants().First(element => element.Name.LocalName == "WrapPanel");
            var metricLabels = summaryWrapPanel.Descendants()
                .Where(element => element.Name.LocalName == "TextBlock")
                .Select(element => (string)element.Attribute("Text"))
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .ToList();

            Assert.Contains("{DynamicResource Results.TotalAce}", metricLabels);
            Assert.Contains("{DynamicResource Results.HighRisk}", metricLabels);
            Assert.Contains("{DynamicResource Results.Deny}", metricLabels);
            Assert.Contains("{DynamicResource Results.File}", metricLabels);
            Assert.DoesNotContain("{DynamicResource Results.Everyone}", metricLabels);
            Assert.DoesNotContain("{DynamicResource Results.AuthenticatedUsers}", metricLabels);
            Assert.Contains(summaryWrapPanel.Descendants(), element => element.Name.LocalName == "Border" && (string)element.Attribute("Style") == "{StaticResource CompactInfoCardBorderStyle}");
            Assert.Contains(resultsRoot.Descendants(), element => element.Name.LocalName == "Border" && (string)element.Attribute("Style") == "{StaticResource CompactNeutralInfoCardBorderStyle}");
            Assert.Contains(resultsRoot.Descendants(), element => element.Name.LocalName == "Expander" && (string)element.Attribute("IsExpanded") == "False");
            Assert.Contains(resultsRoot.Descendants(), element => element.Name.LocalName == "Expander" && (string)element.Attribute("Style") == "{StaticResource ResultsSectionExpanderStyle}");
            Assert.Contains(resultsRoot.Descendants(), element => element.Name.LocalName == "TabControl" && (string)element.Attribute("ItemContainerStyle") == "{StaticResource ResultsTabItemStyle}");
            Assert.Contains(resultsRoot.Descendants(), element => element.Name.LocalName == "Border"
                && (string)element.Attribute("Style") == "{StaticResource LegendChipBorderStyle}");
            Assert.Contains(resultsRoot.Descendants(), element => element.Name.LocalName == "TextBlock"
                && (string)element.Attribute("Text") == "{Binding SelectedFolderName}"
                && (string)element.Attribute("ToolTip") == "{Binding SelectedFolderName}"
                && (string)element.Attribute("Style") == "{StaticResource ExpanderHeaderText}");
            Assert.Contains(resultsRoot.Descendants(), element => element.Name.LocalName == "TextBlock"
                && (string)element.Attribute("Text") == "{DynamicResource Results.ProcessedFolder}"
                && (string)element.Attribute("Style") == "{StaticResource ExpanderHeaderHintText}");
            Assert.Contains(resultsRoot.Descendants(), element => element.Name.LocalName == "TextBlock"
                && (string)element.Attribute("Text") == "{Binding SelectedFolderPath}"
                && (string)element.Attribute("ToolTip") == "{Binding SelectedFolderPath}");
        }

        [Fact]
        public void FolderTreeNodes_ExposeFullNameTooltipForTrimmedLabels()
        {
            var folderTreeRoot = LoadXaml("src", "NtfsAudit.App", "Views", "FolderTreePanel.xaml");

            Assert.Contains(folderTreeRoot.Descendants(), element => element.Name.LocalName == "TextBlock"
                && (string)element.Attribute("Text") == "{Binding DisplayName}"
                && (string)element.Attribute("TextTrimming") == "CharacterEllipsis"
                && (string)element.Attribute("ToolTip") == "{Binding DisplayName}");
            Assert.Contains(folderTreeRoot.Descendants(), element => element.Name.LocalName == "TextBlock"
                && (string)element.Attribute("Text") == "{DynamicResource Tree.DiffParent.Badge}");
            Assert.Contains(folderTreeRoot.Descendants(), element => element.Name.LocalName == "TextBlock"
                && (string)element.Attribute("Text") == "{DynamicResource Tree.BaselineMismatch.Badge}");
            Assert.DoesNotContain(folderTreeRoot.Descendants(), element => element.Name.LocalName == "TextBlock"
                && (string)element.Attribute("Text") == "{Binding DiffLabel}");
            Assert.DoesNotContain(folderTreeRoot.Descendants(), element => element.Name.LocalName == "TextBlock"
                && (string)element.Attribute("Text") == "{Binding BaselineMismatchLabel}");
            Assert.DoesNotContain(folderTreeRoot.Descendants(), element => element.Name.LocalName == "TextBlock"
                && (string)element.Attribute("Text") == "{Binding ExplicitNtfsLabel}");
        }

        private static XElement LoadXaml(params string[] parts)
        {
            var root = FindRepositoryRoot();
            var path = Path.Combine(new[] { root }.Concat(parts).ToArray());
            return XDocument.Load(path).Root;
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
