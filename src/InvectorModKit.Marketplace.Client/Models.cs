using System;
using System.Collections.Generic;

namespace InvectorModKit.Marketplace.Client
{
    public enum ChartSort { Recent, Popular }

    public sealed class NoteCounts
    {
        public int Casual, Normal, Hard, Ultra;
    }

    /// <summary>A published chart as listed by the server (latest version).</summary>
    public sealed class ChartSummary
    {
        public string Id, Title, Artist, Author, YoutubeId;
        public double Duration, Bpm;
        public NoteCounts Notes = new NoteCounts();
        public int Version, Downloads;
        /// <summary>Unix time in milliseconds.</summary>
        public long UpdatedAt;

        public string YoutubeUrl => "https://www.youtube.com/watch?v=" + YoutubeId;
        public string ThumbnailUrl => $"https://i.ytimg.com/vi/{YoutubeId}/mqdefault.jpg";
        public DateTime Updated => DateTimeOffset.FromUnixTimeMilliseconds(UpdatedAt).UtcDateTime;
    }

    public sealed class SearchPage
    {
        public List<ChartSummary> Items = new List<ChartSummary>();
        public int Page;
        public bool More;
    }

    /// <summary>A chart and its fingerprint, as raw JSON (written as is by <see cref="InstallStore"/>).</summary>
    public sealed class ChartDownload
    {
        public ChartSummary Summary;
        public string ChartJson, FingerprintJson;
    }

    public sealed class Session
    {
        public string Token, SteamId, Name, Role;
        public bool CanModerate => Role == "admin";
    }

    public sealed class PublishRequest
    {
        public string Title, Artist, YoutubeUrl;
        public double Duration;
        /// <summary>The chart in ChartJson format; generator/source blocks are dropped by the server.</summary>
        public string ChartJson;
        /// <summary>Fingerprint.ToJson() of the audio the chart was made on.</summary>
        public string FingerprintJson;
    }

    public sealed class MarketplaceException : Exception
    {
        /// <summary>HTTP status, 0 when the server could not be reached.</summary>
        public readonly int Status;

        public MarketplaceException(int status, string message, Exception inner = null) : base(message, inner) { Status = status; }
    }
}
