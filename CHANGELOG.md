# Changelog

All notable changes to this package are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.3.0] - 2026-08-10

### Added
- The **Runtime Browser** sample declared in `package.json` now actually exists
  (`Samples~/RuntimeBrowser`): link an account, browse scans, and load a glb with glTFast
  during play mode.
- The server URL can now be overridden for staging/QA: the `MAGISCAN_BASE_URL` environment
  variable, or per-user via the new **Advanced ▸ Server URL** field on the connect screen.

### Changed
- The default API endpoint no longer uses the non-standard port 5124 — the server is now
  reachable over standard HTTPS (443), which works through corporate firewalls. Existing
  clients pointing at `:5124` keep working; the server serves both.
- Minimum supported Unity version lowered from 6000.0 to **2021.3 LTS** — every API the package
  uses is available there; CI will verify the matrix.
- The `com.unity.nuget.newtonsoft-json` dependency is now pinned at 3.0.2 (the lowest 3.x)
  instead of 3.2.1, so projects that already ship a newer Json package resolve without conflict.

### Fixed
- `MagiscanLinkController.Begin()` is no longer `async void`: the flow is exposed as an awaitable
  `Running` task that never faults, and a throwing `Changed` subscriber can no longer abort the
  link flow (it is logged instead). Covered by new `MagiscanLinkControllerTests`.
- The Magiscan window now cancels in-flight work right before script compilation reloads the
  domain, so it comes back in a consistent state instead of a stale "Waiting"/"Loading" view.
- API calls now time out after 30 seconds instead of hanging forever on a dead connection.
  Model and preview downloads are still unlimited — they have progress and can be cancelled.
- Loading/pulse animations stop when their element leaves the panel (previously the scheduled
  callbacks were never released).

### Removed
- The dead `MAGISCAN_GLTFAST` define check (`GlbImporter.IsGltfastInstalled`). glTFast is a hard
  package dependency and is always present; the check could never report otherwise and only
  confused readers.

## [0.2.1] - 2026-07-05

### Changed
- The scan grid is now responsive: the column count adapts to the window width, cards stretch to
  fill each row exactly, and previews are always square. Relayouts on window resize and after
  "Load more".

## [0.2.0] - 2026-07-05

### Changed
- Redesigned the linking screen into a centered pairing view: the QR sits on a rounded white
  card, the link code copies to the clipboard on click, and an animated "Waiting for approval"
  indicator shows the window is polling.
- Scan cards polished: colored status badge (Done / Processing / Error) overlaid on the preview,
  glb file size in the caption, a scan-type placeholder while the preview loads, hover highlight.
- Import now shows a progress bar with percentage on the card; after a successful import the
  button becomes "Import again".
- Nicer empty state, scan counter in the header, animated loading labels, and a window tab icon.

## [0.1.0] - 2026-06-23

### Added
- Initial release.
- `MagiscanClient` runtime API client for the Linked Devices API (`/link/start`, `/link/poll`,
  `/integrations/tasks`, `/integrations/tasks/{id}`) with typed models and error handling.
- Editor window (**Window ▸ Magiscan**): QR-based account linking, scan browser with previews,
  and one-click glb import into the open scene.
- `EditorPrefs`-backed integration token storage.
- `POST /link/start` now reports device info (`deviceName`, `platform`, `appVersion`) so the phone's
  approve screen can show what is being connected. Sourced from `SystemInfo` via `MagiscanDeviceInfo`.
- Scans are listed newest-first (server-sorted by InitTime).
- Scan list is paginated — loads 50 at a time with a **Load more** button (appended without
  rebuilding the list, so the scroll position is kept). `skip`/`limit` are sent as query parameters.
- Relative `previewUrl` values are resolved against `{BaseUrl}/file/...`; preview load failures are
  now logged with the resolved URL.
