# DesktopFolders

DesktopFolders creates virtual collections directly on your Windows Desktop, similar to app groups on a phone home screen. Collections are **not** Explorer folders — original files and shortcuts are only hidden, never deleted or moved.

<img src="DesktopFolders-icon.png" width="96" alt="DesktopFolders icon">

## Features

- **Drag-and-drop grouping** — drag one Desktop icon onto another and hold ~280 ms to create a collection. Quick drops (< 280 ms) pass through to Windows as normal.
- **Collection popup** — search, grid/list view, pin favorites, rename, and drag items in or out.
- **Compact & expanded layouts** — compact `440×520`, expanded `840×600`, always keeping three cards per row.
- **Smart popup positioning** — popup anchors beside the collection tile and follows it when the Desktop layout changes.
- **Cross-collection transfers** — move items between open collections without losing original file attributes.
- **Virtual layout persistence** — layout saved at `%APPDATA%\DesktopFolders\virtual-layout.json`.
- **System tray app** — runs silently in the background; supports Backup/Restore and Windows startup integration.
- **Reliable IPC** — each collection has at most one popup; startup IPC includes retry, coalescing, and fallback to avoid losing open commands.
- **Keyboard accessible** — Tab focus ring, Enter/Space to open, F2 to rename, Ctrl+F to search, Escape to close.

## Data Safety

DesktopFolders never converts a collection into a physical folder. When an item is added to a collection, the app saves the original file attributes and sets the `Hidden` flag. When an item is dragged out or restored, the original attributes are reapplied.

The tray menu option **Restore all icons then Exit** unhides every item, removes collection tiles, and exits the application.

## Installation

1. Download [`DesktopFolders.exe`](DesktopFolders.exe) from this repository.
2. Run the EXE — the app appears in the system tray with no persistent taskbar window.
3. Drag one Desktop icon onto another and hold ~280 ms to create your first collection.

> **Note:** The application is not code-signed, so Windows SmartScreen may show a warning for files downloaded from the Internet.

## Build from Source

Requires Windows with .NET Framework 4.x (csc.exe, WinForms, UI Automation assemblies).

```powershell
.\build.ps1
```

Diagnostic build with drag/open tracing (writes to `%APPDATA%\DesktopFolders\drag-diagnostic.log`):

```powershell
.\build.ps1 -TraceDrag -Output DesktopFolders-test.exe
```

Regenerate the flat project icon:

```powershell
.\generate-icon.ps1
```

The icon is rendered from flat geometry using `System.Drawing`; this repository does not use AI-generated artwork.

## Repository Structure

```text
DesktopFolders.exe              Release build
DesktopFolders-source.zip       Source code archive
DesktopFolders-icon.png         App icon
README.md                       English documentation (this file)
README-DesktopFolders.txt       Detailed Vietnamese documentation
```

## How It Works

1. A low-level mouse hook detects drag gestures only when the Desktop shell is in the foreground.
2. If the user holds an icon over a target for ~280 ms, the app enters `MERGE_ARMED` state and shows a preview animation.
3. Just before mouse-up, DesktopFolders sends `Escape` + `WM_CANCELMODE` to cancel the Explorer drag, swallows the mouse-up, and commits exactly one collection operation.
4. Moving away from the target at any point before release cancels the armed state and returns the gesture to Windows.
5. The Desktop cache scans in the background every 1.2 s, with a dedicated 450 ms refresh after each quick Windows drop to maintain accurate positions without continuous lag.

## Performance

- The hook only activates when the Desktop shell is in the foreground — interactions at the same coordinates in other applications are never intercepted.
- UI Automation scans run frequently only while the Desktop is active; when another app is in the foreground, the process sleeps and proactively releases unused working set.
- Smoke test on the build machine: **0.0 s CPU** over 8 s when the Desktop is not in the foreground; private bytes ~38 MB, resident working set drops to ~4 MB after idle trim.

## Current Limitations

- Only Desktop items with a file path can be added to collections.
- Virtual system icons (e.g., Recycle Bin) are not yet supported.
- Windows-only; depends on Desktop Explorer (`SysListView32`).

## License

See repository for license details.
