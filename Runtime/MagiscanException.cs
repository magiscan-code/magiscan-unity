using System;

namespace Magiscan
{
    /// <summary>Base error for Magiscan API failures.</summary>
    public class MagiscanException : Exception
    {
        /// <summary>HTTP status code, or 0 if the request never reached the server.</summary>
        public long StatusCode { get; }

        /// <summary>Raw response body, if any.</summary>
        public string ResponseBody { get; }

        public MagiscanException(string message, long statusCode = 0, string responseBody = null, Exception inner = null)
            : base(message, inner)
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
        }
    }

    /// <summary>HTTP 401 — the integration token is missing, invalid, or has been revoked.</summary>
    public sealed class MagiscanUnauthorizedException : MagiscanException
    {
        public MagiscanUnauthorizedException(string responseBody = null)
            : base("Integration token is invalid or has been revoked.", 401, responseBody) { }
    }

    /// <summary>HTTP 429 — rate limited.</summary>
    public sealed class MagiscanRateLimitException : MagiscanException
    {
        public MagiscanRateLimitException(string responseBody = null)
            : base("Too many requests — please slow down.", 429, responseBody) { }
    }

    /// <summary>The request never reached the server (connectivity, DNS, TLS, timeout).</summary>
    public sealed class MagiscanNetworkException : MagiscanException
    {
        public MagiscanNetworkException(string message, Exception inner = null)
            : base(message, 0, null, inner) { }
    }
}
