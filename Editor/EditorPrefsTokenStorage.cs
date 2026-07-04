using UnityEditor;

namespace Magiscan.Editor
{
    /// <summary>
    /// Stores the integration token in <see cref="EditorPrefs"/> (per-user, per-machine).
    /// Good enough for an Editor tool; for stronger protection move it to the OS keychain.
    /// </summary>
    public sealed class EditorPrefsTokenStorage : ITokenStorage
    {
        const string Key = "Magiscan.IntegrationToken";

        public bool HasToken => !string.IsNullOrEmpty(EditorPrefs.GetString(Key, string.Empty));

        public string Load() => EditorPrefs.GetString(Key, string.Empty);

        public void Save(string token) => EditorPrefs.SetString(Key, token ?? string.Empty);

        public void Clear() => EditorPrefs.DeleteKey(Key);
    }
}
