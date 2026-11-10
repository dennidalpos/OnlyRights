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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Markup;

namespace NtfsAudit.App.Services
{
    public static class LocalizationManager
    {
        public const string DefaultLocale = "en";
        public const string FallbackLocale = "en";

        private const string ResourcePrefix = "Resources/Strings.";
        private static readonly List<LocaleOption> _supportedLocales = DiscoverSupportedLocales();

        private static string _currentLocale = DefaultLocale;
        private static ResourceDictionary _activeResources;

        public static event EventHandler LocaleChanged;

        public static IReadOnlyList<LocaleOption> SupportedLocales
        {
            get { return _supportedLocales; }
        }

        public static string CurrentLocale
        {
            get { return _currentLocale; }
        }

        public static LocaleOption ResolveLocale(string locale)
        {
            if (string.IsNullOrWhiteSpace(locale))
            {
                return _supportedLocales[0];
            }

            var normalizedLocale = locale.Trim();
            var exactMatch = _supportedLocales.FirstOrDefault(item => string.Equals(item.Code, normalizedLocale, StringComparison.OrdinalIgnoreCase));
            if (exactMatch != null)
            {
                return exactMatch;
            }

            var separatorIndex = normalizedLocale.IndexOfAny(new[] { '-', '_' });
            if (separatorIndex > 0)
            {
                var neutralLocale = normalizedLocale.Substring(0, separatorIndex);
                var neutralMatch = _supportedLocales.FirstOrDefault(item => string.Equals(item.Code, neutralLocale, StringComparison.OrdinalIgnoreCase));
                if (neutralMatch != null)
                {
                    return neutralMatch;
                }
            }

            return _supportedLocales[0];
        }

        public static void ApplyDefault()
        {
            Apply(DefaultLocale);
        }

        public static void Apply(string locale)
        {
            var resolved = ResolveLocale(locale);
            var application = Application.Current;
            if (application != null)
            {
                Apply(application.Resources, resolved.Code);
            }
            else
            {
                _activeResources = null;
            }

            var changed = !string.Equals(_currentLocale, resolved.Code, StringComparison.OrdinalIgnoreCase);
            _currentLocale = resolved.Code;
            var culture = CultureInfo.GetCultureInfo(resolved.Code);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            if (changed && LocaleChanged != null)
            {
                LocaleChanged(null, EventArgs.Empty);
            }
        }

        public static void Apply(ResourceDictionary resources, string locale)
        {
            if (resources == null)
            {
                throw new ArgumentNullException("resources");
            }

            var resolved = ResolveLocale(locale);
            RemoveStringDictionaries(resources);
            resources.MergedDictionaries.Add(CreateDictionary(FallbackLocale));
            if (!string.Equals(resolved.Code, FallbackLocale, StringComparison.OrdinalIgnoreCase))
            {
                resources.MergedDictionaries.Add(CreateDictionary(resolved.Code));
            }

            _currentLocale = resolved.Code;
            _activeResources = resources;
            var culture = CultureInfo.GetCultureInfo(resolved.Code);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }

        public static string Text(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            var resources = _activeResources ?? (Application.Current == null ? null : Application.Current.Resources);
            var value = TryFindString(resources, key);
            if (value != null)
            {
                return value;
            }

            value = TryFindString(CreateDictionary(CurrentLocale), key) ?? TryFindString(CreateDictionary(FallbackLocale), key);
            return value ?? key;
        }

        public static string Format(string key, params object[] args)
        {
            var format = Text(key);
            return args == null || args.Length == 0
                ? format
                : string.Format(CultureInfo.CurrentCulture, format, args);
        }

        private static string TryFindString(ResourceDictionary resources, string key)
        {
            if (resources == null)
            {
                return null;
            }

            try
            {
                if (resources.Contains(key))
                {
                    return resources[key] as string;
                }
            }
            catch
            {
                return null;
            }

            foreach (var dictionary in resources.MergedDictionaries.Cast<ResourceDictionary>().Reverse())
            {
                var value = TryFindString(dictionary, key);
                if (value != null)
                {
                    return value;
                }
            }

            return null;
        }

