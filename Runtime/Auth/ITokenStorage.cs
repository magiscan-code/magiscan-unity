namespace Magiscan
{
    /// <summary>Abstraction over where the integration token is persisted.</summary>
    public interface ITokenStorage
    {
        /// <summary>True when a non-empty token is stored.</summary>
        bool HasToken { get; }

        /// <summary>Returns the stored token, or <c>null</c>/empty if none.</summary>
        string Load();

        /// <summary>Persists the token.</summary>
        void Save(string token);

        /// <summary>Removes the stored token.</summary>
        void Clear();
    }
}
