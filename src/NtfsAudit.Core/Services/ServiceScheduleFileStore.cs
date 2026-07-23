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
using Newtonsoft.Json;
using NtfsAudit.Core.Models;

#nullable enable

namespace NtfsAudit.Core.Services
{
    public sealed class ServiceScheduleFileStore
    {
        private const string InvalidDirectoryName = "invalid";
        private readonly string _schedulesRoot;
        private readonly string _statusPath;

        public ServiceScheduleFileStore()
            : this(RuntimePaths.GetSchedulesRoot(), RuntimePaths.GetScheduleStatusPath())
        {
        }

        internal ServiceScheduleFileStore(string schedulesRoot, string statusPath)
        {
            _schedulesRoot = schedulesRoot;
            _statusPath = statusPath;
        }

        public IReadOnlyList<ServiceScheduleDefinition> LoadDefinitions()
        {
            if (string.IsNullOrWhiteSpace(_schedulesRoot) || !Directory.Exists(_schedulesRoot))
            {
                return new List<ServiceScheduleDefinition>();
            }

            var definitions = new List<ServiceScheduleDefinition>();
            foreach (var file in Directory.GetFiles(_schedulesRoot, "schedule_*.json").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (!TryLoadDefinition(file, out var definition, out var failureReason))
                {
                    TryQuarantine(file, failureReason);
                    continue;
                }

                definitions.Add(definition!);
            }

            return definitions;
        }

        public void SaveDefinition(ServiceScheduleDefinition definition)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            if (string.IsNullOrWhiteSpace(definition.ScheduleId))
            {
                definition.ScheduleId = Guid.NewGuid().ToString("N");
            }

            Directory.CreateDirectory(_schedulesRoot);
            SecurityHardeningHelper.SecureDirectory(_schedulesRoot);
            var path = GetDefinitionPath(definition.ScheduleId);
            File.WriteAllText(path, JsonConvert.SerializeObject(definition, Formatting.Indented));
            SecurityHardeningHelper.SecureFile(path);
        }

        public bool DeleteDefinition(string scheduleId)
        {
            var path = GetDefinitionPath(scheduleId);
            if (!File.Exists(path))
            {
                return false;
            }

            File.Delete(path);
            return true;
        }

        public ServiceScheduleRuntimeSnapshot LoadRuntimeSnapshot()
        {
            if (string.IsNullOrWhiteSpace(_statusPath) || !File.Exists(_statusPath))
            {
                return new ServiceScheduleRuntimeSnapshot();
            }

            try
            {
                return JsonConvert.DeserializeObject<ServiceScheduleRuntimeSnapshot>(File.ReadAllText(_statusPath))
                    ?? new ServiceScheduleRuntimeSnapshot();
            }
            catch
            {
                return new ServiceScheduleRuntimeSnapshot();
            }
        }

        public void SaveRuntimeSnapshot(ServiceScheduleRuntimeSnapshot snapshot)
        {
            var dir = Path.GetDirectoryName(_statusPath) ?? RuntimePaths.GetCommonDataRoot();
            Directory.CreateDirectory(dir);
            SecurityHardeningHelper.SecureDirectory(dir);
            File.WriteAllText(_statusPath, JsonConvert.SerializeObject(snapshot ?? new ServiceScheduleRuntimeSnapshot(), Formatting.Indented));
            SecurityHardeningHelper.SecureFile(_statusPath);
        }

        internal bool TryLoadDefinition(string filePath, out ServiceScheduleDefinition? definition, out string? failureReason)
        {
            definition = null;
            failureReason = null;

            try
            {
                definition = JsonConvert.DeserializeObject<ServiceScheduleDefinition>(File.ReadAllText(filePath));
            }
            catch (Exception ex)
            {
                failureReason = string.Format("schedule corrotta: {0}", ex.Message);
                return false;
            }

            if (definition == null || string.IsNullOrWhiteSpace(definition.ScheduleId))
            {
                failureReason = "schedule vuota o senza identificativo";
                return false;
            }

            if (definition.Template == null || definition.Template.Roots == null || definition.Template.Roots.Count == 0)
            {
                failureReason = "schedule senza root valide";
                return false;
            }

            return true;
        }

        private string? TryQuarantine(string filePath, string? failureReason)
        {
            try
            {
                return Quarantine(filePath, failureReason);
            }
            catch
            {
                return null;
            }
        }

        internal string? Quarantine(string filePath, string? failureReason)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            var invalidRoot = Path.Combine(Path.GetDirectoryName(filePath) ?? _schedulesRoot, InvalidDirectoryName);
            Directory.CreateDirectory(invalidRoot);
            var destinationFile = Path.Combine(
                invalidRoot,
                string.Format("{0}_{1}.json", Path.GetFileNameWithoutExtension(filePath), DateTime.UtcNow.ToString("yyyyMMddHHmmss")));
            File.Move(filePath, destinationFile);
            File.WriteAllText(destinationFile + ".txt", failureReason ?? "schedule non valida");
            return destinationFile;
        }

        private string GetDefinitionPath(string scheduleId)
        {
            return Path.Combine(_schedulesRoot, string.Format("schedule_{0}.json", scheduleId));
        }
    }
}
