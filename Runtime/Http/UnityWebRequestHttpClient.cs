using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace Magiscan.Http
{
    /// <summary>
    /// <see cref="IHttpClient"/> backed by <see cref="UnityWebRequest"/>. Works in the Editor and in
    /// player builds on every platform Unity supports. All work stays on the main thread.
    /// </summary>
    public sealed class UnityWebRequestHttpClient : IHttpClient
    {
        public async Task<HttpResponse> SendAsync(HttpRequest request, IProgress<float> progress, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            cancellationToken.ThrowIfCancellationRequested();

            using (var uwr = new UnityWebRequest(request.Url, Method(request.Verb)))
            {
                uwr.downloadHandler = new DownloadHandlerBuffer();

                if (request.Body != null && request.Body.Length > 0)
                {
                    uwr.uploadHandler = new UploadHandlerRaw(request.Body);
                    if (!string.IsNullOrEmpty(request.ContentType))
                        uwr.uploadHandler.contentType = request.ContentType;
                }

                if (request.Headers != null)
                {
                    foreach (var kv in request.Headers)
                        uwr.SetRequestHeader(kv.Key, kv.Value);
                }

                var op = uwr.SendWebRequest();
                while (!op.isDone)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        uwr.Abort();
                        throw new OperationCanceledException(cancellationToken);
                    }

                    progress?.Report(op.progress);
                    await Task.Yield();
                }

                progress?.Report(1f);

                var response = new HttpResponse
                {
                    StatusCode = uwr.responseCode,
                    IsNetworkError = uwr.result == UnityWebRequest.Result.ConnectionError
                                     || uwr.result == UnityWebRequest.Result.DataProcessingError,
                    Error = uwr.error,
                };

                if (uwr.downloadHandler != null)
                {
                    response.Data = uwr.downloadHandler.data;
                    response.Text = uwr.downloadHandler.text;
                }

                return response;
            }
        }

        static string Method(HttpVerb verb)
        {
            switch (verb)
            {
                case HttpVerb.Post: return UnityWebRequest.kHttpVerbPOST;
                case HttpVerb.Delete: return UnityWebRequest.kHttpVerbDELETE;
                default: return UnityWebRequest.kHttpVerbGET;
            }
        }
    }
}
