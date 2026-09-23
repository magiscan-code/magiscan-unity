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
        const float GridGap = 8f;
        const float MinCardWidth = 150f;

        List<MagiscanTask> _tasks = new List<MagiscanTask>();
        bool _loadingTasks;
        bool _loadingMore;
        bool _hasMore;
        string _tasksError;
        VisualElement _grid;
        VisualElement _loadMoreRow;
        float _lastGridWidth;
        int _lastGridCount;

        [MenuItem("Window/Magiscan", false, 1100)]
        public static void ShowWindow()
        {
            var window = GetWindow<MagiscanWindow>();
            var icon = EditorGUIUtility.IconContent(EditorGUIUtility.isProSkin ? "d_PreMatCube" : "PreMatCube").image;
            window.titleContent = new GUIContent("Magiscan", icon);
            window.minSize = new Vector2(360f, 420f);
            window.Show();
        }

        /// <summary>EditorPrefs key holding a per-user base-URL override (staging/QA); empty = default.</summary>
        const string BaseUrlOverrideKey = "Magiscan.BaseUrlOverride";

        /// <summary>Editor override wins over the env var, which wins over the built-in default.</summary>
        static MagiscanSettings CreateSettings()
        {
            var settings = MagiscanDeviceInfo.Apply(MagiscanSettings.CreateDefault());
            string editorOverride = EditorPrefs.GetString(BaseUrlOverrideKey, string.Empty);
            if (!string.IsNullOrEmpty(editorOverride))
                settings.BaseUrl = editorOverride;
            return settings;
        }

        void OnEnable()
        {
            _windowCts = new CancellationTokenSource();
            _client = new MagiscanClient(CreateSettings(), new UnityWebRequestHttpClient(), new EditorPrefsTokenStorage());
            _link = new MagiscanLinkController(_client);
            _link.Changed += OnLinkChanged;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        }

        void OnDisable()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
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

        /// <summary>
        /// Script compilation reloads the domain and silently kills every in-flight task. Cancel
        /// cleanly beforehand so a linking/loading window comes back in a consistent state
        /// (CreateGUI runs again after the reload and rebuilds from the stored token).
        /// </summary>
        void OnBeforeAssemblyReload()
        {
            _link?.Cancel();
            _windowCts?.Cancel();
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

            _content.Add(BuildAdvancedFoldout());
        }

        /// <summary>Server URL override for staging/QA. Hidden behind a collapsed foldout.</summary>
        VisualElement BuildAdvancedFoldout()
        {
            var advanced = new Foldout { text = "Advanced", value = false };
            advanced.style.marginTop = 14;
            advanced.style.opacity = 0.8f;

            var url = new TextField("Server URL") { value = EditorPrefs.GetString(BaseUrlOverrideKey, string.Empty) };
            url.tooltip = $"Leave empty for the default ({MagiscanSettings.DefaultBaseUrl}). " +
                          $"The {MagiscanSettings.BaseUrlEnvVar} environment variable also overrides the default.";
            advanced.Add(url);

            var apply = new Button(() =>
            {
                string value = (url.value ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(value))
                    EditorPrefs.DeleteKey(BaseUrlOverrideKey);
                else
                    EditorPrefs.SetString(BaseUrlOverrideKey, value);
                RecreateClient();
                Rebuild();
                ShowNotification(new GUIContent(string.IsNullOrEmpty(value) ? "Using default server" : "Server URL applied"));
            }) { text = "Apply" };
            apply.style.marginTop = 2;
            apply.style.alignSelf = Align.FlexStart;
            advanced.Add(apply);
            return advanced;
        }

        void RecreateClient()
        {
            _link.Changed -= OnLinkChanged;
            _link.Cancel();
            _client = new MagiscanClient(CreateSettings(), new UnityWebRequestHttpClient(), new EditorPrefsTokenStorage());
            _link = new MagiscanLinkController(_client);
            _link.Changed += OnLinkChanged;
        }

        // ---- Linking --------------------------------------------------------

        void BuildLinking()
        {
            // A centered "pairing screen" column, vertically balanced in the window.
            var column = new VisualElement();
            column.style.flexGrow = 1f;
            column.style.justifyContent = Justify.Center;
            column.style.alignItems = Align.Center;

            var title = new Label("Connect to Magiscan");
            title.style.fontSize = 15;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 4;
            column.Add(title);

            if (string.IsNullOrEmpty(_link.UserCode))
            {
                var starting = new Label("Requesting a link code");
                starting.style.opacity = 0.6f;
                starting.style.marginTop = 8;
                AnimateEllipsis(starting, "Requesting a link code");
                column.Add(starting);
            }
            else
            {
                var subtitle = new Label("Scan this code with the Magiscan app");
                subtitle.style.opacity = 0.6f;
                subtitle.style.marginBottom = 14;
                column.Add(subtitle);

                EnsureQrTexture(_link.UserCode);

                // White rounded card around the QR (the texture carries its own quiet zone).
                var qrCard = new VisualElement();
                qrCard.style.backgroundColor = Color.white;
                qrCard.style.borderTopLeftRadius = 12;
                qrCard.style.borderTopRightRadius = 12;
                qrCard.style.borderBottomLeftRadius = 12;
                qrCard.style.borderBottomRightRadius = 12;
                qrCard.style.overflow = Overflow.Hidden;

                var img = new Image { image = _qrTexture, scaleMode = ScaleMode.ScaleToFit };
                if (_qrTexture != null)
                {
                    img.style.width = _qrTexture.width;
                    img.style.height = _qrTexture.height;
                }
                qrCard.Add(img);
                column.Add(qrCard);

                var hint = new Label("Magiscan app ▸ Linked Devices ▸ Scan QR");
                hint.style.fontSize = 10;
                hint.style.opacity = 0.45f;
                hint.style.marginTop = 10;
                column.Add(hint);

                var code = new Label(_link.UserCode)
                {
                    tooltip = "Click to copy",
                };
                code.style.fontSize = 10;
                code.style.opacity = 0.5f;
                code.style.marginTop = 2;
                code.RegisterCallback<ClickEvent>(_ =>
                {
                    EditorGUIUtility.systemCopyBuffer = _link.UserCode;
                    ShowNotification(new GUIContent("Code copied"));
                });
                column.Add(code);

                // "Waiting for approval…" with a pulsing amber dot. The label has a fixed min width
                // so the animated dots don't make the centered row jiggle.
                var waitingRow = new VisualElement();
                waitingRow.style.flexDirection = FlexDirection.Row;
                waitingRow.style.alignItems = Align.Center;
                waitingRow.style.marginTop = 14;

                var dot = new VisualElement();
                dot.style.width = 8;
                dot.style.height = 8;
                dot.style.borderTopLeftRadius = 4;
                dot.style.borderTopRightRadius = 4;
                dot.style.borderBottomLeftRadius = 4;
                dot.style.borderBottomRightRadius = 4;
                dot.style.backgroundColor = new Color(0.95f, 0.72f, 0.22f);
                dot.style.marginRight = 6;
                AnimatePulse(dot);
                waitingRow.Add(dot);

                var waiting = new Label("Waiting for approval");
                waiting.style.opacity = 0.7f;
                waiting.style.minWidth = 150;
                waiting.style.unityTextAlign = TextAnchor.MiddleLeft;
                AnimateEllipsis(waiting, "Waiting for approval");
                waitingRow.Add(waiting);
                column.Add(waitingRow);

                // Surface transient poll problems (e.g. network retry) without leaving the screen.
                if (!string.IsNullOrEmpty(_link.StatusMessage) && _link.StatusMessage.StartsWith("Network error"))
                {
                    var warn = new Label(_link.StatusMessage);
                    warn.style.fontSize = 10;
                    warn.style.color = new Color(0.95f, 0.72f, 0.22f);
                    warn.style.marginTop = 4;
                    column.Add(warn);
                }
            }

            var cancel = new Button(() => { _link.Cancel(); Rebuild(); }) { text = "Cancel" };
            cancel.style.marginTop = 16;
            cancel.style.minWidth = 120;
            column.Add(cancel);

            _content.Add(column);
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
            toolbar.style.justifyContent = Justify.FlexEnd;
            toolbar.style.marginBottom = 8;

            var buttons = new VisualElement { style = { flexDirection = FlexDirection.Row } };
            var refresh = new Button(RefreshTasks) { text = "Refresh" };
            var disconnect = new Button(OnDisconnect) { text = "Disconnect" };
            disconnect.style.marginLeft = 4;
            buttons.Add(refresh);
            buttons.Add(disconnect);
            toolbar.Add(buttons);
            _content.Add(toolbar);

            if (_loadingTasks)
            {
                var loading = Paragraph("Loading scans");
                AnimateEllipsis(loading, "Loading scans");
                _content.Add(loading);
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
                var empty = new VisualElement();
                empty.style.flexGrow = 1f;
                empty.style.justifyContent = Justify.Center;
                empty.style.alignItems = Align.Center;

                var emptyTitle = new Label("No scans yet");
                emptyTitle.style.fontSize = 14;
                emptyTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
                emptyTitle.style.opacity = 0.8f;
                emptyTitle.style.marginBottom = 4;
                empty.Add(emptyTitle);

                var emptyHint = new Label("Create a scan in the Magiscan app,\nthen press Refresh.");
                emptyHint.style.unityTextAlign = TextAnchor.MiddleCenter;
                emptyHint.style.whiteSpace = WhiteSpace.Normal;
                emptyHint.style.opacity = 0.55f;
                empty.Add(emptyHint);

                var emptyRefresh = new Button(RefreshTasks) { text = "Refresh" };
                emptyRefresh.style.marginTop = 10;
                emptyRefresh.style.minWidth = 90;
                empty.Add(emptyRefresh);

                _content.Add(empty);
                return;
            }

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;

            _grid = new VisualElement();
            _grid.style.flexDirection = FlexDirection.Row;
            _grid.style.flexWrap = Wrap.Wrap;
            _grid.RegisterCallback<GeometryChangedEvent>(_ => LayoutGrid());
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

        /// <summary>
        /// Responsive grid: picks the column count from the available width (each card at least
        /// <see cref="MinCardWidth"/> wide), stretches cards to fill the row exactly, and keeps
        /// every preview square by matching its height to the card width.
        /// </summary>
        void LayoutGrid()
        {
            if (_grid == null) return;
            float available = _grid.resolvedStyle.width;
            int count = _grid.childCount;
            if (float.IsNaN(available) || available <= 0f || count == 0) return;
            if (Mathf.Abs(available - _lastGridWidth) < 0.5f && count == _lastGridCount) return;
            _lastGridWidth = available;
            _lastGridCount = count;

            int columns = Mathf.Max(1, Mathf.FloorToInt((available + GridGap) / (MinCardWidth + GridGap)));
            float cardWidth = (available - GridGap * (columns - 1)) / columns;

            int i = 0;
            foreach (var child in _grid.Children())
            {
                child.style.width = cardWidth;
                child.style.marginRight = (i + 1) % columns == 0 ? 0f : GridGap;
                var previewArea = child.Q<VisualElement>("magiscan-preview");
                if (previewArea != null)
                    previewArea.style.height = cardWidth;
                i++;
            }
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

        static readonly Color CardBg = new Color(0f, 0f, 0f, 0.15f);
        static readonly Color CardBgHover = new Color(0f, 0f, 0f, 0.28f);

        VisualElement BuildTaskCard(MagiscanTask task)
        {
            var card = new VisualElement();
            card.style.width = 156;
            card.style.marginRight = 8;
            card.style.marginBottom = 8;
            card.style.paddingBottom = 8;
            card.style.borderTopLeftRadius = 8;
            card.style.borderTopRightRadius = 8;
            card.style.borderBottomLeftRadius = 8;
            card.style.borderBottomRightRadius = 8;
            card.style.backgroundColor = CardBg;
            card.style.overflow = Overflow.Hidden;
            card.RegisterCallback<MouseEnterEvent>(_ => card.style.backgroundColor = CardBgHover);
            card.RegisterCallback<MouseLeaveEvent>(_ => card.style.backgroundColor = CardBg);

            // Preview: placeholder underneath, image stretched on top, status badge overlaid.
            // Named so LayoutGrid can find it and keep it square (height = card width).
            var previewArea = new VisualElement { name = "magiscan-preview" };
            previewArea.style.height = 132;
            previewArea.style.backgroundColor = new Color(0f, 0f, 0f, 0.25f);
            previewArea.style.justifyContent = Justify.Center;
            previewArea.style.alignItems = Align.Center;

            var placeholder = new Label(string.IsNullOrEmpty(task.ScanType) ? "3D" : task.ScanType);
            placeholder.style.fontSize = 16;
            placeholder.style.unityFontStyleAndWeight = FontStyle.Bold;
            placeholder.style.opacity = 0.25f;
            previewArea.Add(placeholder);

            var preview = new Image { scaleMode = ScaleMode.ScaleAndCrop };
            preview.style.position = Position.Absolute;
            preview.style.left = 0;
            preview.style.right = 0;
            preview.style.top = 0;
            preview.style.bottom = 0;
            previewArea.Add(preview);
            LoadPreview(task, preview);

            previewArea.Add(BuildStatusBadge(task));
            card.Add(previewArea);

            var name = new Label(string.IsNullOrEmpty(task.Name) ? "(untitled)" : task.Name);
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            name.style.marginLeft = 8;
            name.style.marginRight = 8;
            name.style.marginTop = 6;
            name.style.whiteSpace = WhiteSpace.Normal;
            card.Add(name);

            var glb = task.FindModel("glb");
            var meta = new Label(glb != null ? $"{task.ScanType} · {FormatSize(glb.FileSize)}" : task.ScanType);
            meta.style.fontSize = 10;
            meta.style.opacity = 0.6f;
            meta.style.marginLeft = 8;
            meta.style.marginRight = 8;
            card.Add(meta);

            var import = new Button { text = task.IsReady ? "Import" : task.Status };
            import.style.marginLeft = 8;
            import.style.marginRight = 8;
            import.style.marginTop = 6;
            import.SetEnabled(task.IsReady && glb != null);
            if (task.IsReady && glb == null)
                import.text = "No glb";
            card.Add(import);

            var progress = new ProgressBar { lowValue = 0f, highValue = 100f };
            progress.style.display = DisplayStyle.None;
            progress.style.marginLeft = 8;
            progress.style.marginRight = 8;
            progress.style.marginTop = 4;
            card.Add(progress);

            import.clicked += () => OnImportClicked(task, import, progress);
            return card;
        }

        VisualElement BuildStatusBadge(MagiscanTask task)
        {
            var badge = new VisualElement();
            badge.style.position = Position.Absolute;
            badge.style.top = 6;
            badge.style.right = 6;
            badge.style.flexDirection = FlexDirection.Row;
            badge.style.alignItems = Align.Center;
            badge.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            badge.style.borderTopLeftRadius = 8;
            badge.style.borderTopRightRadius = 8;
            badge.style.borderBottomLeftRadius = 8;
            badge.style.borderBottomRightRadius = 8;
            badge.style.paddingLeft = 6;
            badge.style.paddingRight = 6;
            badge.style.paddingTop = 2;
            badge.style.paddingBottom = 2;

            var dot = new VisualElement();
            dot.style.width = 6;
            dot.style.height = 6;
            dot.style.borderTopLeftRadius = 3;
            dot.style.borderTopRightRadius = 3;
            dot.style.borderBottomLeftRadius = 3;
            dot.style.borderBottomRightRadius = 3;
            dot.style.backgroundColor = StatusColor(task.ParsedStatus);
            dot.style.marginRight = 4;
            badge.Add(dot);

            var label = new Label(string.IsNullOrEmpty(task.Status) ? "Unknown" : task.Status);
            label.style.fontSize = 9;
            label.style.color = new Color(0.95f, 0.95f, 0.95f);
            badge.Add(label);
            return badge;
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
                LayoutGrid();
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

        async void OnImportClicked(MagiscanTask task, Button button, ProgressBar progress)
        {
            var model = task.FindModel("glb");
            if (model == null)
            {
                ShowNotification(new GUIContent("This scan has no glb model."));
                return;
            }

            button.SetEnabled(false);
            button.text = "Importing…";
            progress.value = 0f;
            progress.title = "0%";
            progress.style.display = DisplayStyle.Flex;

            var reporter = new Progress<float>(p =>
            {
                progress.value = p * 100f;
                progress.title = $"{Mathf.RoundToInt(p * 100f)}%";
            });

            try
            {
                await GlbImporter.ImportIntoSceneAsync(_client, task, model, reporter, _windowCts.Token);
                ShowNotification(new GUIContent($"Imported “{task.Name}”."));
                button.text = "Import again";
            }
            catch (OperationCanceledException)
            {
                button.text = "Import";
            }
            catch (Exception e)
            {
                Debug.LogError($"[Magiscan] Import failed: {e}");
                ShowNotification(new GUIContent("Import failed — see Console."));
                button.text = "Import";
            }
            finally
            {
                progress.style.display = DisplayStyle.None;
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

        static Color StatusColor(MagiscanScanStatus status)
        {
            switch (status)
            {
                case MagiscanScanStatus.Done: return new Color(0.35f, 0.78f, 0.40f);
                case MagiscanScanStatus.Error: return new Color(0.90f, 0.32f, 0.28f);
                case MagiscanScanStatus.Prepare:
                case MagiscanScanStatus.InQueue:
                case MagiscanScanStatus.Processing: return new Color(0.95f, 0.72f, 0.22f);
                default: return new Color(0.6f, 0.6f, 0.6f);
            }
        }

        static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024L) return $"{bytes / (1024f * 1024f):0.#} MB";
            if (bytes >= 1024L) return $"{bytes / 1024f:0} KB";
            return $"{bytes} B";
        }

        /// <summary>Cycles "text", "text.", "text..", "text..." while the label is on screen.</summary>
        static void AnimateEllipsis(Label label, string baseText)
        {
            int step = 0;
            var item = label.schedule.Execute(() =>
            {
                step = (step + 1) % 4;
                label.text = baseText + new string('.', step);
            }).Every(400);
            label.RegisterCallback<DetachFromPanelEvent>(_ => item.Pause());
        }

        /// <summary>Softly pulses an element's opacity while it is on screen.</summary>
        static void AnimatePulse(VisualElement element)
        {
            bool dim = false;
            var item = element.schedule.Execute(() =>
            {
                dim = !dim;
                element.style.opacity = dim ? 0.35f : 1f;
            }).Every(500);
            element.RegisterCallback<DetachFromPanelEvent>(_ => item.Pause());
        }

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
