using System;
using System.Collections.Generic;

namespace InvectorModKit.Marketplace.Client
{
    /// <summary>Values computed from files, kept while each file's write time (its stamp) stays the same.</summary>
    public sealed class StampedCache
    {
        private readonly Dictionary<string, (DateTime stamp, string value)> _known = new Dictionary<string, (DateTime, string)>();

        /// <summary>The value for <paramref name="key"/>, computed again only when <paramref name="stamp"/> changed.</summary>
        public string Get(string key, DateTime stamp, Func<string> compute)
        {
            if (_known.TryGetValue(key, out var known) && known.stamp == stamp) return known.value;
            string value = compute();
            _known[key] = (stamp, value);
            return value;
        }
    }
}
