# Repository map

## Product

- `src/DesktopFolders.cs` — complete application source.
  - Models and persistence: `AppSettings`, `VirtualMember`, `VirtualGroup`, `VirtualLayout`, `DataStore`, `VirtualLayoutGraph`.
  - Shell integration: `ShellDesktopItems`, `ExplorerDesktop`, Shell COM interfaces, `Native`.
  - Collection UI: `FolderPanel`, `HeaderIconButton`, `RoundedPanel`, `DarkScrollBar`, `AppTile`, animation overlays.
  - Controller and entry point: `DirectDesktopController`, `SingleInstanceCommandWindow`, `Program`.
- `assets/CollectionBackground.png` — embedded collection artwork.
- `assets/DesktopFolders.png` — canonical branding image.
- `assets/DesktopFolders.ico` — executable icon generated from the PNG.
- `Resources/strings.en.json`, `Resources/strings.vi.json` — embedded localization dictionaries.

## Build and verification

- `build.ps1` — validates required inputs, locates .NET Framework references, and produces the executable without Windows SDK metadata dependencies.
- `verify-release.ps1` — checks embedded resources, runtime-reference allowlist, version metadata, Authenticode status, and SHA-256 without launching the product.
- `sign-release.ps1` — signs a release with a Code Signing certificate from the current-user certificate store, adds an RFC 3161 timestamp, and verifies the signature.
- `.github/workflows/release.yml` — protected tag/manual release pipeline that imports an ephemeral PFX secret, builds, signs, verifies, and publishes release artifacts.
- `config/app.manifest` — declares as-invoker execution, supported Windows generation, and system-DPI awareness.
- `generate-icon.ps1` — regenerates the PNG-backed ICO.
- `tools/ui-preview/PreviewHarness.cs` — reflection-based UI regression harness for compact/expanded layouts, repaint stability, search perimeter continuity, background drag, localization, and DPI behavior.
- `tools/ui-preview/README.md` — harness commands and expected output.

## Maintainer documentation

- `AGENTS.md` — invariants and required working procedure.
- `README.md` — user installation, build, safety, uninstall, and limitations.
- `docs/ai/ARCHITECTURE.md` — subsystem behavior and safety boundaries.
- `docs/ai/CURRENT_STATE.md` — verified release baseline and risks.
- `docs/ai/HANDOFF.md` — concise next-agent context.
- `docs/ai/UI_DESIGN.md` — current visual design decisions.

## Generated outputs

Only `DesktopFolders.exe` is retained as the current portable release. Diagnostic/test executables and preview images are generated on demand and ignored by Git.
