namespace Magiscan
{
    /// <summary>Status returned by <c>POST /link/poll</c>.</summary>
    public enum MagiscanLinkStatus
    {
        /// <summary>Status string was missing or not recognized.</summary>
        Unknown = 0,
        /// <summary>Waiting for the user to approve from the mobile app.</summary>
        Pending,
        /// <summary>Polling too frequently — back off using the suggested interval.</summary>
        SlowDown,
        /// <summary>Approved; the integration token is included once.</summary>
        Approved,
        /// <summary>The request expired or was not found.</summary>
        Expired,
    }
}
