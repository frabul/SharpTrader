using System;
using System.Collections.Generic;
using System.Text;

namespace SharpTrader
{
    class MemoryCache
    {
        class CacheEntry
        {
            public object Object;
            public DateTime Expiry;
        }
        Dictionary<string, CacheEntry> Entries = new Dictionary<string, CacheEntry>();
        public bool TryGetValue(string key, out object result)
        {
            lock (Entries)
            {
                if (Entries.ContainsKey(key))
                {
                    if (DateTime.UtcNow < Entries[key].Expiry)
                    {
                        result = Entries[key].Object;
                        return true;
                    }
                    else
                    {
                        Entries[key].Object = null;
                    }
                }
                result = null;
                return false;
            }
        }

        public void Set(string key, object obj, DateTime expiry)
        {
            lock (Entries)
                Entries[key] = new CacheEntry { Object = obj, Expiry = expiry };
        }
    }
}
