using System;
using System.Threading;
using System.Threading.Tasks;

namespace Magiscan.Editor
{
    /// <summary>
    /// Drives the QR linking flow: <c>POST /link/start</c>, render the QR, then poll
    /// <c>POST /link/poll</c> until the user approves from the mobile app. UI-agnostic — the window
    /// subscribes to <see cref="Changed"/> and reads the public state.
    /// </summary>
    public sealed class MagiscanLinkController
    {
        public enum LinkState
        {
            Idle,
            Starting,
            WaitingForApproval,
            Approved,
            Failed,
        }

        readonly MagiscanClient _client;
        CancellationTokenSource _cts;

        public MagiscanLinkController(MagiscanClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>Current state of the flow.</summary>
        public LinkState State { get; private set; } = LinkState.Idle;

        /// <summary>The code to encode into the QR (valid while <see cref="State"/> is WaitingForApproval).</summary>
        public string UserCode { get; private set; }

        /// <summary>Human-readable status for display.</summary>
        public string StatusMessage { get; private set; }

        /// <summary>True while a link attempt is running.</summary>
        public bool IsBusy => State == LinkState.Starting || State == LinkState.WaitingForApproval;

        /// <summary>Raised on the main thread whenever any public property changes.</summary>
        public event Action Changed;

        /// <summary>
        /// The in-flight link attempt started by <see cref="Begin"/>; completes when the flow reaches
        /// a terminal state. Never faults — failures surface via <see cref="State"/>. Lets tests and
        /// callers await the flow instead of relying on fire-and-forget.
        /// </summary>
        public Task Running { get; private set; } = Task.CompletedTask;

        /// <summary>Begins a new link attempt. Safe to call again — cancels any previous attempt.</summary>
        public void Begin()
        {
            Cancel();
            _cts = new CancellationTokenSource();
            Running = RunAsync(_cts.Token);
        }

        async Task RunAsync(CancellationToken ct)
        {
            UserCode = null;
            SetState(LinkState.Starting, "Requesting a link code…");

            try
            {
                LinkStartResponse start = await _client.StartLinkAsync(ct);
                UserCode = start.UserCode;
                int interval = Math.Max(1, start.Interval);
                DateTime deadline = DateTime.UtcNow.AddSeconds(start.ExpiresIn > 0 ? start.ExpiresIn : 600);
                SetState(LinkState.WaitingForApproval, "Scan the QR code with the Magiscan app, then approve.");

                while (!ct.IsCancellationRequested)
                {
                    if (DateTime.UtcNow >= deadline)
                    {
                        Fail("The link code expired. Press Connect to try again.");
                        return;
                    }

                    await Task.Delay(TimeSpan.FromSeconds(interval), ct);

                    LinkPollResponse poll;
                    try
                    {
                        poll = await _client.PollLinkAsync(start.DeviceCode, ct);
                    }
                    catch (MagiscanNetworkException)
                    {
                        StatusMessage = "Network error — retrying…";
                        Raise();
                        continue;
                    }

                    switch (poll.ParsedStatus)
                    {
                        case MagiscanLinkStatus.Approved:
                            _client.Tokens.Save(poll.IntegrationToken);
                            UserCode = null;
                            SetState(LinkState.Approved, "Connected to Magiscan.");
                            return;

                        case MagiscanLinkStatus.Expired:
                            Fail("The link code expired. Press Connect to try again.");
                            return;

                        case MagiscanLinkStatus.SlowDown:
                            interval = Math.Max(interval, poll.Interval > 0 ? poll.Interval : interval) + 5;
                            break;

                        case MagiscanLinkStatus.Pending:
                        default:
                            if (poll.Interval > 0) interval = poll.Interval;
                            break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelled by the user or window close — leave state as set by Cancel().
            }
            catch (MagiscanRateLimitException)
            {
                Fail("Too many attempts. Wait a moment and press Connect again.");
            }
            catch (Exception e)
            {
                Fail(e.Message);
            }
        }

        /// <summary>Cancels the current attempt and returns to idle.</summary>
        public void Cancel()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

            if (IsBusy)
                SetState(LinkState.Idle, null);
        }

        void Fail(string message) => SetState(LinkState.Failed, message);

        void SetState(LinkState state, string message)
        {
            State = state;
            StatusMessage = message;
            Raise();
        }

        void Raise()
        {
            try
            {
                Changed?.Invoke();
            }
            catch (Exception e)
            {
                // A broken subscriber must not abort the link flow (or fault Running).
                UnityEngine.Debug.LogException(e);
            }
        }
    }
}
