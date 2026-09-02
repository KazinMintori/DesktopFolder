# DesktopFolders

<img src="DesktopFolders-icon.png" width="80" alt="DesktopFolders icon — dark slate folder with four colored app tiles">

**iPhone-style app groups for your Windows Desktop.**

Drag one Desktop icon onto another, hold for a moment, and they merge into a virtual collection — just like grouping apps on a phone home screen. Your files are never moved or deleted; they are only hidden behind a single collection tile that you can open, search, and reorganize at any time.

## How it works

```
  Drag icon A      Hold over icon B       Release
  onto icon B       for ~280 ms            mouse
       │                 │                   │
       ▼                 ▼                   ▼
  Explorer keeps   App enters            Collection is
  its native       MERGE_ARMED state,    created; A and B
  drag/drop        shows preview         are hidden and
  (quick drops     animation             replaced by a
  still work)                            single tile
```

1. A low-level mouse hook watches drag gestures **only** when the Desktop shell is in the foreground — other applications are never affected.
2. **Quick drops** (< 280 ms) pass straight through to Windows; the app does not interfere.
3. **Holding** over a target icon for ~280 ms triggers `MERGE_ARMED`: the app shows a preview overlay while `WM_MOUSEMOVE` keeps flowing to Explorer so the cursor and drag image stay smooth.
4. On mouse-up, the app sends `Escape` + `WM_CANCELMODE` to cancel Explorer's OLE drag, swallows the release, and commits exactly one collection operation.
5. Moving away from the target **at any point** cancels the armed state and returns full control to Windows.

## Features

| Feature | Detail |
|---|---|
| **Virtual collections** | Groups are metadata in `%APPDATA%\DesktopFolders\virtual-layout.json` — not physical folders. Original file paths and attributes are preserved and restored on removal. |
| **Collection popup** | Dark-themed panel (Fluent/Slate design) with search, grid/list toggle, pin favorites, inline rename (click title or F2), drag items in/out. |
| **Compact & Expanded** | Compact 440×520, Expanded 840×600. Always keeps 3 cards per row; centered within viewport. |
| **Smart anchoring** | Popup positions itself below/above/beside the tile with 10 px gap, avoids overlapping other open panels, and follows the tile when the Desktop rearranges icons. |
| **Cross-collection drag** | Drag an item from one open collection directly into another without losing hidden state or attributes. |
| **Auto-dissolve** | When a group has only one item left, it automatically dissolves back to a standalone icon (configurable). |
| **Composite tile icon** | Each collection tile shows a 3×3 grid of up to 9 member icons — generated as a PNG-backed 256×256 ICO, not a generic folder icon. |
| **System tray lifecycle** | No taskbar window. Runs via `ApplicationContext` + `NotifyIcon`. Supports Backup/Restore of the virtual layout (`.desktopfolders` files) and optional Windows startup. |
| **Keyboard accessible** | Tab focus ring, Enter/Space to open, F2 to rename, Ctrl+F to search, Escape to close. Cards and toolbar buttons have accessible names. |
| **Animation** | Open/close/expand morph overlays using Windows Composition (or graceful GDI fallback). `ReduceMotion` setting disables all animation. |

## Data safety

DesktopFolders **never** creates real folders or moves files. When an item enters a collection:

1. The app saves the file's original `FileAttributes`.
2. It sets the `Hidden` flag so Explorer no longer shows the standalone icon.
3. When the item is dragged out or restored, the original attributes are written back and `SHChangeNotify` makes the icon reappear instantly — no manual Desktop refresh needed.

**Tray → "Khôi phục toàn bộ icon rồi Exit"** unhides every member, deletes all collection tiles and their generated icons, and exits cleanly.

## Performance

- The mouse hook only fires when the Desktop Explorer (`Progman` → `SHELLDLL_DefView` → `SysListView32`) is in the foreground.
- UI Automation scans run every 1.2 s while the Desktop is active; when another app is in the foreground, the timer increments an inactivity counter and after ~12 s calls `EmptyWorkingSet` to release unused memory.
- Desktop cache refresh after a quick Windows drop uses a dedicated 450 ms timer so icon positions stay accurate without continuous lag.
- Path notifications use per-file `SHCNE_ATTRIBUTES` + `SHCNE_UPDATEITEM` — never `SHCNE_ASSOCCHANGED` or `UPDATEDIR` broadcasts — so unrelated icons are not disturbed.
- Measured on the dev machine: **0.0 s CPU** over 8 s when Desktop is not in the foreground; private bytes ~38 MB, resident working set drops to ~4 MB after idle trim.

