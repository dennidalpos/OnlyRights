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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace NtfsAudit.App.Cache
{
    public class SidNameCache
    {
        private readonly ConcurrentDictionary<string, SidCacheEntry> _cache = new ConcurrentDictionary<string, SidCacheEntry>();

        public bool TryGet(string sid, out SidCacheEntry entry)
        {
            return _cache.TryGetValue(sid, out entry);
        }

        public void Set(string sid, string name, bool isGroup, bool isDisabled = false)
        {
            _cache[sid] = new SidCacheEntry { Name = name, IsGroup = isGroup, IsDisabled = isDisabled };
        }

        public void Load(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                var data = JsonConvert.DeserializeObject<Dictionary<string, SidCacheEntry>>(File.ReadAllText(path));
                if (data != null)
                {
                    foreach (var pair in data)
                    {
                        if (pair.Value == null || string.IsNullOrWhiteSpace(pair.Value.Name)) continue;
                        _cache[pair.Key] = pair.Value;
                    }
                }
            }
            catch
            {
            }
        }

        public void Save(string path)
        {
            var json = JsonConvert.SerializeObject(_cache, Formatting.Indented);
            File.WriteAllText(path, json);
        }
    }

    public class SidCacheEntry
    {
        public string Name { get; set; }
        public bool IsGroup { get; set; }
        public bool IsDisabled { get; set; }
    }
}