        private static void RemoveStringDictionaries(ResourceDictionary resources)
        {
            for (var i = resources.MergedDictionaries.Count - 1; i >= 0; i--)
            {
                var dictionary = resources.MergedDictionaries[i];
                if (dictionary != null)
                {
                    if ((dictionary.Source != null && dictionary.Source.OriginalString.IndexOf(ResourcePrefix, StringComparison.OrdinalIgnoreCase) >= 0)
                        || dictionary.Contains("App.Title"))
                    {
                        resources.MergedDictionaries.RemoveAt(i);
                    }
                }
            }
        }

        private static ResourceDictionary CreateDictionary(string locale)
        {
            if (Application.Current == null)
            {
                return LoadLooseDictionary(locale);
            }

            return new ResourceDictionary
            {
                Source = new Uri(string.Format("/NtfsAudit.App;component/Resources/Strings.{0}.xaml", locale), UriKind.Relative)
            };
        }

        private static ResourceDictionary LoadLooseDictionary(string locale)
        {
            var baseDirectories = new[]
            {
                AppContext.BaseDirectory,
                typeof(LocalizationManager).Assembly.Location,
                AppDomain.CurrentDomain.BaseDirectory
            };

            foreach (var baseDir in baseDirectories)
            {
                if (string.IsNullOrWhiteSpace(baseDir))
                {
                    continue;
                }

                try
                {
                    var current = new DirectoryInfo(File.Exists(baseDir) ? Path.GetDirectoryName(baseDir) : baseDir);
                    while (current != null)
                    {
                        var candidate = Path.Combine(current.FullName, "src", "NtfsAudit.App", "Resources", string.Format("Strings.{0}.xaml", locale));
                        if (File.Exists(candidate))
                        {
                            using (var stream = File.OpenRead(candidate))
                            {
                                return (ResourceDictionary)XamlReader.Load(stream);
                            }
                        }

                        current = current.Parent;
                    }
                }
                catch
                {
                }
            }

            return new ResourceDictionary();
        }

        private static List<LocaleOption> DiscoverSupportedLocales()
        {
            var locales = new List<LocaleOption>();
            var codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var assembly = typeof(LocalizationManager).Assembly;
                var resourceName = assembly.GetName().Name + ".g";
                var resourceManager = new System.Resources.ResourceManager(resourceName, assembly);
                var resourceSet = resourceManager.GetResourceSet(CultureInfo.InvariantCulture, true, true);
                if (resourceSet != null)
                {
                    foreach (System.Collections.DictionaryEntry entry in resourceSet)
                    {
                        var key = entry.Key as string;
                        if (key != null && key.StartsWith("resources/strings.", StringComparison.OrdinalIgnoreCase) && key.EndsWith(".baml", StringComparison.OrdinalIgnoreCase))
                        {
                            var parts = key.Split('.');
                            if (parts.Length >= 3)
                            {
                                var code = parts[parts.Length - 2];
                                if (!string.IsNullOrWhiteSpace(code) && code.Length == 2)
                                {
                                    codes.Add(code.ToLowerInvariant());
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            try
            {
                var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                while (current != null)
                {
                    var resourcesDir = Path.Combine(current.FullName, "src", "NtfsAudit.App", "Resources");
                    if (Directory.Exists(resourcesDir))
                    {
                        var files = Directory.GetFiles(resourcesDir, "Strings.*.xaml");
                        foreach (var file in files)
                        {
                            var name = Path.GetFileNameWithoutExtension(file);
                            var parts = name.Split('.');
                            if (parts.Length >= 2)
                            {
                                var code = parts[1];
                                if (!string.IsNullOrWhiteSpace(code) && code.Length == 2)
                                {
                                    codes.Add(code.ToLowerInvariant());
                                }
                            }
                        }
                        break;
                    }
                    current = current.Parent;
                }
            }
            catch
            {
            }

            codes.Add("en");
            codes.Add("it");

            foreach (var code in codes.OrderBy(c => c))
            {
                string displayName = code == "en" ? "English" : (code == "it" ? "Italiano" : code.ToUpperInvariant());
                try
                {
                    var dict = CreateDictionary(code);
                    if (dict != null && dict.Contains("Locale.Name"))
                    {
                        var nameVal = dict["Locale.Name"] as string;
                        if (!string.IsNullOrWhiteSpace(nameVal))
                        {
                            displayName = nameVal;
                        }
                    }
                }
                catch
                {
                }
                locales.Add(new LocaleOption(code, displayName));
            }

            return locales;
        }
    }
}
