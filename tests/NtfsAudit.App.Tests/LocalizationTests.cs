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
using System.Collections.Generic;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using NtfsAudit.App.Models;
using NtfsAudit.App.Services;
using Xunit;

namespace NtfsAudit.App.Tests
{
    public class LocalizationTests
    {
        [Fact]
        public void SupportedLocales_IncludeEnglishAndItalian_WithEnglishDefault()
        {
            Assert.Equal("en", LocalizationManager.DefaultLocale);
            Assert.Equal("en", LocalizationManager.FallbackLocale);
            Assert.Contains(LocalizationManager.SupportedLocales, locale => locale.Code == "en");
            Assert.Contains(LocalizationManager.SupportedLocales, locale => locale.Code == "it");
        }

        [Fact]
        public void UnsupportedLocale_FallsBackToEnglish()
        {
            Assert.Equal("en", LocalizationManager.ResolveLocale("fr").Code);
        }

        [Fact]
        public void CultureSpecificLocale_ResolvesToSupportedNeutralLocale()
        {
            Assert.Equal("it", LocalizationManager.ResolveLocale("it-IT").Code);
            Assert.Equal("en", LocalizationManager.ResolveLocale("en-US").Code);
        }

        [Fact]
        public void MissingKey_ReturnsVisibleKeyMarker()
        {
            LocalizationManager.Apply(new ResourceDictionary(), "en");

            Assert.Equal("Missing.Localization.Key", LocalizationManager.Text("Missing.Localization.Key"));
        }

        [Fact]
        public void ItalianDictionary_UsesSameKeysAsEnglish()
        {
            var englishKeys = LoadResourceKeys("Strings.en.xaml");
            var italianKeys = LoadResourceKeys("Strings.it.xaml");

            Assert.Equal(englishKeys, italianKeys);
            Assert.Equal(englishKeys.Count, englishKeys.Distinct().Count());
            Assert.Contains("Scan.ApplyCompatibleOptions", englishKeys);
            Assert.Contains("Scan.CompatibleOptionsHint", englishKeys);
            Assert.Contains("Progress.CredentialsGlobalCurrentUser", englishKeys);
            Assert.Contains("Progress.CompatibleOptionsApplied", englishKeys);
            Assert.Contains("Validation.CredentialsGlobalPartial", englishKeys);
            Assert.Contains("Scan.RootBrowse.AutomationName", englishKeys);
            Assert.Contains("Tree.BadgesLegend", englishKeys);
            Assert.Contains("Results.RiskSummaryFormat", englishKeys);
            Assert.Contains("Dialog.GroupDetails", englishKeys);
            Assert.Contains("Progress.ErrorsPartiallyLoaded", englishKeys);
            Assert.Contains("Settings.ServiceSchedulingAvailable", englishKeys);
            Assert.Contains("Schedule.Day.Monday", englishKeys);
            Assert.Contains("Grid.ReadAndExecute", englishKeys);
            Assert.Contains("App.SingleInstanceInitializationError", englishKeys);
        }

        [Fact]
        public void UiPreferences_LocaleField_IsOptionalAndNonBreaking()
        {
            var legacyJson = @"{ ""ShowAllow"": true, ""ScanAllDepths"": true }";
            var parsed = JObject.Parse(legacyJson);

            Assert.Null(parsed["Locale"]);
        }

        [Fact]
        public void ServiceRuntimeStatusPresenter_UsesActiveLocale()
        {
            LocalizationManager.Apply(new ResourceDictionary(), "en");
            var presenter = new ServiceRuntimeStatusPresenter();

            var viewState = presenter.Build(true, true, new ServiceRuntimeStatus
            {
                IsRunning = true,
                CurrentRootPath = @"C:\Data",
                CurrentRootIndex = 1,
                TotalRoots = 2
            });

            Assert.Equal("Service active", viewState.BadgeText);
            Assert.Contains("Service running", viewState.StatusText);
        }

        [Fact]
        public void ScanRootPathKindConverter_UsesLocalizedResourceStrings()
        {
            var resources = new ResourceDictionary();
            var converter = new ScanRootPathKindLabelConverter();

            LocalizationManager.Apply(resources, "en");
            Assert.Equal("Local", converter.Convert(@"C:\Data", typeof(string), null, null));
            Assert.Equal("UNC / SMB", converter.Convert(@"\\server\share", typeof(string), null, null));

            LocalizationManager.Apply(resources, "it");
            Assert.Equal("Locale", converter.Convert(@"C:\Data", typeof(string), null, null));
            Assert.Equal("UNC / SMB", converter.Convert(@"\\server\share", typeof(string), null, null));
        }

        private static SortedSet<string> LoadResourceKeys(string fileName)
        {
            var root = FindRepositoryRoot();
            var document = XDocument.Load(Path.Combine(root, "src", "NtfsAudit.App", "Resources", fileName));
            var xamlKey = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");
            return new SortedSet<string>(document.Descendants().Select(element => (string)element.Attribute(xamlKey)).Where(key => !string.IsNullOrWhiteSpace(key)));
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
