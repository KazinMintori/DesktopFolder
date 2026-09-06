<p align="center">
  <img src="assets/DesktopFolders.png" width="112" alt="DesktopFolders icon">
</p>

<h1 align="center">DesktopFolders</h1>

<p align="center">
  Beautiful, virtual app collections for the Windows Desktop.<br>
  Organize shortcuts and system icons without moving or deleting the files behind them.
</p>

<p align="center">
  <a href="https://github.com/KazinMintori/DesktopFolder/releases/latest/download/DesktopFolders.exe"><strong>Download for Windows</strong></a>
  ·
  <a href="https://github.com/KazinMintori/DesktopFolder/releases/latest">Release notes</a>
</p>

---

DesktopFolders brings phone-style grouping to the classic Windows Desktop. Drag one supported icon over another, hold briefly, and a polished cosmic-glass collection replaces the individual icons. Open it to search, reorder, pin, nest, or restore items whenever you want.

## Why DesktopFolders?

- **A cleaner desktop, without real folders.** Collections are metadata, not directories.
- **Your files stay where they are.** Original paths and attributes are preserved.
- **Designed for Windows.** Desktop placement uses supported Shell APIs instead of writing into Explorer.
- **More than shortcuts.** Recycle Bin, This PC, and Network can join filesystem items in mixed collections.
- **Fast and focused.** One portable executable, no account, no network service, and no third-party runtime files.

## Product highlights

| | Capability |
|---|---|
| **Cosmic collection UI** | Compact and expanded layouts, instant search, polished glass cards, grid/list modes, and restrained motion. |
| **Flexible organization** | Pin favorites, drag to reorder, transfer between open collections, or create nested collections. |
| **Safe restoration** | Drag members back to the Desktop or dissolve a collection; original attributes and Shell visibility are restored. |
| **Collision-free placement** | Restored filesystem icons are placed into the nearest available Desktop grid slot through `IFolderView`. |
| **Accessible controls** | Keyboard navigation, visible keyboard state, descriptive control names, F2 rename, Ctrl+F search, and Escape close. |
| **Bilingual interface** | English and Vietnamese are embedded in the executable, with automatic system-language selection. |

## How grouping works

1. Drag a supported Desktop icon over another supported icon.
2. Hold for the configured delay—280 ms by default.
3. Release when the collection preview appears.

Quick drops before the threshold remain native Windows Explorer operations. DesktopFolders uses no global mouse hook and does not inject code into Explorer.

## Download and install

1. Download [`DesktopFolders.exe`](https://github.com/KazinMintori/DesktopFolder/releases/latest/download/DesktopFolders.exe).
2. Move it to a permanent user-writable location.
3. Run it and review Settings.
4. Optionally enable **Start with Windows**.

The release is portable: artwork, localization, and application resources are embedded. Developer tools are not required on the destination machine.

### Requirements

- Windows 10 or Windows 11.
- The classic Explorer Desktop backed by `SysListView32`.
- .NET Framework 4.x, included with supported Windows installations.

> **Unsigned build:** the current executable is not Authenticode-signed. SmartScreen or antivirus reputation checks may warn about a newly downloaded release. Compare its SHA-256 value with the checksum published in the GitHub Release.

## Data safety and privacy

DesktopFolders never moves members into physical folders and never deletes user files.

- Filesystem members keep their absolute path and original `FileAttributes` in `%APPDATA%\DesktopFolders\virtual-layout.json`.
- Recycle Bin, This PC, and Network use stable Windows Known Folder identities. Raw PIDL pointers are never persisted.
- Runtime PIDLs and Shell COM objects are scoped and released after use.
- The application contains no network client, telemetry, account system, API keys, or bundled third-party service.

## Safe uninstall

1. Right-click the DesktopFolders tray icon.
2. Choose **Prepare uninstall…** and confirm.
3. Verify that collection members have returned to the Desktop.
4. Delete `DesktopFolders.exe`.

The preparation step restores members before clearing the layout, removes per-user startup and file-registration data, and aborts safely if a member is unavailable.

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+F` | Focus collection search |
| `F2` | Rename the active collection |
| `Enter` / `Space` | Activate the focused item or control |
| `Escape` | Clear search, close a collection, or close Settings |

## Build from source

Building requires Windows and the .NET Framework 4.x C# compiler. The resulting executable has no Windows SDK metadata dependency.

```powershell
.\build.ps1
.\build.ps1 -TraceDrag -Output DesktopFolders-trace.exe
.\build.ps1 -TestBuild -Output DesktopFolders-test.exe
.\DesktopFolders-test.exe --test-layout-graph
.\DesktopFolders-test.exe --test-shell-items
.\DesktopFolders-test.exe --test-uninstall-restore
.\verify-release.ps1
```

See [`AGENTS.md`](AGENTS.md) and [`docs/ai/ARCHITECTURE.md`](docs/ai/ARCHITECTURE.md) for maintainer invariants and architecture notes.

## Current limitations

- Other pathless Shell namespace objects, such as Control Panel, are not supported.
- Changing attributes for Public Desktop items may require elevated permissions.
- Third-party Explorer replacements that do not expose the classic Desktop list view are unsupported.
- The release has no automatic updater; install updates by replacing the executable. Existing collection shortcuts self-heal to the new executable location on startup.
