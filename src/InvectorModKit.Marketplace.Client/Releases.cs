using System;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.Marketplace.Client
{
    /// <summary>A published version of the mod: its number and where its installer is.</summary>
    public sealed class Release
    {
        public string Version;
        public string SetupUrl;
    }

    /// <summary>The mod's versions, published on GitHub Releases (theovil1/invectormodkit).</summary>
    public static class Releases
    {
        public const string LatestUrl = "https://api.github.com/repos/theovil1/invectormodkit/releases/latest";
        public const string SetupAsset = "InvectorModKitSetup.exe";

        /// <summary>The latest release from GitHub's answer; null when it is an error, unreadable, or has no installer.</summary>
        public static Release Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            JObject o;
            try { o = JObject.Parse(json); }
            catch (JsonException) { return null; }
            string tag = (string)o["tag_name"];
            string url = (o["assets"] as JArray)?.OfType<JObject>()
                .FirstOrDefault(a => string.Equals((string)a["name"], SetupAsset, StringComparison.OrdinalIgnoreCase))?["browser_download_url"]?.ToString();
            if (Numbers(tag) == null || string.IsNullOrEmpty(url)) return null;
            return new Release { Version = tag.TrimStart('v', 'V'), SetupUrl = url };
        }

        /// <summary>Is <paramref name="candidate"/> ("v1.2.3" or "1.2.3") later than <paramref name="current"/>? False if either is unreadable.</summary>
        public static bool IsNewer(string candidate, string current)
        {
            int[] a = Numbers(candidate), b = Numbers(current);
            if (a == null || b == null) return false;
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                int x = i < a.Length ? a[i] : 0, y = i < b.Length ? b[i] : 0;
                if (x != y) return x > y;
            }
            return false;
        }

        private static int[] Numbers(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) return null;
            var parts = version.Trim().TrimStart('v', 'V').Split('.');
            var numbers = new int[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                if (!int.TryParse(parts[i], out numbers[i]) || numbers[i] < 0) return null;
            return numbers;
        }
    }
}
