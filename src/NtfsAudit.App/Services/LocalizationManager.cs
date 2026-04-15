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
        private static readonly List<LocaleOption> _supportedLocales = new List<LocaleOption>
        {
            new LocaleOption("en", "English"),
            new LocaleOption("it", "Italiano")
        };

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

            return _supportedLocales.FirstOrDefault(item => string.Equals(item.Code, locale.Trim(), StringComparison.OrdinalIgnoreCase))
                ?? _supportedLocales[0];
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

            var changed = !string.Equals(_currentLocale, resolved.Code, StringComparison.OrdinalIgnoreCase);
            _currentLocale = resolved.Code;
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(resolved.Code);
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
        }

        public static string Text(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            var resources = Application.Current == null ? _activeResources : Application.Current.Resources;
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
                if (dictionary != null
                    && dictionary.Source != null
                    && dictionary.Source.OriginalString.IndexOf(ResourcePrefix, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    resources.MergedDictionaries.RemoveAt(i);
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
            try
            {
                var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
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

            return new ResourceDictionary();
        }
    }
}
