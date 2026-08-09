using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Magiscan;
using Magiscan.Http;
using NUnit.Framework;

namespace Magiscan.Tests
{
    public class MagiscanClientTests
    {
        sealed class FakeHttp : IHttpClient
        {
            public HttpRequest LastRequest;
            public Func<HttpRequest, HttpResponse> Handler = _ => new HttpResponse { StatusCode = 200, Text = "{}" };

            public Task<HttpResponse> SendAsync(HttpRequest request, IProgress<float> progress, CancellationToken ct)
            {
                LastRequest = request;
                return Task.FromResult(Handler(request));
            }
        }

        sealed class FakeTokens : ITokenStorage
        {
            string _t;
            public bool HasToken => !string.IsNullOrEmpty(_t);
            public string Load() => _t;
            public void Save(string t) => _t = t;
            public void Clear() => _t = null;
        }

        static HttpResponse Ok(string body) => new HttpResponse { StatusCode = 200, Text = body };

        // Fake responses resolve synchronously, so it is safe to block.
        static T Wait<T>(Task<T> task) => task.GetAwaiter().GetResult();

        FakeHttp _http;
        FakeTokens _tokens;
        MagiscanClient _client;

        [SetUp]
        public void SetUp()
        {
            _http = new FakeHttp();
            _tokens = new FakeTokens();
            _client = new MagiscanClient(new MagiscanSettings(), _http, _tokens);
        }

        const string TasksJson = @"[
          { ""id"":""665f"", ""name"":""My scan"", ""scanType"":""360"", ""status"":""Done"",
            ""previewUrl"":""https://x/p.jpg"",
            ""models"":[
              {""formatName"":""usdz"",""url"":""https://x/a.usdz"",""fileSize"":1234567},
              {""formatName"":""glb"",""url"":""https://x/a.glb"",""fileSize"":987654}
            ] },
          { ""id"":""777a"", ""name"":""Processing"", ""scanType"":""roomplan"", ""status"":""Processing"",
            ""previewUrl"":""https://x/p2.jpg"", ""models"":[] }
        ]";

        [Test]
        public void GetTasks_ParsesTopLevelArray()
        {
            _tokens.Save("tok");
            _http.Handler = _ => Ok(TasksJson);

            var tasks = Wait(_client.GetTasksAsync());

            Assert.AreEqual(2, tasks.Count);
            Assert.AreEqual("My scan", tasks[0].Name);
            Assert.AreEqual(MagiscanScanStatus.Done, tasks[0].ParsedStatus);
            Assert.IsTrue(tasks[0].IsReady);
            Assert.AreEqual(2, tasks[0].Models.Count);
            Assert.AreEqual(MagiscanScanStatus.Processing, tasks[1].ParsedStatus);
            Assert.IsFalse(tasks[1].IsReady);
        }

        [Test]
        public void FindModel_IsCaseInsensitive()
        {
            _tokens.Save("tok");
            _http.Handler = _ => Ok(TasksJson);
            var tasks = Wait(_client.GetTasksAsync());

            var glb = tasks[0].FindModel("GLB");
            Assert.IsNotNull(glb);
            Assert.AreEqual(987654L, glb.FileSize);
            Assert.IsTrue(glb.Url.EndsWith("a.glb"));
        }

        [Test]
        public void GetTasks_SendsAuthHeaderAndPagingQuery()
        {
            _tokens.Save("tok_abc");
            _http.Handler = _ => Ok("[]");

            Wait(_client.GetTasksAsync(skip: 5, limit: 50));

            Assert.AreEqual("https://magiscan.ar-generation.com/integrations/tasks?skip=5&limit=50", _http.LastRequest.Url);
            Assert.AreEqual("tok_abc", _http.LastRequest.Headers["X-Integration-Token"]);
            Assert.IsFalse(_http.LastRequest.Headers.ContainsKey("skip"));
            Assert.IsFalse(_http.LastRequest.Headers.ContainsKey("limit"));
        }

        [Test]
        public void GetTasks_WithoutToken_Throws()
        {
            Assert.Throws<MagiscanUnauthorizedException>(() => Wait(_client.GetTasksAsync()));
        }

        [Test]
        public void StartLink_PostsIntegrationType()
        {
            _http.Handler = _ => Ok(@"{""deviceCode"":""d"",""userCode"":""u"",""integrationType"":""unity"",""expiresIn"":600,""interval"":5}");

            var start = Wait(_client.StartLinkAsync());

            Assert.AreEqual("d", start.DeviceCode);
            Assert.AreEqual("u", start.UserCode);
            Assert.AreEqual(600, start.ExpiresIn);
            Assert.AreEqual(5, start.Interval);
            Assert.AreEqual(HttpVerb.Post, _http.LastRequest.Verb);
            Assert.AreEqual("application/json", _http.LastRequest.ContentType);
            StringAssert.Contains("\"integrationType\":\"unity\"", Encoding.UTF8.GetString(_http.LastRequest.Body));
        }

        [Test]
        public void Poll_ParsesEachStatus()
        {
            _http.Handler = _ => Ok(@"{""status"":""pending"",""interval"":5}");
            Assert.AreEqual(MagiscanLinkStatus.Pending, Wait(_client.PollLinkAsync("d")).ParsedStatus);

            _http.Handler = _ => Ok(@"{""status"":""slow_down"",""interval"":5}");
            Assert.AreEqual(MagiscanLinkStatus.SlowDown, Wait(_client.PollLinkAsync("d")).ParsedStatus);

            _http.Handler = _ => Ok(@"{""status"":""approved"",""integrationToken"":""ITOK""}");
            var approved = Wait(_client.PollLinkAsync("d"));
            Assert.AreEqual(MagiscanLinkStatus.Approved, approved.ParsedStatus);
            Assert.AreEqual("ITOK", approved.IntegrationToken);

            _http.Handler = _ => Ok(@"{""status"":""expired""}");
            Assert.AreEqual(MagiscanLinkStatus.Expired, Wait(_client.PollLinkAsync("d")).ParsedStatus);
        }

        [Test]
        public void ErrorStatuses_MapToTypedExceptions()
        {
            _tokens.Save("tok");

            _http.Handler = _ => new HttpResponse { StatusCode = 401, Text = "{}" };
            Assert.Throws<MagiscanUnauthorizedException>(() => Wait(_client.GetTasksAsync()));

            _http.Handler = _ => new HttpResponse { StatusCode = 429, Text = "rate" };
            Assert.Throws<MagiscanRateLimitException>(() => Wait(_client.StartLinkAsync()));

            _http.Handler = _ => new HttpResponse { IsNetworkError = true, Error = "dns" };
            Assert.Throws<MagiscanNetworkException>(() => Wait(_client.StartLinkAsync()));
        }
    }
}
