using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Magiscan.Http;
using Newtonsoft.Json;

namespace Magiscan
{
    /// <summary>
    /// Strongly-typed client for the Magiscan Linked Devices API: account linking (<c>/link/*</c>)
    /// and read-only access to scan tasks (<c>/integrations/*</c>). Pure C# — usable from the
    /// Editor and at runtime.
    /// </summary>
    public sealed class MagiscanClient
    {
        const string TokenHeader = "X-Integration-Token";

        readonly MagiscanSettings _settings;
        readonly IHttpClient _http;
        readonly ITokenStorage _tokens;

        public MagiscanClient(MagiscanSettings settings, IHttpClient http, ITokenStorage tokens)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        }

        public ITokenStorage Tokens => _tokens;
        public bool IsLinked => _tokens.HasToken;

        // ---- Linking -------------------------------------------------------

        /// <summary>
        /// Starts a link request. <c>POST /link/start</c>. No auth. Sends the configured device info
        /// (<c>deviceName</c>/<c>platform</c>/<c>appVersion</c>) so the phone can show what is being
        /// connected on its approve screen. Fields are omitted when not set (backward compatible).
        /// </summary>
        public async Task<LinkStartResponse> StartLinkAsync(CancellationToken cancellationToken = default)
        {
            var body = new Dictionary<string, string> { ["integrationType"] = _settings.IntegrationType };
            if (!string.IsNullOrEmpty(_settings.DeviceName)) body["deviceName"] = _settings.DeviceName;
            if (!string.IsNullOrEmpty(_settings.Platform)) body["platform"] = _settings.Platform;
            if (!string.IsNullOrEmpty(_settings.AppVersion)) body["appVersion"] = _settings.AppVersion;

            var req = JsonRequest(HttpVerb.Post, "/link/start", body);
            var resp = await _http.SendAsync(req, null, cancellationToken);
            ThrowIfError(resp);
            return Deserialize<LinkStartResponse>(resp);
        }

        /// <summary>Polls a pending link request once. <c>POST /link/poll</c>. No auth.</summary>
        public async Task<LinkPollResponse> PollLinkAsync(string deviceCode, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(deviceCode)) throw new ArgumentNullException(nameof(deviceCode));
            var req = JsonRequest(HttpVerb.Post, "/link/poll", new { deviceCode });
            var resp = await _http.SendAsync(req, null, cancellationToken);
            ThrowIfError(resp);
            return Deserialize<LinkPollResponse>(resp);
        }

        // ---- Tasks ---------------------------------------------------------

        /// <summary>
        /// Lists scan tasks of the linked account, newest first (server-sorted by InitTime).
        /// <c>GET /integrations/tasks?skip={skip}&amp;limit={limit}</c>. Page with skip/limit.
        /// </summary>
        public async Task<IReadOnlyList<MagiscanTask>> GetTasksAsync(int skip = 0, int limit = 50, CancellationToken cancellationToken = default)
        {
            if (skip < 0) skip = 0;
            if (limit < 1) limit = 1;
            var req = AuthorizedRequest(HttpVerb.Get, $"/integrations/tasks?skip={skip}&limit={limit}");
            var resp = await _http.SendAsync(req, null, cancellationToken);
            ThrowIfError(resp);
            return Deserialize<List<MagiscanTask>>(resp) ?? new List<MagiscanTask>();
        }

        /// <summary>Fetches a single task. <c>GET /integrations/tasks/{id}</c>.</summary>
        public async Task<MagiscanTask> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(taskId)) throw new ArgumentNullException(nameof(taskId));
            var req = AuthorizedRequest(HttpVerb.Get, "/integrations/tasks/" + Uri.EscapeDataString(taskId));
            var resp = await _http.SendAsync(req, null, cancellationToken);
            ThrowIfError(resp);
            return Deserialize<MagiscanTask>(resp);
        }

        // ---- Files ---------------------------------------------------------

        /// <summary>
        /// Resolves a possibly-relative file reference (e.g. <c>previewUrl</c>) to an absolute URL.
        /// Absolute http(s) URLs are returned unchanged; relative paths are served from
        /// <c>{BaseUrl}/file/{path}</c> (matching the server's file route).
        /// </summary>
        public string ResolveFileUrl(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return value;
            return BaseUrl + "/file/" + value.TrimStart('/');
        }

        /// <summary>Downloads raw bytes from a direct URL (model or preview). No auth header is sent.</summary>
        public async Task<byte[]> DownloadAsync(string url, IProgress<float> progress = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(url)) throw new ArgumentNullException(nameof(url));
            var resp = await _http.SendAsync(new HttpRequest { Verb = HttpVerb.Get, Url = url }, progress, cancellationToken);
            ThrowIfError(resp);
            return resp.Data;
        }

        // ---- Helpers -------------------------------------------------------

        HttpRequest JsonRequest(HttpVerb verb, string path, object body)
        {
            return new HttpRequest
            {
                Verb = verb,
                Url = BaseUrl + path,
                Body = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body)),
                ContentType = "application/json",
            };
        }

        HttpRequest AuthorizedRequest(HttpVerb verb, string path)
        {
            var token = _tokens.Load();
            if (string.IsNullOrEmpty(token))
                throw new MagiscanUnauthorizedException();

            var req = new HttpRequest { Verb = verb, Url = BaseUrl + path };
            req.Headers[TokenHeader] = token;
            return req;
        }

        string BaseUrl => (_settings.BaseUrl ?? string.Empty).TrimEnd('/');

        static void ThrowIfError(HttpResponse resp)
        {
            if (resp.IsNetworkError)
                throw new MagiscanNetworkException(string.IsNullOrEmpty(resp.Error) ? "Network error." : resp.Error);
            if (resp.IsSuccess)
                return;

            switch (resp.StatusCode)
            {
                case 401: throw new MagiscanUnauthorizedException(resp.Text);
                case 429: throw new MagiscanRateLimitException(resp.Text);
                default: throw new MagiscanException($"Request failed with HTTP {resp.StatusCode}.", resp.StatusCode, resp.Text);
            }
        }

        static T Deserialize<T>(HttpResponse resp)
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(resp.Text ?? string.Empty);
            }
            catch (Exception e)
            {
                throw new MagiscanException("Failed to parse server response: " + e.Message, resp.StatusCode, resp.Text, e);
            }
        }
    }
}
