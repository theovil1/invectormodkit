using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace InvectorModKit.Marketplace.Client
{
    /// <summary>What a chart plays, apart from where it comes from: tells whether the author changed it since publishing.</summary>
    public static class ChartContent
    {
        /// <summary>
        /// SHA-256 (lowercase hex) of the chart without its <c>generator</c> and <c>source</c> blocks (the server drops the
        /// same two; pinning a published chart rewrites <c>source</c>). Null when the text is not a JSON object.
        /// </summary>
        public static string Hash(string chartJson)
        {
            if (string.IsNullOrEmpty(chartJson)) return null;
            JObject chart;
            try { chart = JToken.Parse(chartJson) as JObject; }
            catch (JsonException) { return null; }
            if (chart == null) return null;
            chart.Remove("generator");
            chart.Remove("source");
            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(chart.ToString(Formatting.None)));
                var hex = new StringBuilder(bytes.Length * 2);
                foreach (byte b in bytes) hex.Append(b.ToString("x2"));
                return hex.ToString();
            }
        }
    }
}
