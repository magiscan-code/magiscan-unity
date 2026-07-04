namespace Magiscan
{
    /// <summary>
    /// Processing state of a scan task, as reported by <c>GET /integrations/tasks</c>.
    /// </summary>
    public enum MagiscanScanStatus
    {
        /// <summary>Status string was missing or not recognized.</summary>
        Unknown = 0,
        Prepare,
        InQueue,
        Processing,
        Done,
        Error,
    }
}
