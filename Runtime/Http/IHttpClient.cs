using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Magiscan.Http
{
    public enum HttpVerb
    {
        Get,
        Post,
        Delete,
    }

    /// <summary>A minimal, transport-agnostic HTTP request description (mockable in tests).</summary>
    public sealed class HttpRequest
    {
        public HttpVerb Verb = HttpVerb.Get;
        public string Url;
        public Dictionary<string, string> Headers = new Dictionary<string, string>();

        /// <summary>Raw request body (e.g. UTF-8 JSON), or <c>null</c>.</summary>
        public byte[] Body;

        /// <summary>Content type for <see cref="Body"/>, e.g. <c>"application/json"</c>.</summary>
        public string ContentType;

        /// <summary>Request timeout in seconds; 0 = no timeout (large model downloads rely on
        /// progress + cancellation instead).</summary>
        public int TimeoutSeconds;
    }

    /// <summary>Result of an <see cref="HttpRequest"/>.</summary>
    public sealed class HttpResponse
    {
        public long StatusCode;
        public byte[] Data;
        public string Text;

        /// <summary>Transport failure with no HTTP status (connection/DNS/TLS error).</summary>
        public bool IsNetworkError;

        /// <summary>Human-readable transport error, if any.</summary>
        public string Error;

        public bool IsSuccess => !IsNetworkError && StatusCode >= 200 && StatusCode < 300;
    }

    /// <summary>Transport used by <see cref="MagiscanClient"/>.</summary>
    public interface IHttpClient
    {
        Task<HttpResponse> SendAsync(HttpRequest request, IProgress<float> progress, CancellationToken cancellationToken);
    }
}
