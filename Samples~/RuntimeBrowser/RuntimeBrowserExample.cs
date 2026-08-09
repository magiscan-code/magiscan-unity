using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GLTFast;
using Magiscan;
using Magiscan.Http;
using UnityEngine;

namespace Magiscan.Samples
{
    /// <summary>
    /// Minimal runtime example: link a Magiscan account during play mode, list the account's scans,
    /// and load a scan's glb into the scene with glTFast. UI is immediate-mode (OnGUI) so the sample
    /// works in any scene — just add this component to an empty GameObject and press Play.
    /// </summary>
    public class RuntimeBrowserExample : MonoBehaviour
    {
        MagiscanClient _client;
        CancellationTokenSource _cts;

        string _status = "Not linked.";
        string _userCode;
        bool _busy;
        IReadOnlyList<MagiscanTask> _tasks;
        Vector2 _scroll;

        void Start()
        {
            _cts = new CancellationTokenSource();
            var settings = MagiscanDeviceInfo.Apply(MagiscanSettings.CreateDefault());
            _client = new MagiscanClient(settings, new UnityWebRequestHttpClient(), new PlayerPrefsTokenStorage());
            if (_client.IsLinked)
            {
                _status = "Linked.";
                _ = RefreshAsync();
            }
        }

        void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }

        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 420, Screen.height - 20));
            GUILayout.Label(_status);

            if (!_client.IsLinked)
            {
                if (!string.IsNullOrEmpty(_userCode))
                {
                    // Render this code as a QR in a real app; the Magiscan mobile app scans it
                    // under Linked Devices ▸ Scan QR. The raw code is shown here for simplicity.
                    GUILayout.Label($"Link code: {_userCode}", new GUIStyle(GUI.skin.label) { fontSize = 28 });
                }

                if (!_busy && GUILayout.Button("Link account"))
                    _ = LinkAsync();
            }
            else
            {
                GUILayout.BeginHorizontal();
                if (!_busy && GUILayout.Button("Refresh scans"))
                    _ = RefreshAsync();
                if (GUILayout.Button("Unlink"))
                {
                    _client.Tokens.Clear();
                    _tasks = null;
                    _status = "Not linked.";
                }
                GUILayout.EndHorizontal();

                if (_tasks != null)
                {
                    _scroll = GUILayout.BeginScrollView(_scroll);
                    foreach (var task in _tasks)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"{task.Name} — {task.Status}", GUILayout.Width(280));
                        bool canImport = !_busy && task.IsReady && task.FindModel("glb") != null;
                        if (canImport && GUILayout.Button("Load"))
                            _ = LoadAsync(task);
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.EndScrollView();
                }
            }

            GUILayout.EndArea();
        }

        async Task LinkAsync()
        {
            _busy = true;
            try
            {
                _status = "Requesting link code…";
                var start = await _client.StartLinkAsync(_cts.Token);
                _userCode = start.UserCode;
                _status = "Approve in the Magiscan app (Linked Devices ▸ Scan QR).";

                int interval = Mathf.Max(1, start.Interval);
                var deadline = Time.realtimeSinceStartup + (start.ExpiresIn > 0 ? start.ExpiresIn : 600);
                while (Time.realtimeSinceStartup < deadline)
                {
                    await Task.Delay(TimeSpan.FromSeconds(interval), _cts.Token);
                    var poll = await _client.PollLinkAsync(start.DeviceCode, _cts.Token);
                    if (poll.ParsedStatus == MagiscanLinkStatus.Approved)
                    {
                        _client.Tokens.Save(poll.IntegrationToken);
                        _userCode = null;
                        _status = "Linked.";
                        await RefreshAsync();
                        return;
                    }
                    if (poll.ParsedStatus == MagiscanLinkStatus.Expired)
                        break;
                    if (poll.Interval > 0) interval = poll.Interval;
                }
                _userCode = null;
                _status = "Link code expired — try again.";
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                _status = $"Link failed: {e.Message}";
                _userCode = null;
            }
            finally { _busy = false; }
        }

        async Task RefreshAsync()
        {
            _busy = true;
            try
            {
                _status = "Loading scans…";
                _tasks = await _client.GetTasksAsync(0, 50, _cts.Token);
                _status = $"Linked · {_tasks.Count} scans.";
            }
            catch (OperationCanceledException) { }
            catch (MagiscanUnauthorizedException)
            {
                _client.Tokens.Clear();
                _tasks = null;
                _status = "The device was revoked — link again.";
            }
            catch (Exception e)
            {
                _status = $"Loading failed: {e.Message}";
            }
            finally { _busy = false; }
        }

        async Task LoadAsync(MagiscanTask task)
        {
            _busy = true;
            try
            {
                var glb = task.FindModel("glb");
                _status = $"Downloading “{task.Name}”…";
                byte[] bytes = await _client.DownloadAsync(glb.Url, null, _cts.Token);

                var gltf = new GltfImport();
                if (!await gltf.LoadGltfBinary(bytes, cancellationToken: _cts.Token))
                    throw new MagiscanException("glTFast could not parse the model.");

                var root = new GameObject(string.IsNullOrEmpty(task.Name) ? "Magiscan scan" : task.Name);
                if (!await gltf.InstantiateMainSceneAsync(root.transform, _cts.Token))
                    throw new MagiscanException("glTFast could not instantiate the model.");

                _status = $"Loaded “{task.Name}”.";
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                _status = $"Load failed: {e.Message}";
            }
            finally { _busy = false; }
        }
    }
}
