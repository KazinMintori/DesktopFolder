<p align="center">
  <img src="assets/DesktopFolders.png" width="112" alt="DesktopFolders icon">
</p>

<h1 align="center">DesktopFolders</h1>

<p align="center">
  Phone-style virtual collections for the Windows desktop.
</p>

<p align="center">
  Organize desktop shortcuts and selected Windows system icons without moving them into physical folders.
</p>

---

DesktopFolders is a lightweight Windows desktop organizer that lets you group supported desktop items into virtual collections. Collections are stored as metadata, while the original files and shortcuts remain in their existing locations.

Drag one supported desktop icon over another, hold briefly, and DesktopFolders can create a collection in their place. Collections can then be opened to search, reorder, pin, nest, or restore their contents.

## Features

| Feature | Description |
|---|---|
| **Virtual collections** | Group supported desktop items without creating physical folders or changing their original paths. |
| **Compact and expanded views** | Open collections in a compact layout or expand them when more space is needed. |
| **Search and organization** | Search items, reorder them with drag and drop, pin favorites, transfer items between collections, and create nested collections. |
| **Desktop restoration** | Move members back to the desktop or dissolve a collection when it is no longer needed. |
| **Grid-aware placement** | Restored filesystem items are placed in an available desktop position instead of intentionally overlapping existing icons. |
| **System icon support** | Recycle Bin, This PC, and Network can be used alongside supported filesystem items. |
| **Keyboard support** | Includes keyboard navigation, F2 rename, Ctrl+F search, Enter/Space activation, and Escape handling. |
| **English and Vietnamese** | The interface includes embedded English and Vietnamese localization with automatic system-language selection. |

## How it works

1. Drag a supported desktop icon over another supported icon.
2. Hold briefly over the target icon.
3. Release when the collection preview appears.

By default, the grouping delay is 280 ms. Quick drops before that threshold are left to the normal Windows desktop drag-and-drop behavior.

DesktopFolders interacts with the Windows Shell and does not inject code into Explorer.

## Getting the source code

DesktopFolders is currently distributed through its source repository rather than pre-built releases.

### Option 1: Download ZIP

1. Open this repository on GitHub.
2. Select **Code → Download ZIP**.
3. Extract the archive to a local folder.
4. Open PowerShell in the extracted project directory.
5. Build the application with:

```powershell
.\build.ps1
```

The generated executable will be written to the project directory as `DesktopFolders.exe` unless another output name is specified.

### Option 2: Clone with Git

```powershell
git clone https://github.com/KazinMintori/DesktopFolder.git
cd DesktopFolder
.\build.ps1
```

## Requirements

To build and run DesktopFolders, you need:

- Windows 10 or Windows 11.
- The standard Windows Explorer desktop environment.
- .NET Framework 4.x.
- PowerShell for the included build script.

The project uses the C# compiler included with the .NET Framework installation and does not require Visual Studio for the default build process.

## Build options

The standard build command is:

```powershell
.\build.ps1
```

Additional development builds are available:

```powershell
.\build.ps1 -TraceDrag -Output DesktopFolders-trace.exe
.\build.ps1 -TestBuild -Output DesktopFolders-test.exe
```

Test builds expose the project's internal validation commands:

```powershell
.\DesktopFolders-test.exe --test-layout-graph
.\DesktopFolders-test.exe --test-shell-items
.\DesktopFolders-test.exe --test-uninstall-restore
```

Release verification tooling can be run with:

```powershell
.\verify-release.ps1
```

## Data and privacy

DesktopFolders is designed to operate locally.

- Collection layout data is stored under `%APPDATA%\DesktopFolders\virtual-layout.json`.
- Filesystem items keep their original filesystem locations.
- Supported Windows system icons are represented using Windows Shell identities rather than copied into project-managed folders.
- The application does not include telemetry, an account system, API keys, or a background network service.

## Restoring the desktop

DesktopFolders provides a **Prepare uninstall…** action from its tray menu.

Before removing the application:

1. Right-click the DesktopFolders tray icon.
2. Choose **Prepare uninstall…**.
3. Confirm the operation.
4. Verify that collection members have returned to the desktop.
5. Remove the application executable and project files if they are no longer needed.

The preparation step attempts to restore collection members before clearing the saved virtual layout.

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+F` | Focus collection search |
| `F2` | Rename the active collection |
| `Enter` / `Space` | Activate the focused item or control |
| `Escape` | Clear search, close a collection, or close Settings |

## Project structure

The application is built primarily from the C# source under `src/`, together with embedded assets, localization resources, and the application manifest.

Useful project files include:

- `build.ps1` — main build script.
- `verify-release.ps1` — release validation checks.
- `src/DesktopFolders.cs` — application source.
- `Resources/` — embedded localization resources.
- `assets/` — application artwork and icons.
- `config/app.manifest` — Windows application manifest.
- `docs/` — architecture, maintenance, and development documentation.

Maintainer documentation is available in [`AGENTS.md`](AGENTS.md), [`docs/ai/ARCHITECTURE.md`](docs/ai/ARCHITECTURE.md), and [`docs/CODE_SIGNING.md`](docs/CODE_SIGNING.md).

## Current limitations

- Some Windows Shell namespace items, such as Control Panel entries, are not currently supported.
- Changing attributes for Public Desktop items may require elevated permissions.
- Third-party Explorer replacements that do not expose the expected Windows desktop interfaces are not supported.
- DesktopFolders does not currently include an automatic updater.

## Project status

DesktopFolders is under active development. Behavior may change as desktop compatibility, collection handling, and user-interface details are refined.

Bug reports and reproducible compatibility issues are welcome through GitHub Issues.
