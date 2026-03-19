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
using NtfsAudit.App.Cache;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public sealed class ScanCredentialStore
    {
        private readonly string _storePath;

        public ScanCredentialStore()
        {
            _storePath = new LocalCacheStore().GetCacheFilePath("scan-credentials.json");
        }

        public ScanCredentialSettings Load()
        {
            try
            {
                if (!File.Exists(_storePath))
                {
                    return new ScanCredentialSettings();
                }

                var payload = JsonConvert.DeserializeObject<CredentialStorePayload>(File.ReadAllText(_storePath));
                if (payload == null)
                {
                    return new ScanCredentialSettings();
                }

                return new ScanCredentialSettings
                {
                    GlobalCredential = payload.GlobalCredential == null
                        ? null
                        : ScanCredentialProtector.ResolveForRuntime(payload.GlobalCredential),
                    RootOverrides = (payload.RootOverrides ?? new List<CredentialOverridePayload>())
                        .Where(item => item != null && !string.IsNullOrWhiteSpace(item.RootPath))
                        .ToDictionary(
                            item => NormalizeRoot(item.RootPath),
                            item => ScanCredentialProtector.ResolveForRuntime(item.Credential),
                            StringComparer.OrdinalIgnoreCase)
                };
            }
            catch
            {
                return new ScanCredentialSettings();
            }
        }

        public void SaveGlobal(ScanCredential credential)
        {
            var payload = LoadPayload();
            payload.GlobalCredential = ScanCredentialProtector.ProtectForCurrentUser(credential);
            SavePayload(payload);
        }

        public void SaveOverride(string rootPath, ScanCredential credential)
        {
            var normalizedRoot = NormalizeRoot(rootPath);
            if (string.IsNullOrWhiteSpace(normalizedRoot))
            {
                return;
            }

            var payload = LoadPayload();
            payload.RootOverrides.RemoveAll(item => string.Equals(NormalizeRoot(item.RootPath), normalizedRoot, StringComparison.OrdinalIgnoreCase));
            var protectedCredential = ScanCredentialProtector.ProtectForCurrentUser(credential);
            if (protectedCredential != null)
            {
                payload.RootOverrides.Add(new CredentialOverridePayload
                {
                    RootPath = normalizedRoot,
                    Credential = protectedCredential
                });
            }

            SavePayload(payload);
        }

        private CredentialStorePayload LoadPayload()
        {
            try
            {
                if (!File.Exists(_storePath))
                {
                    return new CredentialStorePayload();
                }

                var payload = JsonConvert.DeserializeObject<CredentialStorePayload>(File.ReadAllText(_storePath));
                return payload ?? new CredentialStorePayload();
            }
            catch
            {
                return new CredentialStorePayload();
            }
        }

        private void SavePayload(CredentialStorePayload payload)
        {
            var directory = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var normalized = payload ?? new CredentialStorePayload();
            normalized.RootOverrides = (normalized.RootOverrides ?? new List<CredentialOverridePayload>())
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.RootPath) && item.Credential != null)
                .GroupBy(item => NormalizeRoot(item.RootPath), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToList();

            File.WriteAllText(_storePath, JsonConvert.SerializeObject(normalized, Formatting.Indented));
        }

        private static string NormalizeRoot(string rootPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                return string.Empty;
            }

            return rootPath.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private sealed class CredentialStorePayload
        {
            public ScanCredential GlobalCredential { get; set; }
            public List<CredentialOverridePayload> RootOverrides { get; set; } = new List<CredentialOverridePayload>();
        }

        private sealed class CredentialOverridePayload
        {
            public string RootPath { get; set; }
            public ScanCredential Credential { get; set; }
        }
    }

    public sealed class ScanCredentialSettings
    {
        public ScanCredential GlobalCredential { get; set; }
        public Dictionary<string, ScanCredential> RootOverrides { get; set; } = new Dictionary<string, ScanCredential>(StringComparer.OrdinalIgnoreCase);
    }
}
