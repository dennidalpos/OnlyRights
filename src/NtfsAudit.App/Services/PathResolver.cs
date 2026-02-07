using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NtfsAudit.App.Services
{
    public static class PathResolver
    {
        private static readonly Dictionary<string, string> DfsCache =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string ToExtendedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal)) return path;
            if (path.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return @"\\?\UNC\" + path.TrimStart('\\');
            }

            return @"\\?\" + path;
        }

        public static string FromExtendedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return path;
            if (path.StartsWith(@"\\?\UNC\", StringComparison.Ordinal))
            {
                return @"\\" + path.Substring(8);
            }

            if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                return path.Substring(4);
            }

            return path;
        }

        public static string NormalizeRootPath(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;
            var trimmed = input.Trim();
            var normalized = FromExtendedPath(trimmed);
            if (normalized.StartsWith("\\\\"))
            {
                var dfsResolved = TryResolveDfsPath(normalized);
                return dfsResolved ?? normalized;
            }

            if (normalized.Length < 2 || normalized[1] != ':') return normalized;

            var drive = normalized.Substring(0, 2);
            var uncRoot = TryGetUncPath(drive);
            if (string.IsNullOrWhiteSpace(uncRoot)) return normalized;

            var suffix = normalized.Length > 2 ? normalized.Substring(2) : string.Empty;
            var relative = suffix.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var combined = string.IsNullOrEmpty(relative) ? uncRoot : Path.Combine(uncRoot, relative);
            var dfsResolvedCombined = TryResolveDfsPath(combined);
            return dfsResolvedCombined ?? combined;
        }

        private static string TryGetUncPath(string drive)
        {
            var length = 512;
            var builder = new StringBuilder(length);
            var result = WNetGetConnection(drive, builder, ref length);
            return result == 0 ? builder.ToString() : null;
        }

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetGetConnection(string localName, StringBuilder remoteName, ref int length);

        private static string TryResolveDfsPath(string uncPath)
        {
            if (string.IsNullOrWhiteSpace(uncPath)) return null;
            var normalized = FromExtendedPath(uncPath);
            if (!normalized.StartsWith("\\\\", StringComparison.Ordinal)) return null;

            string cached;
            lock (DfsCache)
            {
                if (DfsCache.TryGetValue(normalized, out cached))
                {
                    return string.IsNullOrWhiteSpace(cached) ? null : cached;
                }
            }

            var parts = normalized.TrimStart('\\').Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;

            var dfsRoot = string.Format("\\\\{0}\\{1}", parts[0], parts[1]);
            var remainder = parts.Length > 2 ? string.Join("\\", parts, 2, parts.Length - 2) : string.Empty;
            IntPtr buffer;
            try
            {
                var status = NetDfsGetInfo(dfsRoot, null, null, 3, out buffer);
                if (status != 0 || buffer == IntPtr.Zero)
                {
                    CacheDfs(normalized, null);
                    return null;
                }
            }
            catch
            {
                CacheDfs(normalized, null);
                return null;
            }

            try
            {
                var info = Marshal.PtrToStructure<DFS_INFO_3>(buffer);
                var storages = ReadStorages(info.Storage, (int)info.NumberOfStorages);
                string firstCandidate = null;

                foreach (var storage in storages)
                {
                    var root = string.Format("\\\\{0}\\{1}", storage.ServerName, storage.ShareName);
                    var candidate = string.IsNullOrEmpty(remainder) ? root : Path.Combine(root, remainder);
                    if (firstCandidate == null)
                    {
                        firstCandidate = candidate;
                    }

                    if (Directory.Exists(candidate))
                    {
                        CacheDfs(normalized, candidate);
                        return candidate;
                    }
                }

                CacheDfs(normalized, firstCandidate);
                return firstCandidate;
            }
            finally
            {
                NetApiBufferFree(buffer);
            }
        }

        private static void CacheDfs(string key, string value)
        {
            lock (DfsCache)
            {
                DfsCache[key] = value ?? string.Empty;
            }
        }

        private static List<DfsStorageInfo> ReadStorages(IntPtr buffer, int count)
        {
            var result = new List<DfsStorageInfo>();
            if (buffer == IntPtr.Zero || count <= 0) return result;
            var size = Marshal.SizeOf(typeof(DFS_STORAGE_INFO));
            for (var i = 0; i < count; i++)
            {
                var current = new IntPtr(buffer.ToInt64() + (i * size));
                var info = Marshal.PtrToStructure<DFS_STORAGE_INFO>(current);
                result.Add(new DfsStorageInfo
                {
                    State = info.State,
                    ServerName = info.ServerName,
                    ShareName = info.ShareName
                });
            }
            return result;
        }

        [DllImport("Netapi32.dll", CharSet = CharSet.Unicode)]
        private static extern int NetDfsGetInfo(string dfsEntryPath, string serverName, string shareName, int level, out IntPtr buffer);

        [DllImport("Netapi32.dll")]
        private static extern int NetApiBufferFree(IntPtr buffer);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DFS_INFO_3
        {
            public string EntryPath;
            public string Comment;
            public uint State;
            public uint NumberOfStorages;
            public IntPtr Storage;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DFS_STORAGE_INFO
        {
            public uint State;
            public string ServerName;
            public string ShareName;
        }

        private class DfsStorageInfo
        {
            public uint State { get; set; }
            public string ServerName { get; set; }
            public string ShareName { get; set; }
        }
    }
}
