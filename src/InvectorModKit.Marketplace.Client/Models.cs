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
        /// <summary>Hidden by reports or an admin (only the author still sees it).</summary>
        public bool Hidden;
        /// <summary>Unix time in milliseconds.</summary>
        public long UpdatedAt;
        /// <summary>Where the chart's excerpt starts (seconds); null from a server that does not send it.</summary>
        public double? Preview;
        /// <summary>The author's own charts only (/me/charts): players with a score, reports; null from an older server.</summary>
        public int? Players, Reports;

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

    public sealed class BrowserLogin
    {
        public string Code, Url;
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
        /// <summary>The player accepted the publishing rules (needed once; the server answers 428 "rules" otherwise).</summary>
        public bool AcceptRules;
    }

    public sealed class ScoreSubmission
    {
        public string PlayId;
        public int Score, Percent, Misses, Streak, Perfect;
        /// <summary>Notes in the chart that was played, for this difficulty.</summary>
        public int Notes;
    }

    public sealed class ScoreResult
    {
        public int Rank, BestScore, BestPercent, Total;
    }

    public sealed class BoardEntry
    {
        public int Rank, Score, Percent;
        public string SteamId, Name;
    }

    public sealed class BoardList
    {
        public int Version, Total;
        public List<BoardEntry> Entries = new List<BoardEntry>();
        /// <summary>The signed-in player's entry, wherever it ranks; null when not signed in or not on the board.</summary>
        public BoardEntry Me;
    }

    /// <summary>One report of a chart (moderation).</summary>
    public sealed class ReportDetail
    {
        public string Reason, Name, SteamId;
        /// <summary>Unix time in milliseconds.</summary>
        public long At;
    }

    /// <summary>A reported chart with its reports, newest first (moderation).</summary>
    public sealed class ReportedChart
    {
        public string Id, Title, Artist, Author, AuthorSteamId;
        public bool Hidden;
        public int Version, Reports;
        /// <summary>The newest report, Unix time in milliseconds.</summary>
        public long Last;
        public List<ReportDetail> Details = new List<ReportDetail>();
    }

    /// <summary>A chart as the moderation list gives it (hidden ones too).</summary>
    public sealed class AdminChart
    {
        public string Id, Title, Artist, Author, AuthorSteamId;
        public bool Hidden;
        public int Version, Downloads, Reports;
        public long UpdatedAt;
    }

    /// <summary>A player as moderation sees them.</summary>
    public sealed class AdminUser
    {
        public string SteamId, Name, Role;
        public long CreatedAt;
        /// <summary>When they were banned (Unix ms), null when not banned or before ban dates were kept.</summary>
        public long? BannedAt;
        public int Charts, Scores;
    }

    public sealed class MarketplaceException : Exception
    {
        /// <summary>HTTP status, 0 when the server could not be reached.</summary>
        public readonly int Status;

        public MarketplaceException(int status, string message, Exception inner = null) : base(message, inner) { Status = status; }
    }
}
