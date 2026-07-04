using Newtonsoft.Json;

namespace Magiscan
{
    /// <summary>Response of <c>POST /link/start</c>.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class LinkStartResponse
    {
        /// <summary>Secret used by the plugin to poll. Never display this to the user.</summary>
        [JsonProperty("deviceCode")] public string DeviceCode { get; set; }

        /// <summary>Code encoded into the QR and approved from the mobile app.</summary>
        [JsonProperty("userCode")] public string UserCode { get; set; }

        [JsonProperty("integrationType")] public string IntegrationType { get; set; }

        /// <summary>Lifetime of the request, in seconds.</summary>
        [JsonProperty("expiresIn")] public int ExpiresIn { get; set; }

        /// <summary>Recommended poll interval, in seconds.</summary>
        [JsonProperty("interval")] public int Interval { get; set; }
    }
}
