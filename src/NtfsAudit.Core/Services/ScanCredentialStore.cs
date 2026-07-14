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
using System.IO;
using Newtonsoft.Json;
using NtfsAudit.App.Cache;
using NtfsAudit.App.Models;

namespace NtfsAudit.App.Services
{
    public sealed class ScanCredentialStore
    {
        private readonly string _storePath;

        public ScanCredentialStore()
            : this(Path.Combine(RuntimePaths.GetCommonDataRoot(), "scan-credentials.json"))
        {
        }

        internal ScanCredentialStore(string storePath)
        {
            _storePath = storePath;
        }

        public ScanCredentialSettings Load()
        {
            try
            {
                var payload = LoadPayloadFromPath(_storePath);
                if (payload == null)
                {
                    return new ScanCredentialSettings();
                }

                return new ScanCredentialSettings
                {
                    GlobalCredential = payload.GlobalCredential == null
                        ? null
                        : ScanCredentialProtector.ResolveForRuntime(payload.GlobalCredential)
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
            payload.GlobalCredential = ScanCredentialProtector.ProtectForLocalMachine(credential);
            SavePayload(payload);
        }

        private CredentialStorePayload LoadPayload()
        {
            return LoadPayloadFromPath(_storePath) ?? new CredentialStorePayload();
        }

        private void SavePayload(CredentialStorePayload payload)
        {
            var directory = Path.GetDirectoryName(_storePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var normalized = payload ?? new CredentialStorePayload();
            File.WriteAllText(_storePath, JsonConvert.SerializeObject(normalized, Formatting.Indented));
        }

        private static CredentialStorePayload LoadPayloadFromPath(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                {
                    return null;
                }

                return JsonConvert.DeserializeObject<CredentialStorePayload>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }

        private sealed class CredentialStorePayload
        {
            public ScanCredential GlobalCredential { get; set; }
        }
    }

    public sealed class ScanCredentialSettings
    {
        public ScanCredential GlobalCredential { get; set; }
    }
}
