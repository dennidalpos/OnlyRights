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
                    return "DFS";
                case PathKind.Unc:
                    return "SMB Share";
                case PathKind.Nfs:
                    return "NFS";
                case PathKind.Local:
                    return "Locale";
                default:
                    return "Sconosciuto";
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
                case PathKind.Unc:
                    return new SolidColorBrush(Color.FromRgb(227, 242, 253));
                case PathKind.Nfs:
                    return new SolidColorBrush(Color.FromRgb(255, 243, 224));
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
}
