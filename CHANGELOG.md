# Changelog

All notable changes to this package are documented here. The format is based on
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
