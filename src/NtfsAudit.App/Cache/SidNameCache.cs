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

        public void Set(string sid, string name, bool isGroup)
        {
            _cache[sid] = new SidCacheEntry { Name = name, IsGroup = isGroup };
        }

        public void Load(string path)
        {
            if (!File.Exists(path)) return;
            var json = File.ReadAllText(path);
            try
            {
                var data = JsonConvert.DeserializeObject<Dictionary<string, SidCacheEntry>>(json);
                if (data != null)
                {
                    foreach (var pair in data)
                    {
                        if (pair.Value == null || string.IsNullOrWhiteSpace(pair.Value.Name)) continue;
                        _cache[pair.Key] = pair.Value;
                    }
                    return;
                }
            }
            catch
            {
            }

            try
            {
                var legacyData = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                if (legacyData == null) return;
                foreach (var pair in legacyData)
                {
                    if (string.IsNullOrWhiteSpace(pair.Value)) continue;
                    _cache[pair.Key] = new SidCacheEntry { Name = pair.Value, IsGroup = false };
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
    }
}
