<p align="center">
  <img src="assets/DesktopFolders.png" width="112" alt="DesktopFolders icon">
</p>

<h1 align="center">DesktopFolders</h1>

<p align="center">
  Virtual app collections for the Windows desktop.
</p>

<p align="center">
  Group shortcuts, files, folders, and selected Windows system icons into compact desktop collections while keeping the original items in place.
</p>

---

DesktopFolders brings phone-style icon grouping to the Windows desktop. Instead of moving items into physical folders, it keeps collection information separately and preserves the original desktop items and their locations.

Drag one supported desktop icon over another, hold briefly, and release when the collection preview appears. Collections can then be opened, searched, reordered, nested, or restored back to the desktop.

## Features

| Feature | Description |
|---|---|
| **Virtual collections** | Group supported desktop items without creating physical folders or changing their original paths. |
| **Compact and expanded views** | Open collections in a compact layout or expand them when more space is needed. |
| **Search and organization** | Search items, reorder them with drag and drop, pin favorites, move items between collections, and create nested collections. |
| **Desktop restoration** | Move members back to the desktop or dissolve a collection when it is no longer needed. |
| **Grid-aware placement** | Restored filesystem items are placed in an available desktop position. |
| **System icon support** | Recycle Bin, This PC, and Network can be grouped alongside supported filesystem items. |
| **Keyboard support** | Includes keyboard navigation, F2 rename, Ctrl+F search, Enter/Space activation, and Escape handling. |
| **English and Vietnamese** | Includes embedded English and Vietnamese localization with automatic system-language selection. |

## Download and run

The repository includes a ready-to-run `DesktopFolders.exe` together with the complete source code.

1. Click **Code** at the top of this repository.
2. Choose **Download ZIP**.
3. Extract the downloaded archive.
4. Open the extracted `DesktopFolder-main` folder.
5. Run `DesktopFolders.exe`.

No installer is required.

## How to use

1. Drag a supported desktop icon over another supported icon.
2. Hold briefly over the target icon.
3. Release when the collection preview appears.
4. Open the collection to manage its contents.

The default grouping delay is 280 ms. Quick drag-and-drop actions before that threshold continue to behave as normal Windows desktop operations.

## Requirements

- Windows 10 or Windows 11.
- Standard Windows Explorer desktop environment.
- .NET Framework 4.x.

## Build from source

If you prefer to build DesktopFolders yourself, download or clone the repository and run the included PowerShell build script.

### Download ZIP

1. Select **Code → Download ZIP**.
2. Extract the archive.
3. Open PowerShell in the extracted project directory.
4. Run:

```powershell
.\build.ps1
```

### Clone with Git

```powershell
git clone https://github.com/KazinMintori/DesktopFolder.git
cd DesktopFolder
.\build.ps1
```

The build script compiles the C# source and embedded application resources into `DesktopFolders.exe`.

Visual Studio is not required for the default build process. The script uses the C# compiler included with .NET Framework.

### Development builds

```powershell
.\build.ps1 -TraceDrag -Output DesktopFolders-trace.exe
.\build.ps1 -TestBuild -Output DesktopFolders-test.exe
```

The test build provides additional internal validation commands:

```powershell
.\DesktopFolders-test.exe --test-layout-graph
.\DesktopFolders-test.exe --test-shell-items
.\DesktopFolders-test.exe --test-uninstall-restore
```

## Data and privacy

DesktopFolders operates locally on the computer.

- Collection layout data is stored in `%APPDATA%\DesktopFolders\virtual-layout.json`.
- Filesystem items remain at their original filesystem locations.
- Supported Windows system icons are referenced through Windows Shell identities.
- DesktopFolders does not include telemetry, user accounts, or a background network service.

## Restoring the desktop and uninstalling

Before removing DesktopFolders, use the built-in **Prepare uninstall…** option to restore collection members to the desktop.

1. Right-click the DesktopFolders tray icon.
2. Choose **Prepare uninstall…**.
3. Confirm the operation.
4. Verify that collection members have returned to the desktop.
5. Delete `DesktopFolders.exe` and the extracted project folder if they are no longer needed.

## Keyboard shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+F` | Focus collection search |
| `F2` | Rename the active collection |
| `Enter` / `Space` | Activate the focused item or control |
| `Escape` | Clear search, close a collection, or close Settings |

## Project structure

| Path | Purpose |
|---|---|
| `src/DesktopFolders.cs` | Main application source code |
| `Resources/` | English and Vietnamese localization resources |
| `assets/` | Application artwork and icons |
| `config/app.manifest` | Windows application manifest |
| `build.ps1` | Main build script |
| `docs/` | Architecture and maintainer documentation |

Additional maintainer documentation is available in [`AGENTS.md`](AGENTS.md) and [`docs/ai/ARCHITECTURE.md`](docs/ai/ARCHITECTURE.md).

## Current limitations

- Some Windows Shell namespace items, such as Control Panel entries, are not currently supported.
- Changes involving Public Desktop items may require elevated permissions.
- Third-party Explorer replacements that do not expose the expected Windows desktop interfaces are not supported.
- Automatic updates are not currently included.

## Project status

DesktopFolders is under active development. Compatibility, collection behavior, and interface details may continue to change as the project evolves.

Bug reports and reproducible compatibility issues are welcome through GitHub Issues.
