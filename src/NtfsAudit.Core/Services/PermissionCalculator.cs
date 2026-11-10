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
using System.Security.AccessControl;
using NtfsAudit.Core.Models;

namespace NtfsAudit.Core.Services
{
    public static class PermissionCalculator
    {
        public static readonly int FullControlMask = (int)FileSystemRights.FullControl;

        public static Dictionary<string, AccessAccumulator> BuildAccessMap(IEnumerable<PermissionEntry> permissions, bool includeInherited)
        {
            var map = new Dictionary<string, AccessAccumulator>(StringComparer.OrdinalIgnoreCase);
            if (permissions == null) return map;
            foreach (var permission in permissions)
            {
                if (!includeInherited && permission.IsInherited)
                {
                    continue;
                }
                if (string.IsNullOrWhiteSpace(permission.PrincipalSid)) continue;
                if (!map.TryGetValue(permission.PrincipalSid, out var accumulator))
                {
                    accumulator = new AccessAccumulator();
                    map[permission.PrincipalSid] = accumulator;
                }

                if (permission.AccessType == PermissionDecision.Allow)
                {
                    accumulator.Allow |= permission.RightsMask;
                }
                else
                {
                    accumulator.Deny |= permission.RightsMask;
                }
            }
            return map;
        }

        public static int GetEffectiveMask(Dictionary<string, AccessAccumulator> map, string sid)
        {
            if (map == null || string.IsNullOrWhiteSpace(sid)) return 0;
            if (!map.TryGetValue(sid, out var accumulator))
            {
                return 0;
            }
            return accumulator.Allow & ~accumulator.Deny;
        }

        public static int IntersectMasks(int ntfsMask, int shareMask, bool hasShare)
        {
            if (!hasShare) return ntfsMask;
            return ntfsMask & shareMask;
        }

        public static PermissionScope ResolveScope(InheritanceFlags inheritanceFlags, PropagationFlags propagationFlags)
        {
            var appliesToThisFolder = true;
            var appliesToSubfolders = inheritanceFlags.HasFlag(InheritanceFlags.ContainerInherit);
            var appliesToFiles = inheritanceFlags.HasFlag(InheritanceFlags.ObjectInherit);

            if (propagationFlags.HasFlag(PropagationFlags.InheritOnly))
            {
                appliesToThisFolder = false;
            }

            if (propagationFlags.HasFlag(PropagationFlags.NoPropagateInherit))
            {
                appliesToSubfolders = false;
                appliesToFiles = false;
            }

            return new PermissionScope(appliesToThisFolder, appliesToSubfolders, appliesToFiles);
        }

        public readonly struct PermissionScope
        {
            public PermissionScope(bool appliesToThisFolder, bool appliesToSubfolders, bool appliesToFiles)
            {
                AppliesToThisFolder = appliesToThisFolder;
                AppliesToSubfolders = appliesToSubfolders;
                AppliesToFiles = appliesToFiles;
            }

            public bool AppliesToThisFolder { get; }
            public bool AppliesToSubfolders { get; }
            public bool AppliesToFiles { get; }
        }

        public class AccessAccumulator
        {
            public int Allow { get; set; }
            public int Deny { get; set; }
        }
    }
}
