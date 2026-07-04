using UnityEngine;

namespace Magiscan
{
    /// <summary>
    /// Fills a <see cref="MagiscanSettings"/> with this device's info (name, OS, plugin version) from
    /// <see cref="SystemInfo"/>, so the phone's approve screen can show what is being connected.
    /// Only fills fields that are not already set.
    /// </summary>
    public static class MagiscanDeviceInfo
    {
        public static MagiscanSettings Apply(MagiscanSettings settings)
        {
            if (settings == null) return null;
            if (string.IsNullOrEmpty(settings.DeviceName)) settings.DeviceName = SystemInfo.deviceName;
            if (string.IsNullOrEmpty(settings.Platform)) settings.Platform = SystemInfo.operatingSystem;
            if (string.IsNullOrEmpty(settings.AppVersion)) settings.AppVersion = MagiscanSettings.PluginVersion;
            return settings;
        }
    }
}
