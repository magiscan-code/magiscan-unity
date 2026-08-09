using System;
using System.Threading;
using System.Threading.Tasks;
using Magiscan;
using Magiscan.Editor;
using Magiscan.Http;
using NUnit.Framework;

namespace Magiscan.Tests
{
    public class MagiscanLinkControllerTests
    {
        sealed class FakeHttp : IHttpClient
        {
            public Func<HttpRequest, HttpResponse> Handler = _ => new HttpResponse { StatusCode = 200, Text = "{}" };

            public Task<HttpResponse> SendAsync(HttpRequest request, IProgress<float> progress, CancellationToken ct)
                => Task.FromResult(Handler(request));
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

        FakeHttp _http;
        FakeTokens _tokens;
        MagiscanLinkController _link;
        SynchronizationContext _previousContext;

        [SetUp]
        public void SetUp()
        {
            // The editor's SynchronizationContext would deadlock blocking Wait() calls below
            // (continuations post back to the main thread we are blocking). The controller has no
            // Unity-API dependency, so run its continuations on the thread pool for these tests.
            _previousContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(null);

            _http = new FakeHttp();
            _tokens = new FakeTokens();
            _link = new MagiscanLinkController(new MagiscanClient(new MagiscanSettings(), _http, _tokens));
        }

        [TearDown]
        public void TearDown()
        {
            _link.Cancel();
            SynchronizationContext.SetSynchronizationContext(_previousContext);
        }

        [Test]
        public void Begin_RateLimited_EndsFailed()
        {
            _http.Handler = _ => new HttpResponse { StatusCode = 429, Text = "rate" };

            _link.Begin();
            Assert.IsTrue(_link.Running.Wait(TimeSpan.FromSeconds(5)), "flow should finish");

            Assert.AreEqual(MagiscanLinkController.LinkState.Failed, _link.State);
            Assert.IsFalse(_tokens.HasToken);
        }

        [Test]
        public void Begin_Approved_SavesTokenAndEndsApproved()
        {
            _http.Handler = req => req.Url.EndsWith("/link/start")
                ? Ok(@"{""deviceCode"":""d"",""userCode"":""u"",""expiresIn"":600,""interval"":1}")
                : Ok(@"{""status"":""approved"",""integrationToken"":""ITOK""}");

            _link.Begin();
            Assert.IsTrue(_link.Running.Wait(TimeSpan.FromSeconds(10)), "flow should finish");

            Assert.AreEqual(MagiscanLinkController.LinkState.Approved, _link.State);
            Assert.AreEqual("ITOK", _tokens.Load());
            Assert.IsNull(_link.UserCode);
        }

        [Test]
        public void Cancel_DuringPolling_ReturnsToIdle()
        {
            _http.Handler = req => req.Url.EndsWith("/link/start")
                ? Ok(@"{""deviceCode"":""d"",""userCode"":""u"",""expiresIn"":600,""interval"":5}")
                : Ok(@"{""status"":""pending"",""interval"":5}");

            _link.Begin();
            // The fake transport completes synchronously, so Begin returns parked on the poll delay.
            Assert.AreEqual(MagiscanLinkController.LinkState.WaitingForApproval, _link.State);
            Assert.AreEqual("u", _link.UserCode);

            _link.Cancel();
            Assert.IsTrue(_link.Running.Wait(TimeSpan.FromSeconds(5)), "flow should unwind after cancel");
            Assert.AreEqual(MagiscanLinkController.LinkState.Idle, _link.State);
        }

        [Test]
        public void Begin_ThrowingSubscriber_DoesNotThrowAndStillFails()
        {
            UnityEngine.Application.SetStackTraceLogType(UnityEngine.LogType.Exception, UnityEngine.StackTraceLogType.None);
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            try
            {
                _http.Handler = _ => new HttpResponse { StatusCode = 500, Text = "boom" };
                _link.Changed += () => throw new InvalidOperationException("subscriber bug");

                Assert.DoesNotThrow(() => _link.Begin());
                Assert.IsTrue(_link.Running.Wait(TimeSpan.FromSeconds(5)), "flow should finish despite the subscriber");
                Assert.AreEqual(MagiscanLinkController.LinkState.Failed, _link.State);
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
            }
        }
    }
}
