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
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public sealed class ScanRootPathKindLabelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var path = value as string;
            var kind = PathResolver.DetectPathKind(path);
            switch (kind)
            {
                case PathKind.Dfs:
                    return LocalizationManager.Text("PathKind.Dfs");
                case PathKind.WslUnc:
                    return LocalizationManager.Text("PathKind.WslUnc");
                case PathKind.UncSmb:
                    return LocalizationManager.Text("PathKind.UncSmb");
                case PathKind.Local:
                    return LocalizationManager.Text("PathKind.Local");
                case PathKind.Unsupported:
                    return LocalizationManager.Text("PathKind.Unsupported");
                default:
                    return LocalizationManager.Text("PathKind.Unknown");
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class ScanRootPathKindBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var path = value as string;
            var kind = PathResolver.DetectPathKind(path);
            switch (kind)
            {
                case PathKind.Dfs:
                    return new SolidColorBrush(Color.FromRgb(232, 245, 233));
                case PathKind.WslUnc:
                    return new SolidColorBrush(Color.FromRgb(255, 243, 224));
                case PathKind.UncSmb:
                    return new SolidColorBrush(Color.FromRgb(227, 242, 253));
                case PathKind.Unsupported:
                    return new SolidColorBrush(Color.FromRgb(255, 235, 238));
                default:
                    return new SolidColorBrush(Color.FromRgb(245, 245, 245));
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class DfsMultiServerVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var path = value as string;
            if (PathResolver.DetectPathKind(path) != PathKind.Dfs)
            {
                return Visibility.Collapsed;
            }

            var targets = PathResolver.GetDfsTargets(path);
            return targets != null && targets.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }

    public sealed class HeightPercentageConverter : IValueConverter
    {
        public double Percentage { get; set; } = 0.4;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double height)
            {
                var pct = Percentage;
                if (parameter != null && double.TryParse(parameter.ToString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var p))
                {
                    pct = p;
                }
                var calculated = height * pct;
                return calculated > 50.0 ? calculated : 50.0;
            }
            return 280.0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
