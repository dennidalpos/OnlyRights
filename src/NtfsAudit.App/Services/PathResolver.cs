using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NtfsAudit.App.Services
{
    public static class PathResolver
    {
        public static string NormalizeRootPath(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return input;
            var trimmed = input.Trim();
            if (trimmed.StartsWith("\\\\"))
            {
                var dfsResolved = TryResolveDfsPath(trimmed);
                return dfsResolved ?? trimmed;
            }

            if (trimmed.Length < 2 || trimmed[1] != ':') return trimmed;

            var drive = trimmed.Substring(0, 2);
            var uncRoot = TryGetUncPath(drive);
            if (string.IsNullOrWhiteSpace(uncRoot)) return trimmed;

            var suffix = trimmed.Length > 2 ? trimmed.Substring(2) : string.Empty;
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
            var parts = uncPath.TrimStart('\\').Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) return null;

            var dfsRoot = string.Format("\\\\{0}\\{1}", parts[0], parts[1]);
            var remainder = parts.Length > 2 ? string.Join("\\", parts, 2, parts.Length - 2) : string.Empty;
            IntPtr buffer;
            var status = NetDfsGetInfo(dfsRoot, null, null, 3, out buffer);
            if (status != 0 || buffer == IntPtr.Zero) return null;

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
                        return candidate;
                    }
                }

                return firstCandidate;
            }
            finally
            {
                NetApiBufferFree(buffer);
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
