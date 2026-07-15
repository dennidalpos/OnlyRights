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
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
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
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style" && (string)element.Attribute(XamlNamespace + "Key") == "FieldRowGridStyle");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style" && (string)element.Attribute(XamlNamespace + "Key") == "InlineActionsPanelStyle");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style" && (string)element.Attribute(XamlNamespace + "Key") == "FieldLabelText");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "SolidColorBrush" && (string)element.Attribute(XamlNamespace + "Key") == "PermissionReadBrush");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "SolidColorBrush" && (string)element.Attribute(XamlNamespace + "Key") == "PermissionModifyBrush");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "SolidColorBrush" && (string)element.Attribute(XamlNamespace + "Key") == "PermissionProtectedBrush");
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
        public void SharedResources_KeepBorderPrimitivesOnSingleCornerRadiusScale()
        {
            var root = LoadXaml("src", "NtfsAudit.App", "Resources", "SharedResources.xaml");

            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style"
                && (string)element.Attribute(XamlNamespace + "Key") == "CompactInfoCardBorderStyle"
                && element.Descendants().Any(descendant => descendant.Name.LocalName == "Setter"
                    && (string)descendant.Attribute("Property") == "CornerRadius"
                    && (string)descendant.Attribute("Value") == "{StaticResource ControlCornerRadius}"));
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style"
                && (string)element.Attribute(XamlNamespace + "Key") == "SupportPanelBorderStyle"
                && (string)element.Attribute("BasedOn") == "{StaticResource PanelBorderStyle}");
            Assert.DoesNotContain(root.Descendants(), element => element.Name.LocalName == "Setter"
                && (string)element.Attribute("Property") == "Padding"
                && (string)element.Attribute("Value") == "8,3"
                && element.Ancestors().Any(ancestor => ancestor.Name.LocalName == "Style"
                    && (string)ancestor.Attribute(XamlNamespace + "Key") == "WarningStatusBadgeBorderStyle"));
        }

        [Fact]
        public void TreeAndResultsPanels_KeepFiltersInsideExpanders()
        {
            var folderTreeRoot = LoadXaml("src", "NtfsAudit.App", "Views", "FolderTreePanel.xaml");
            var resultsRoot = LoadXaml("src", "NtfsAudit.App", "Views", "ResultsPanel.xaml");

            Assert.Contains(folderTreeRoot.Descendants(), element => element.Name.LocalName == "Expander" && (string)element.Attribute("Header") == "{DynamicResource Tree.LegendFilters}");
            Assert.Contains(folderTreeRoot.Descendants(), element => element.Name.LocalName == "Expander" && (string)element.Attribute("Style") == "{StaticResource TreeSectionExpanderStyle}");
            Assert.Contains(folderTreeRoot.Descendants(), element => element.Name.LocalName == "Border" && (string)element.Attribute("Style") == "{StaticResource SupportPanelBorderStyle}");
            Assert.Contains(resultsRoot.Descendants(), element => element.Name.LocalName == "Expander" && (string)element.Attribute("Header") == "{DynamicResource Results.RightsLegend}");
            Assert.Contains(resultsRoot.Descendants(), element => element.Name.LocalName == "Expander" && (string)element.Attribute("Header") == "{DynamicResource Results.PersistentFilters}");
            Assert.Contains(resultsRoot.Descendants(), element => element.Name.LocalName == "Border" && (string)element.Attribute("Style") == "{StaticResource SupportPanelBorderStyle}");
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
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "DataTemplate"
                && (string)element.Attribute("DataType") == "{x:Type sys:String}");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style"
                && (string)element.Attribute("TargetType") == "Control"
                && element.Ancestors().Any(ancestor => ancestor.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal)));
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style"
                && (string)element.Attribute("TargetType") == "Label"
                && element.Ancestors().Any(ancestor => ancestor.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal)));
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style"
                && (string)element.Attribute("TargetType") == "TextBlock"
                && element.Ancestors().Any(ancestor => ancestor.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal)));
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style"
                && (string)element.Attribute("TargetType") == "AccessText"
                && element.Ancestors().Any(ancestor => ancestor.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal)));
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "Style"
                && (string)element.Attribute("TargetType") == "Run"
                && element.Ancestors().Any(ancestor => ancestor.Name.LocalName.EndsWith(".Resources", StringComparison.Ordinal)));
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "ContentPresenter"
                && (string)element.Attribute("TextElement.Foreground") == "{TemplateBinding Foreground}"
                && (string)element.Attribute("RecognizesAccessKey") == "True");
            Assert.Contains(root.Descendants(), element => element.Name.LocalName == "ToggleButton"
                && (string)element.Attribute("Content") == "{TemplateBinding Header}");
        }

        [Fact]
        public void SharedResources_ToolTipTemplate_OpensWithoutRuntimeParseErrors()
        {
            RunInSta(() =>
            {
                var application = EnsureApplication();
                application.Resources.MergedDictionaries.Clear();
                application.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("/NtfsAudit.App;component/Resources/SharedResources.xaml", UriKind.Relative)
                });

                var host = new Window
                {
                    Width = 240,
                    Height = 120,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.None,
                    Content = new TextBlock
                    {
                        Text = "Host surface",
                        ToolTip = new ToolTip
                        {
                            Content = "Tooltip smoke"
                        }
                    }
                };

                try
                {
                    host.Show();
                    var target = (FrameworkElement)host.Content;
                    var toolTip = (ToolTip)target.ToolTip;
                    toolTip.PlacementTarget = target;
                    toolTip.IsOpen = true;
                    DoEvents();

                    Assert.True(toolTip.IsOpen);
                    Assert.NotNull(toolTip.Template);
                    Assert.Equal(Colors.White, ((SolidColorBrush)toolTip.Foreground).Color);
                    Assert.NotNull(toolTip.Template.FindName("ContentSite", toolTip));
                    var accessText = FindDescendant<AccessText>(toolTip);
                    if (accessText != null)
                    {
                        Assert.Equal(Colors.White, ((SolidColorBrush)accessText.Foreground).Color);
                    }

                    var textBlock = FindDescendant<TextBlock>(toolTip);
                    if (textBlock != null)
                    {
                        Assert.Equal(Colors.White, ((SolidColorBrush)textBlock.Foreground).Color);
                    }
                }
                finally
                {
                    if (host.IsVisible)
                    {
                        host.Close();
                    }

                    DoEvents();
                }
            });
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
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "TextBlock" && (string)element.Attribute("Text") == "{DynamicResource Main.NewScan.ToolTip}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "TextBox" && (string)element.Attribute("AutomationProperties.Name") == "{DynamicResource Scan.OutputDirectory.AutomationName}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "TextBox" && (string)element.Attribute(XamlNamespace + "Name") == "OutputDirectoryTextBox");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "TextBox" && (string)element.Attribute("AutomationProperties.Name") == "{DynamicResource Scan.GlobalUser.AutomationName}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "PasswordBox" && (string)element.Attribute("AutomationProperties.Name") == "{DynamicResource Scan.GlobalPassword.AutomationName}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "TextBlock" && (string)element.Attribute("Text") == "{DynamicResource Scan.ServiceModeHint}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "TextBlock" && (string)element.Attribute("Text") == "{DynamicResource Scan.IdentityHint}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "Border" && (string)element.Attribute("Style") == "{StaticResource StatusBadgeBorderStyle}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "Button" && (string)element.Attribute("AutomationProperties.Name") == "{DynamicResource Main.RefreshService.AutomationName}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "Button" && (string)element.Attribute("ToolTip") == "{DynamicResource Main.RefreshService.ToolTip}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "Grid" && (string)element.Attribute("Style") == "{StaticResource FieldRowGridStyle}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "WrapPanel" && (string)element.Attribute("Style") == "{StaticResource InlineActionsPanelStyle}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "TextBlock" && (string)element.Attribute("Style") == "{StaticResource FieldLabelText}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "Button" && (string)element.Attribute("AutomationProperties.Name") == "{DynamicResource Main.NewSchedule.AutomationName}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "Button" && (string)element.Attribute("AutomationProperties.Name") == "{DynamicResource Main.SaveSchedule.AutomationName}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "Button" && (string)element.Attribute("AutomationProperties.Name") == "{DynamicResource Main.DeleteSchedule.AutomationName}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "TextBox" && (string)element.Attribute("AutomationProperties.Name") == "{DynamicResource Main.ScheduleName.AutomationName}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "ComboBox" && (string)element.Attribute("AutomationProperties.Name") == "{DynamicResource Main.ScheduleFrequency.AutomationName}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "DatePicker" && (string)element.Attribute("AutomationProperties.Name") == "{DynamicResource Main.ScheduleDate.AutomationName}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "TextBox" && (string)element.Attribute("AutomationProperties.Name") == "{DynamicResource Main.ScheduleTime.AutomationName}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "ComboBox" && (string)element.Attribute("AutomationProperties.Name") == "{DynamicResource Main.ScheduleDayOfWeek.AutomationName}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "TextBox" && (string)element.Attribute("AutomationProperties.Name") == "{DynamicResource Main.ScheduleDayOfMonth.AutomationName}");
            Assert.Contains(settingsRoot.Descendants(), element => element.Name.LocalName == "CheckBox" && (string)element.Attribute("AutomationProperties.Name") == "{DynamicResource Main.ScheduleEnabled.AutomationName}");
        }

        [Fact]
        public void PrincipalDetailsWindow_UsesSharedPanelChrome()
        {
            var principalDetailsRoot = LoadXaml("src", "NtfsAudit.App", "PrincipalDetailsWindow.xaml");

            Assert.Contains(principalDetailsRoot.Descendants(), element => element.Name.LocalName == "Border" && (string)element.Attribute("Style") == "{StaticResource PanelBorderStyle}");
            Assert.Contains(principalDetailsRoot.Descendants(), element => element.Name.LocalName == "Grid" && (string)element.Attribute("Margin") == "0,10,0,0");
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
            Assert.Contains(resultsRoot.Descendants(), element => element.Name.LocalName == "Border"
                && (string)element.Attribute("Background") == "{StaticResource PermissionModifyBrush}"
                && (string)element.Attribute("ToolTip") == "{DynamicResource Badge.Modify.ToolTip}");
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
            Assert.DoesNotContain(resultsRoot.Descendants(), element => element.Name.LocalName == "TextBlock"
                && (string)element.Attribute("Text") == "{DynamicResource Results.SelectFolderForDetails}");
            Assert.Contains(resultsRoot.Descendants(), element => element.Name.LocalName == "TextBlock"
                && (string)element.Attribute("Text") == "{DynamicResource Results.SelectTreeFolder}");
        }

        [Fact]
        public void FolderTreeNodes_ExposeFullNameTooltipForTrimmedLabels()
        {
            var folderTreeRoot = LoadXaml("src", "NtfsAudit.App", "Views", "FolderTreePanel.xaml");

            // Tree nodes now use Segoe MDL2 Assets icon instead of TypeLabel text.
            Assert.Contains(folderTreeRoot.Descendants(), element => element.Name.LocalName == "TextBlock"
                && (string)element.Attribute("FontFamily") == "Segoe MDL2 Assets");
            Assert.Contains(folderTreeRoot.Descendants(), element => element.Name.LocalName == "TextBlock"
                && (string)element.Attribute("Text") == "{Binding DisplayName}"
                && (string)element.Attribute("TextTrimming") == "CharacterEllipsis"
                && (string)element.Attribute("ToolTip") == "{Binding DisplayName}");
            Assert.Contains(folderTreeRoot.Descendants(), element => element.Name.LocalName == "TextBlock"
                && (string)element.Attribute("Text") == "{DynamicResource Tree.DiffParent.Badge}");
            Assert.Contains(folderTreeRoot.Descendants(), element => element.Name.LocalName == "TextBlock"
                && (string)element.Attribute("Text") == "{DynamicResource Tree.WithFiles.Badge}");
            Assert.Contains(folderTreeRoot.Descendants(), element => element.Name.LocalName == "TextBlock"
                && (string)element.Attribute("Text") == "{DynamicResource Tree.BaselineMismatch.Badge}");
            Assert.Contains(folderTreeRoot.Descendants(), element => element.Name.LocalName == "Border"
                && (string)element.Attribute("Background") == "{StaticResource PermissionDifferenceBrush}");
            Assert.Contains(folderTreeRoot.Descendants(), element => element.Name.LocalName == "Border"
                && (string)element.Attribute("Background") == "{StaticResource PermissionProtectedBrush}");
            Assert.DoesNotContain(folderTreeRoot.Descendants(), element => element.Name.LocalName == "TextBlock"
                && (string)element.Attribute("Text") == "{Binding DiffLabel}");
            Assert.DoesNotContain(folderTreeRoot.Descendants(), element => element.Name.LocalName == "TextBlock"
                && (string)element.Attribute("Text") == "{Binding BaselineMismatchLabel}");
            Assert.DoesNotContain(folderTreeRoot.Descendants(), element => element.Name.LocalName == "TextBlock"
                && (string)element.Attribute("Text") == "{Binding ExplicitNtfsLabel}");
            Assert.DoesNotContain(folderTreeRoot.Descendants(), element => element.Name.LocalName == "TextBlock"
                && (string)element.Attribute("Text") == "{DynamicResource Tree.RootTypeLabel}");
        }

        private static XElement LoadXaml(params string[] parts)
        {
            var root = FindRepositoryRoot();
            var path = Path.Combine(new[] { root }.Concat(parts).ToArray());
            return XDocument.Load(path).Root;
        }

        private static Application EnsureApplication()
        {
            return Application.Current ?? new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown
            };
        }

        private static void RunInSta(Action action)
        {
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null)
            {
                throw failure;
            }
        }

        private static void DoEvents()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        private static T FindDescendant<T>(DependencyObject root)
            where T : DependencyObject
        {
            if (root == null)
            {
                return null;
            }

            var count = VisualTreeHelper.GetChildrenCount(root);
            for (var index = 0; index < count; index++)
            {
                var child = VisualTreeHelper.GetChild(root, index);
                if (child is T typedChild)
                {
                    return typedChild;
                }

                var descendant = FindDescendant<T>(child);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            if (root is FrameworkElement frameworkElement)
            {
                foreach (var logicalChild in LogicalTreeHelper.GetChildren(frameworkElement))
                {
                    if (logicalChild is DependencyObject logicalDependencyObject)
                    {
                        if (logicalDependencyObject is T typedLogicalChild)
                        {
                            return typedLogicalChild;
                        }

                        var logicalDescendant = FindDescendant<T>(logicalDependencyObject);
                        if (logicalDescendant != null)
                        {
                            return logicalDescendant;
                        }
                    }
                }
            }

            return null;
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
