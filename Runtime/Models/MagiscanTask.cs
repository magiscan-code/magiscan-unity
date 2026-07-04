using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Magiscan
{
    /// <summary>A scan task owned by the linked account (<c>GET /integrations/tasks</c>).</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class MagiscanTask
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("name")] public string Name { get; set; }

        /// <summary>e.g. <c>"360"</c>, <c>"roomplan"</c>, <c>"pointcloud"</c>, <c>"imageTo3d"</c>.</summary>
        [JsonProperty("scanType")] public string ScanType { get; set; }

        /// <summary>Raw status string. Use <see cref="ParsedStatus"/> for the enum.</summary>
        [JsonProperty("status")] public string Status { get; set; }

        [JsonProperty("previewUrl")] public string PreviewUrl { get; set; }

        /// <summary>Available model files. Empty until <see cref="Status"/> is <c>Done</c>.</summary>
        [JsonProperty("models")] public List<MagiscanModel> Models { get; set; } = new List<MagiscanModel>();

        /// <summary><see cref="Status"/> parsed into an enum (Unknown if unrecognized).</summary>
        public MagiscanScanStatus ParsedStatus =>
            Enum.TryParse(Status, ignoreCase: true, out MagiscanScanStatus s) ? s : MagiscanScanStatus.Unknown;

        /// <summary>True when processing has finished and models are available.</summary>
        public bool IsReady => ParsedStatus == MagiscanScanStatus.Done;

        /// <summary>Returns the model in the requested format, or <c>null</c>.</summary>
        public MagiscanModel FindModel(string formatName)
        {
            if (Models == null) return null;
            foreach (var m in Models)
            {
                if (m != null && string.Equals(m.FormatName, formatName, StringComparison.OrdinalIgnoreCase))
                    return m;
            }
            return null;
        }
    }
}
