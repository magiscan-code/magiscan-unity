# Magiscan for Unity

Import 3D scans from the [Magiscan AI](https://magiscan.ar-generation.com) app directly into
Unity: link your account once with a QR code, browse your scans in an Editor window, and add
models to the open scene in one click. A clean runtime C# client is included for player builds.

## Requirements

- Unity **2021.3 LTS or newer**.
- Dependencies (installed automatically by the Package Manager):
  `com.unity.cloud.gltfast` ≥ 6.0.1, `com.unity.nuget.newtonsoft-json` ≥ 3.0.2.
- The Magiscan mobile app with an account, used once to approve the link.

## The Editor window

**Window ▸ Magiscan**

1. **Connect** — the window calls `POST /link/start` and renders the returned code as a QR.
2. Scan it in the Magiscan app (*Linked Devices ▸ Scan QR*) and approve. The window polls
   `POST /link/poll` (device-flow style: `pending` / `slow_down` / `approved` / `expired`).
3. On approval the integration token is stored per-user in `EditorPrefs` and the window shows
   your scans, newest first, 50 per page.
4. **Import** downloads the scan's glb into `Assets/Magiscan/Models/` (name-collision-safe,
   suffixed with the scan id), imports it through glTFast's scripted importer, and instantiates
   it into the open scene with Undo support.

**Disconnect** forgets the token on this machine. To revoke the device permanently, remove it
in the mobile app under *Linked Devices*.

### Changing the server (staging / QA)

Set the `MAGISCAN_BASE_URL` environment variable before launching Unity; otherwise the built-in
default (`MagiscanSettings.DefaultBaseUrl`) is used.

## Architecture

```
Runtime/   Magiscan.asmdef — no Editor dependency, works in player builds
  MagiscanClient        Typed API client: /link/start, /link/poll, /integrations/tasks
  MagiscanSettings      Base URL, integration type, device info
  Http/IHttpClient      Transport abstraction (UnityWebRequest implementation included)
  Auth/ITokenStorage    Token storage abstraction (PlayerPrefs implementation included)
  Models/               DTOs: MagiscanTask, MagiscanModel, link responses, status enums
Editor/    Magiscan.Editor.asmdef — Editor-only
  MagiscanWindow        UI Toolkit window (connect / QR pairing / scan grid)
  MagiscanLinkController QR link flow driver, UI-agnostic, exposes an awaitable Running task
  QrCode/               QR encoder (MIT, Project Nayuki) + texture builder
  ModelImport/GlbImporter Downloads a glb and instantiates it into the scene
Tests/     EditMode tests: client (against a fake transport), QR encoder, link controller
Samples~/RuntimeBrowser  Play-mode linking + browsing + glTFast loading example
```

Error handling is typed: `MagiscanUnauthorizedException` (401 — the window drops the token and
returns to the connect screen), `MagiscanRateLimitException` (429), `MagiscanNetworkException`
(transport). API calls time out after 30 s; model downloads are unlimited but cancellable and
report progress.

## Runtime API

```csharp
using Magiscan;
using Magiscan.Http;

var settings = MagiscanDeviceInfo.Apply(MagiscanSettings.CreateDefault());
var client = new MagiscanClient(settings, new UnityWebRequestHttpClient(), new PlayerPrefsTokenStorage());

// Link once (render start.UserCode as a QR, then poll):
var start = await client.StartLinkAsync();
var poll = await client.PollLinkAsync(start.DeviceCode);
if (poll.ParsedStatus == MagiscanLinkStatus.Approved)
    client.Tokens.Save(poll.IntegrationToken);

// Browse and download:
foreach (var task in await client.GetTasksAsync(skip: 0, limit: 50))
{
    if (!task.IsReady) continue;
    var glb = task.FindModel("glb");
    byte[] bytes = await client.DownloadAsync(glb.Url);
    // Load with glTFast: GltfImport.LoadGltfBinary + InstantiateMainSceneAsync.
}
```

See `Samples~/RuntimeBrowser` for the complete flow. Inject your own `IHttpClient` to test
without the network, or your own `ITokenStorage` to use a platform secure store — recommended
for production builds, since `PlayerPrefs`/`EditorPrefs` store the token unencrypted.

## Render pipelines (URP / HDRP)

glTFast ships Built-in and **URP** shader graphs — scans work out of the box in both. Under
**HDRP**, glTFast's shader-graph support covers common cases but complex material features may
need manual adjustment; photogrammetry scans use a single baked albedo texture, which converts
cleanly in practice. If a model imports pink, check that the active pipeline's glTFast shader
variants are included (see the glTFast documentation on shader stripping in builds).

Scan geometry is unoptimized photogrammetry output (tens of thousands of triangles, one large
texture). For mobile targets, budget accordingly or decimate after import.

## Tests

**Window ▸ General ▸ Test Runner ▸ EditMode**. To see the package tests in your project, add to
`Packages/manifest.json`:

```json
"testables": ["com.magiscan"]
```
