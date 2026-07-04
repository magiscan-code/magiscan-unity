using Newtonsoft.Json;

namespace Magiscan
{
    /// <summary>Response of <c>POST /link/poll</c>.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class LinkPollResponse
    {
        /// <summary>Raw status: <c>pending</c>, <c>slow_down</c>, <c>approved</c>, or <c>expired</c>.</summary>
        [JsonProperty("status")] public string Status { get; set; }

        /// <summary>Suggested poll interval, in seconds (pending / slow_down).</summary>
        [JsonProperty("interval")] public int Interval { get; set; }

        /// <summary>Integration token. Present only once, when <see cref="Status"/> is <c>approved</c>.</summary>
        [JsonProperty("integrationToken")] public string IntegrationToken { get; set; }

        /// <summary><see cref="Status"/> parsed into an enum.</summary>
        public MagiscanLinkStatus ParsedStatus
        {
            get
            {
                switch (Status)
                {
                    case "pending": return MagiscanLinkStatus.Pending;
                    case "slow_down": return MagiscanLinkStatus.SlowDown;
                    case "approved": return MagiscanLinkStatus.Approved;
                    case "expired": return MagiscanLinkStatus.Expired;
                    default: return MagiscanLinkStatus.Unknown;
                }
            }
        }
    }
}
