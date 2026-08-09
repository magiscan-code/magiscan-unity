# Magiscan for Unity

Import 3D scans from the [Magiscan AI](https://magiscan.ar-generation.com) app directly into
Unity. Link your account once with a QR code, browse your scans, and drop the models into the
open scene — no manual file juggling.

- **Editor window** — connect, browse scans with previews, one-click import into the scene.
- **Runtime API** — a clean C# client you can also use in player builds.
- Models are imported as regular project assets (`.glb`, via [glTFast](https://docs.unity3d.com/Packages/com.unity.cloud.gltfast@6.0/manual/index.html)).

## Requirements

- **Unity 2021.3 LTS or newer** (the CI test matrix targets 2021.3, 2022.3, and Unity 6;
  primary development happens on Unity 6).
- Internet access — dependencies (glTFast, Newtonsoft Json) are fetched automatically by the
  Package Manager. The API is served over standard HTTPS (443), so corporate firewalls are fine.
- The **Magiscan** mobile app ([iOS / Android](https://magiscan.ar-generation.com)) with an
  account — used once, to approve the link by QR.

The server address is built in and needs no configuration. For staging/QA there is an override:
the **Advanced ▸ Server URL** field on the connect screen, or the `MAGISCAN_BASE_URL`
environment variable.

## Installation

**From a git URL** (recommended):

1. Open **Window ▸ Package Manager**.
2. Press **`+`** ▸ **Add package from git URL…**
3. Paste:

   ```
   https://github.com/magiscan-code/magiscan-unity.git
   ```

   To pin a specific version, append a tag: `https://github.com/magiscan-code/magiscan-unity.git#v0.1.0`

**From disk:** clone this repository, then *Package Manager ▸ + ▸ Add package from disk…* and
pick `package.json`. Or copy the folder into your project's `Packages/` directory (embedded).

## Getting started

1. Open **Window ▸ Magiscan**.
2. Click **Connect** — a QR code appears.
3. In the Magiscan app: **Linked Devices ▸ Scan QR**, scan and approve.
4. The window switches to your scans, newest first. Click **Import** on any finished scan —
   the model is downloaded to `Assets/Magiscan/Models/` and added to the open scene.
5. **Load more** at the bottom of the list pages in the next 50 scans.

Linking is one-time: the integration token is stored per-user via `EditorPrefs`.
**Disconnect** forgets it on this machine; to revoke it for good, remove the device in the
Magiscan app under *Linked Devices*.

## Runtime API

The `Runtime/` assembly has no Editor dependency, so the same client works in player builds:

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
    // Instantiate with glTFast: GltfImport.LoadGltfBinary + InstantiateMainScene.
}
```

Inject your own `IHttpClient` to unit-test without the network, or your own `ITokenStorage`
to keep the token in a platform secure store. See `Samples~/RuntimeBrowser` for a complete
runtime example (importable from the Package Manager's *Samples* tab).

## Render pipelines (URP / HDRP)

glTFast ships Built-in and **URP** shaders — imported scans render out of the box in both.
Under **HDRP** common cases work, but exotic material setups may need manual adjustment;
photogrammetry scans use one baked albedo texture, which converts cleanly in practice.
If a model imports pink, check glTFast's shader/stripping notes for your pipeline.
Mind the budget on mobile: raw scans are tens of thousands of triangles with a large texture.
More detail in [`Documentation~/magiscan.md`](Documentation~/magiscan.md).

## Troubleshooting

| Symptom | Fix |
| --- | --- |
| Model downloaded but didn't import | Make sure `com.unity.cloud.gltfast` is present (it installs automatically as a dependency). |
| List or previews don't load | Check your connection; the Console logs the reason as `[Magiscan] …`. Corporate TLS-inspection proxies can break requests — try from another network to confirm. |
| Import disabled on a scan | The scan is still processing (not `Done`), or it has no `glb` model (e.g. a point cloud). |
| Window returned to Connect on its own | The device was revoked from the app — link again. |
| *"The link code expired"* | QR codes live for 10 minutes; press Connect to get a fresh one. |

## Running the tests

**Window ▸ General ▸ Test Runner ▸ EditMode ▸ Run All** — covers the API client (against a
fake HTTP layer) and the QR encoder. To make package tests visible in your project, add to
`Packages/manifest.json`:

```json
"testables": ["com.magiscan"]
```

## Package layout

```
Runtime/    API client (MagiscanClient), models, IHttpClient / ITokenStorage abstractions
Editor/     Editor window (UI Toolkit), QR rendering, linking flow, glb-to-scene import
Tests/      EditMode tests
Samples~/   Runtime usage example
```

More detail in [`Documentation~/magiscan.md`](Documentation~/magiscan.md).

## How linking works

1. The plugin calls `POST /link/start` and gets a short-lived `userCode`, shown as a QR.
2. The phone scans it and approves; the plugin polls until it receives an integration token.
3. Scans are listed via `GET /integrations/tasks` (read-only), and models are downloaded from
   direct URLs. The token can be revoked from the app at any time.

## License

Proprietary — © 2026 Magiscan Inc. See [LICENSE.md](LICENSE.md). Free to install and use for
integrating with Magiscan services.
