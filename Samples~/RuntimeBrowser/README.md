# Runtime Browser sample

Links a Magiscan account **during play mode**, lists the account's scans, and loads a selected
scan into the scene with glTFast — everything the Editor window does, but with the runtime API
only, so the same code works in player builds.

## Run it

1. Import this sample from the Package Manager (*Magiscan AI ▸ Samples ▸ Runtime Browser*).
2. Create an empty GameObject in any scene and add **RuntimeBrowserExample**.
3. Press Play, click **Link account**, and approve in the Magiscan mobile app
   (*Linked Devices ▸ Scan QR*). The sample shows the raw link code — a real app would render
   it as a QR code with any QR library.
4. Click **Load** on a finished scan: the glb is downloaded and instantiated at the origin.

## What to look at

- `RuntimeBrowserExample.cs` — the whole flow: `StartLinkAsync` → `PollLinkAsync` loop →
  `GetTasksAsync` → `DownloadAsync` → `GltfImport.LoadGltfBinary` + `InstantiateMainSceneAsync`.
- The token is stored with `PlayerPrefsTokenStorage`. For production, implement `ITokenStorage`
  against your platform's secure storage instead.