## Installation

1. Download [`DesktopFolders.exe`](DesktopFolders.exe) from this repository.
2. Run the EXE — it appears in the system tray.
3. Drag one Desktop icon onto another and hold ~280 ms to create your first collection.

> [!NOTE]
> The application is not code-signed. Windows SmartScreen may show a warning for files downloaded from the Internet.

## Settings

Right-click the tray icon → **Settings**, or double-click the tray icon.

| Setting | Default | Description |
|---|---|---|
| Start with Windows | Off | Adds/removes `HKCU\...\Run\DesktopFolders` |
| Reduce motion | Off | Disables all open/close/expand animations |
| Auto-dissolve single-item groups | On | Automatically restores the last remaining item to the Desktop |
| Hover delay (ms) | 280 | Time to hold over a target before `MERGE_ARMED` (120–800 ms) |

## Build from source

**Requirements:** Windows with .NET Framework 4.x (`csc.exe`, WinForms, `UIAutomationClient`, `UIAutomationTypes`, `WindowsBase`).

```powershell
# Release build
.\build.ps1

# Diagnostic build (writes drag-diagnostic.log to %APPDATA%\DesktopFolders)
.\build.ps1 -TraceDrag -Output DesktopFolders-test.exe

# Regenerate the flat project icon from geometry (System.Drawing, no external assets)
.\generate-icon.ps1
```

The build script auto-detects Windows Composition assemblies (Windows SDK) and enables hardware-accelerated open/expand animations when available. On systems without the SDK, the build succeeds and falls back to GDI morph overlays.

## Architecture

The entire application is a single C# file compiled to a WinForms executable with no third-party dependencies.

```
DirectDesktopFolders.cs          Entire application source (~2 000 lines)
├─ Program                       Entry point, single-instance mutex, --open-group CLI
├─ DirectDesktopController       ApplicationContext: tray, mouse hook, drag state machine,
│                                Desktop scan timer, IPC command window
├─ FolderPanel                   Collection popup form: UI, drag-in/out, rename, search,
│                                cross-collection transfer, dissolve
├─ FolderPreviewOverlay          MERGE_ARMED visual preview (transparent overlay)
├─ WindowMorphOverlay            GDI open/close/expand snapshot animation
├─ CompositionMotionOverlay      Windows.UI.Composition accelerated motion cue
├─ AppTile                       Single item card (grid & list mode, pin indicator)
├─ GroupTileFactory               Generates .lnk tile + composite 256×256 PNG ICO
├─ ExplorerDesktop               SysListView32 discovery, UI Automation scan, hit testing
├─ DataStore                     JSON persistence (virtual-layout, settings, drag log)
├─ IconLoader                    Cached icon extraction (Icon.ExtractAssociatedIcon)
├─ CollectionTheme               Dark-slate color palette & corner radius constants
├─ SettingsForm                  Settings dialog
└─ Native                       Win32 P/Invoke declarations
```

## Repository contents

```
DesktopFolders.exe               Release binary
DesktopFolders-source.zip        Source code archive
DesktopFolders-icon.png          App icon (256×256 PNG)
README.md                       This file (English)
README-DesktopFolders.txt        Detailed documentation (Vietnamese)
```

## Current limitations

- Only Desktop items backed by a file path (`.lnk`, `.url`, `.exe`, `.appref-ms`, etc.) can be grouped. Virtual system icons like Recycle Bin are not supported.
- Collection tiles are standard `.lnk` shortcuts. Nesting one collection inside another is intentionally disabled.
- Windows-only; depends on the classic Desktop Explorer shell (`SysListView32`).

## IPC & single instance

The app uses a named `Mutex` for single-instance enforcement. When a second process launches with `--open-group <id>`, it sends the group ID via `WM_COPYDATA` to the running instance's hidden command window. If the command window is not yet ready (early startup), a file-based fallback writes a `.request` file that the main process polls. Open requests are coalesced by group ID so rapid duplicate launches never create duplicate popups.
