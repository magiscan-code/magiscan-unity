using Newtonsoft.Json;

namespace Magiscan
{
    /// <summary>A downloadable model file attached to a <see cref="MagiscanTask"/>.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class MagiscanModel
    {
        /// <summary>File format, e.g. <c>"glb"</c>, <c>"usdz"</c>, <c>"pc"</c>.</summary>
        [JsonProperty("formatName")] public string FormatName { get; set; }

        /// <summary>Direct download URL (CloudFront).</summary>
        [JsonProperty("url")] public string Url { get; set; }

        /// <summary>File size in bytes.</summary>
        [JsonProperty("fileSize")] public long FileSize { get; set; }
    }
}
