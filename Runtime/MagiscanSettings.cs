namespace Magiscan
{
    /// <summary>Connection settings for the Magiscan API.</summary>
    public sealed class MagiscanSettings
    {
        /// <summary>Default production server (standard HTTPS port — safe for corporate firewalls).</summary>
        public const string DefaultBaseUrl = "https://magiscan.ar-generation.com";

        /// <summary>
        /// Integration type sent to <c>POST /link/start</c>. Allowed values: <c>"unity"</c>,
        /// <c>"magiscan_macos"</c>.
        /// </summary>
        public const string DefaultIntegrationType = "unity";

        /// <summary>Plugin version, reported to the server as <c>appVersion</c>. Keep in sync with package.json.</summary>
        public const string PluginVersion = "0.3.1";

        public string BaseUrl { get; set; } = DefaultBaseUrl;
        public string IntegrationType { get; set; } = DefaultIntegrationType;

        /// <summary>Human-readable name of this device (optional). Shown on the phone's approve screen.</summary>
        public string DeviceName { get; set; }

        /// <summary>OS / platform string (optional). Shown on the phone's approve screen.</summary>
        public string Platform { get; set; }

        /// <summary>Plugin/app version (optional). Defaults to <see cref="PluginVersion"/>.</summary>
        public string AppVersion { get; set; } = PluginVersion;

        /// <summary>
        /// Name of the environment variable that overrides <see cref="BaseUrl"/> when set —
        /// lets QA and CI point the plugin at a staging server without code changes.
        /// </summary>
        public const string BaseUrlEnvVar = "MAGISCAN_BASE_URL";

        public static MagiscanSettings CreateDefault()
        {
            var settings = new MagiscanSettings();
            try
            {
                string overrideUrl = System.Environment.GetEnvironmentVariable(BaseUrlEnvVar);
                if (!string.IsNullOrEmpty(overrideUrl))
                    settings.BaseUrl = overrideUrl;
            }
            catch (System.Security.SecurityException)
            {
                // Restricted platforms may forbid reading environment variables — keep the default.
            }
            return settings;
        }
    }
}
