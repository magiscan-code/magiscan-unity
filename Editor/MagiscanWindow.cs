using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Magiscan.Editor.ModelImport;
using Magiscan.Editor.Qr;
using Magiscan.Http;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Magiscan.Editor
{
    /// <summary>The Magiscan editor window: link an account by QR and import scans into the scene.</summary>
    public sealed class MagiscanWindow : EditorWindow
    {
        enum View { Disconnected, Linking, Linked }

        MagiscanClient _client;
        MagiscanLinkController _link;
        CancellationTokenSource _windowCts;

        VisualElement _content;

        Texture2D _qrTexture;
        string _qrForCode;
        readonly Dictionary<string, Texture2D> _previewCache = new Dictionary<string, Texture2D>();

        const int PageSize = 50;

        List<MagiscanTask> _tasks = new List<MagiscanTask>();
        bool _loadingTasks;
        bool _loadingMore;
        bool _hasMore;
        string _tasksError;
        VisualElement _grid;
        VisualElement _loadMoreRow;

        [MenuItem("Window/Magiscan", false, 1100)]
        public static void ShowWindow()
        {
            var window = GetWindow<MagiscanWindow>();
            window.titleContent = new GUIContent("Magiscan");
            window.minSize = new Vector2(360f, 420f);
            window.Show();
        }

        void OnEnable()
        {
            _windowCts = new CancellationTokenSource();
            var settings = MagiscanDeviceInfo.Apply(MagiscanSettings.CreateDefault());
            _client = new MagiscanClient(settings, new UnityWebRequestHttpClient(), new EditorPrefsTokenStorage());
            _link = new MagiscanLinkController(_client);
            _link.Changed += OnLinkChanged;
        }

        void OnDisable()
        {
            if (_link != null) _link.Changed -= OnLinkChanged;
            _link?.Cancel();

            _windowCts?.Cancel();
            _windowCts?.Dispose();
            _windowCts = null;

            DestroyTexture(ref _qrTexture);
            foreach (var tex in _previewCache.Values)
                if (tex != null) DestroyImmediate(tex);
            _previewCache.Clear();
        }

        void CreateGUI()
        {
            rootVisualElement.style.flexGrow = 1f;
            _content = new VisualElement();
            _content.style.flexGrow = 1f;
            _content.style.paddingLeft = 12;
            _content.style.paddingRight = 12;
            _content.style.paddingTop = 12;
            _content.style.paddingBottom = 12;
            rootVisualElement.Add(_content);

            Rebuild();
            if (_client.IsLinked)
                RefreshTasks();
        }

        // ---- View routing ---------------------------------------------------

        View CurrentView()
        {
            if (_client.IsLinked) return View.Linked;
            if (_link.IsBusy) return View.Linking;
            return View.Disconnected;
        }

        void OnLinkChanged()
        {
            bool justApproved = _link.State == MagiscanLinkController.LinkState.Approved;
            Rebuild();
            if (justApproved)
                RefreshTasks();
        }

        void Rebuild()
        {
            if (_content == null) return;
            _content.Clear();

            switch (CurrentView())
            {
                case View.Disconnected: BuildDisconnected(); break;
                case View.Linking: BuildLinking(); break;
                case View.Linked: BuildLinked(); break;
            }
        }

        // ---- Disconnected ---------------------------------------------------

        void BuildDisconnected()
        {
            _content.Add(Header("Magiscan AI"));
            _content.Add(Paragraph("Link your Magiscan account to browse your 3D scans and import them into the scene."));

            if (_link.State == MagiscanLinkController.LinkState.Failed && !string.IsNullOrEmpty(_link.StatusMessage))
                _content.Add(ErrorBox(_link.StatusMessage));

            var connect = new Button(() => _link.Begin()) { text = "Connect" };
            connect.style.height = 28;
            connect.style.marginTop = 8;
            _content.Add(connect);
        }

        // ---- Linking --------------------------------------------------------

        void BuildLinking()
        {
            _content.Add(Header("Connect"));
            _content.Add(Paragraph(_link.StatusMessage ?? "Starting…"));

            if (!string.IsNullOrEmpty(_link.UserCode))
            {
                EnsureQrTexture(_link.UserCode);

                var qrFrame = new VisualElement();
                qrFrame.style.alignItems = Align.Center;
                qrFrame.style.marginTop = 6;
                qrFrame.style.marginBottom = 6;

                var img = new Image { image = _qrTexture, scaleMode = ScaleMode.ScaleToFit };
                if (_qrTexture != null)
                {
                    img.style.width = _qrTexture.width;
                    img.style.height = _qrTexture.height;
                }
                qrFrame.Add(img);
                _content.Add(qrFrame);

                _content.Add(Paragraph("In the Magiscan app: Linked Devices ▸ Scan QR, then approve."));

                var code = new Label(_link.UserCode);
                code.style.unityTextAlign = TextAnchor.MiddleCenter;
                code.style.unityFontStyleAndWeight = FontStyle.Bold;
                code.style.opacity = 0.6f;
                code.style.marginTop = 2;
                code.style.fontSize = 10;
                _content.Add(code);
            }

            var cancel = new Button(() => { _link.Cancel(); Rebuild(); }) { text = "Cancel" };
            cancel.style.marginTop = 10;
            _content.Add(cancel);
        }

        void EnsureQrTexture(string userCode)
        {
            if (_qrTexture != null && _qrForCode == userCode)
                return;

            DestroyTexture(ref _qrTexture);
            try
            {
                var qr = QrCode.EncodeText(userCode, QrEcc.Medium);
                _qrTexture = QrTextureBuilder.Build(qr, moduleSize: 6, quietZone: 4);
                _qrForCode = userCode;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Magiscan] Failed to render QR code: {e.Message}");
            }
        }

        // ---- Linked ---------------------------------------------------------

        void BuildLinked()
        {
            var toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.justifyContent = Justify.SpaceBetween;
            toolbar.style.marginBottom = 8;

            var title = Header("Your scans");
            title.style.marginBottom = 0;
            toolbar.Add(title);

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var refresh = new Button(RefreshTasks) { text = "Refresh" };
            var disconnect = new Button(OnDisconnect) { text = "Disconnect" };
            disconnect.style.marginLeft = 4;
            buttons.Add(refresh);
            buttons.Add(disconnect);
            toolbar.Add(buttons);
            _content.Add(toolbar);

            if (!GlbImporter.IsGltfastInstalled)
                _content.Add(ErrorBox("glTFast (com.unity.cloud.gltfast) is not installed — model import is disabled. Install it via the Package Manager."));

            if (_loadingTasks)
            {
                _content.Add(Paragraph("Loading…"));
                return;
            }

            if (!string.IsNullOrEmpty(_tasksError))
            {
                _content.Add(ErrorBox(_tasksError));
                var retry = new Button(RefreshTasks) { text = "Retry" };
                _content.Add(retry);
                return;
            }

            if (_tasks == null || _tasks.Count == 0)
            {
                _content.Add(Paragraph("No scans yet. Create one in the Magiscan app, then press Refresh."));
                return;
            }

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;

            _grid = new VisualElement();
            _grid.style.flexDirection = FlexDirection.Row;
            _grid.style.flexWrap = Wrap.Wrap;
            scroll.Add(_grid);

            foreach (var task in _tasks)
                _grid.Add(BuildTaskCard(task));

            _loadMoreRow = new VisualElement();
            _loadMoreRow.style.alignItems = Align.Center;
            _loadMoreRow.style.marginTop = 6;
            _loadMoreRow.style.marginBottom = 6;
            scroll.Add(_loadMoreRow);
            UpdateLoadMoreRow();

            _content.Add(scroll);
        }

        void UpdateLoadMoreRow()
        {
            if (_loadMoreRow == null) return;
            _loadMoreRow.Clear();
            if (!_hasMore) return;

            var more = new Button(LoadMore)
            {
                text = _loadingMore ? "Loading…" : $"Load more ({_tasks.Count} loaded)"
            };
            more.SetEnabled(!_loadingMore);
            more.style.minWidth = 160;
            _loadMoreRow.Add(more);
        }

        VisualElement BuildTaskCard(MagiscanTask task)
        {
            var card = new VisualElement();
            card.style.width = 150;
            card.style.marginRight = 8;
            card.style.marginBottom = 8;
            card.style.paddingBottom = 6;
            card.style.borderTopLeftRadius = 6;
            card.style.borderTopRightRadius = 6;
            card.style.borderBottomLeftRadius = 6;
            card.style.borderBottomRightRadius = 6;
            card.style.backgroundColor = new Color(0f, 0f, 0f, 0.15f);
            card.style.overflow = Overflow.Hidden;

            var preview = new Image { scaleMode = ScaleMode.ScaleAndCrop };
            preview.style.height = 130;
            preview.style.backgroundColor = new Color(0f, 0f, 0f, 0.25f);
            LoadPreview(task, preview);
            card.Add(preview);

            var name = new Label(string.IsNullOrEmpty(task.Name) ? "(untitled)" : task.Name);
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.marginLeft = 6;
            name.style.marginRight = 6;
            name.style.marginTop = 4;
            name.style.whiteSpace = WhiteSpace.Normal;
            card.Add(name);

            var meta = new Label($"{task.ScanType} · {task.Status}");
            meta.style.fontSize = 10;
            meta.style.opacity = 0.6f;
            meta.style.marginLeft = 6;
            meta.style.marginRight = 6;
            card.Add(meta);

            bool hasGlb = task.FindModel("glb") != null;
            var import = new Button { text = task.IsReady ? "Import" : task.Status };
            import.style.marginLeft = 6;
            import.style.marginRight = 6;
            import.style.marginTop = 4;
            import.SetEnabled(task.IsReady && hasGlb && GlbImporter.IsGltfastInstalled);
            if (task.IsReady && !hasGlb)
                import.text = "No glb";
            import.clicked += () => OnImportClicked(task, import);
            card.Add(import);

            return card;
        }

        // ---- Actions --------------------------------------------------------

        async void RefreshTasks()
        {
            if (_windowCts == null) return;
            _loadingTasks = true;
            _loadingMore = false;
            _tasksError = null;
            _tasks.Clear();
            _hasMore = true;
            Rebuild();

            try
            {
                // Server sorts newest-first (InitTime); just take the first page.
                var page = await _client.GetTasksAsync(0, PageSize, _windowCts.Token);
                _tasks = new List<MagiscanTask>(page);
                _hasMore = page.Count == PageSize;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (MagiscanUnauthorizedException)
            {
                // Token revoked on the server — drop it and return to the connect screen.
                _client.Tokens.Clear();
                _tasks.Clear();
                _loadingTasks = false;
                Rebuild();
                return;
            }
            catch (Exception e)
            {
                _tasksError = e.Message;
            }

            _loadingTasks = false;
            Rebuild();
        }

        async void LoadMore()
        {
            if (_loadingMore || !_hasMore || _windowCts == null) return;
            _loadingMore = true;
            UpdateLoadMoreRow();

            try
            {
                var page = await _client.GetTasksAsync(_tasks.Count, PageSize, _windowCts.Token);
                _hasMore = page.Count == PageSize;
                foreach (var task in page)
                {
                    _tasks.Add(task);
                    _grid?.Add(BuildTaskCard(task)); // append only — keeps scroll position
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (MagiscanUnauthorizedException)
            {
                _client.Tokens.Clear();
                _tasks.Clear();
                Rebuild();
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Magiscan] Load more failed: {e.Message}");
            }

            _loadingMore = false;
            UpdateLoadMoreRow();
        }

        async void OnImportClicked(MagiscanTask task, Button button)
        {
            var model = task.FindModel("glb");
            if (model == null)
            {
                ShowNotification(new GUIContent("This scan has no glb model."));
                return;
            }

            button.SetEnabled(false);
            string original = button.text;
            var progress = new Progress<float>(p => button.text = $"…{Mathf.RoundToInt(p * 100f)}%");

            try
            {
                await GlbImporter.ImportIntoSceneAsync(_client, task, model, progress, _windowCts.Token);
                ShowNotification(new GUIContent($"Imported “{task.Name}”."));
                button.text = "Imported";
            }
            catch (OperationCanceledException)
            {
                button.text = original;
                button.SetEnabled(true);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Magiscan] Import failed: {e}");
                ShowNotification(new GUIContent("Import failed — see Console."));
                button.text = original;
                button.SetEnabled(true);
            }
        }

        void OnDisconnect()
        {
            bool confirm = EditorUtility.DisplayDialog(
                "Disconnect Magiscan",
                "Forget the stored token on this machine? You can also revoke this device from the Magiscan app under Linked Devices.",
                "Disconnect", "Cancel");
            if (!confirm) return;

            _client.Tokens.Clear();
            _tasks.Clear();
            _tasksError = null;
            Rebuild();
        }

        void LoadPreview(MagiscanTask task, Image target)
        {
            if (task == null || string.IsNullOrEmpty(task.PreviewUrl) || string.IsNullOrEmpty(task.Id))
                return;

            if (_previewCache.TryGetValue(task.Id, out var cached) && cached != null)
            {
                target.image = cached;
                return;
            }

            _ = LoadPreviewAsync(task, target);
        }

        async Task LoadPreviewAsync(MagiscanTask task, Image target)
        {
            string url = _client.ResolveFileUrl(task.PreviewUrl);
            try
            {
                byte[] bytes = await _client.DownloadAsync(url, null, _windowCts.Token);
                if (_windowCts == null || _windowCts.IsCancellationRequested)
                    return;

                if (bytes == null || bytes.Length == 0)
                {
                    Debug.LogWarning($"[Magiscan] Preview empty for '{task.Name}' ({url}).");
                    return;
                }

                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                if (tex.LoadImage(bytes))
                {
                    _previewCache[task.Id] = tex;
                    if (target != null)
                        target.image = tex;
                }
                else
                {
                    DestroyImmediate(tex);
                    Debug.LogWarning($"[Magiscan] Preview decode failed for '{task.Name}' ({url}, {bytes.Length} bytes, header={HexHead(bytes)}).");
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception e)
            {
                Debug.LogWarning($"[Magiscan] Preview load failed for '{task.Name}' (raw='{task.PreviewUrl}', resolved='{url}'): {e.Message}");
            }
        }

        static string HexHead(byte[] b)
        {
            int n = Mathf.Min(12, b.Length);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < n; i++) sb.Append(b[i].ToString("X2"));
            return sb.ToString();
        }

        // ---- Small UI helpers ----------------------------------------------

        static Label Header(string text)
        {
            var label = new Label(text);
            label.style.fontSize = 15;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = 6;
            return label;
        }

        static Label Paragraph(string text)
        {
            var label = new Label(text);
            label.style.whiteSpace = WhiteSpace.Normal;
            label.style.marginBottom = 4;
            label.style.opacity = 0.85f;
            return label;
        }

        static VisualElement ErrorBox(string text)
        {
            var box = new VisualElement();
            box.style.backgroundColor = new Color(0.6f, 0.1f, 0.1f, 0.25f);
            box.style.paddingLeft = 8;
            box.style.paddingRight = 8;
            box.style.paddingTop = 6;
            box.style.paddingBottom = 6;
            box.style.marginTop = 4;
            box.style.marginBottom = 4;
            box.style.borderTopLeftRadius = 4;
            box.style.borderTopRightRadius = 4;
            box.style.borderBottomLeftRadius = 4;
            box.style.borderBottomRightRadius = 4;

            var label = new Label(text) { style = { whiteSpace = WhiteSpace.Normal } };
            box.Add(label);
            return box;
        }

        static void DestroyTexture(ref Texture2D tex)
        {
            if (tex != null)
            {
                DestroyImmediate(tex);
                tex = null;
            }
        }
    }
}
