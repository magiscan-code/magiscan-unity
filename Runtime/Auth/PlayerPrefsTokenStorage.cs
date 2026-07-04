using UnityEngine;

namespace Magiscan
{
    /// <summary>
    /// Runtime <see cref="ITokenStorage"/> backed by <see cref="PlayerPrefs"/>. Convenient for samples
    /// and prototypes; for production consider a platform secure store (Keychain, Keystore, DPAPI).
    /// </summary>
    public sealed class PlayerPrefsTokenStorage : ITokenStorage
    {
        const string Key = "Magiscan.IntegrationToken";

        public bool HasToken => !string.IsNullOrEmpty(PlayerPrefs.GetString(Key, string.Empty));

        public string Load() => PlayerPrefs.GetString(Key, string.Empty);

        public void Save(string token)
        {
            PlayerPrefs.SetString(Key, token ?? string.Empty);
            PlayerPrefs.Save();
        }

        public void Clear()
        {
            PlayerPrefs.DeleteKey(Key);
            PlayerPrefs.Save();
        }
    }
}
