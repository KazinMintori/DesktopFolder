# Architecture

This document is orientation only. `src/DesktopFolders.cs` and the build scripts are authoritative.

## Runtime shape

- **VERIFIED**: `Program` owns single-instance startup and routes activation or `--open-group` commands through `SingleInstanceCommandWindow`.
- **VERIFIED**: `DirectDesktopController` is the tray `ApplicationContext`, desktop scanner, pointer monitor, merge state machine, and panel coordinator.
- **VERIFIED**: `FolderPanel` implements collection search, grid/list display, pinning, reorder, nesting, transfer, restore, dissolve, and temporary background dragging.
- **VERIFIED**: Startup validates every collection Shell Link target against the current executable path and rewrites stale links. Shell Link RCWs are released after both reads and writes.
- **VERIFIED**: The app is one WinForms executable with embedded artwork and localization resources. It has no third-party runtime dependencies.

## Persistence and safety

- **VERIFIED**: `DataStore` writes settings and `virtual-layout.json` below `%APPDATA%\DesktopFolders` using a temporary file followed by replace/move.
- **VERIFIED**: Filesystem members retain `Path` and `OriginalAttributes`; adding a member sets only the Hidden flag and restoration reapplies the original attributes.
- **VERIFIED**: Layout schema version 4 adds `ShellIdentity` and `OriginalShellVisibility`. Older layouts remain readable because these fields are additive and filesystem-member fields are unchanged.
- **VERIFIED**: `VirtualLayoutGraph.Normalize` and `WouldCreateCycle` preserve valid nested-group relationships and reject cyclic containment.

## Windows Shell integration

- **VERIFIED**: `ExplorerDesktop` discovers the classic Desktop `SysListView32`, scans items through UI Automation, and uses `IFolderView::SelectAndPositionItems` for collision-free placement.
- **VERIFIED**: Recycle Bin, This PC, and Network use stable `KNOWNFOLDERID` strings as persistent identities. Raw PIDL pointers are never serialized.
- **VERIFIED**: `ShellDesktopItems` resolves a runtime PIDL for each operation, releases it with `ILFree`, releases extracted icon handles with `DestroyIcon`, and releases COM objects in `finally` blocks.
- **VERIFIED**: Virtual Shell members use Shell operations for visibility, icon extraction, and opening; filesystem APIs are used only for file-backed members.
- **VERIFIED**: Each Shell member records whether its icon was originally visible and restores that state on removal or dissolve.
- **VERIFIED**: **Prepare uninstall** restores every available non-group member before committing an empty layout, removes generated tiles/data and per-user registration, then exits. If restoration or the empty-layout commit fails, restored members are re-hidden and the original layout is retained.

## Interaction and UI

- **VERIFIED**: Quick Explorer drops remain untouched until the configurable hover threshold is reached. Armed merges use targeted desktop-window cancellation; there is no global mouse hook or Explorer injection.
- **VERIFIED**: The collection header uses one softly tinted translucent 140×44 feature group with the same continuous blue–purple perimeter as search, containing four 32×36 custom-painted buttons. Selected and keyboard-focused actions use glyph color plus a small underline rather than a square outline. Closing is immediate; compact/expanded resizing updates the collection directly without creating a topmost composition host window. Grid/list selection, expanded state, close, hover, press, focus, and disabled rendering are explicit.
- **VERIFIED**: The search control has fixed compact proportions and a continuous blue/purple rounded perimeter drawn from non-overlapping segments to avoid color seams.
- **VERIFIED**: `ReduceMotion` and terminal-server sessions use the reduced-motion path.
- **VERIFIED**: Item move-out feedback uses the app-owned GDI overlay; collection close and compact/expanded resize are immediate and create no auxiliary window.

## Known boundaries

- **VERIFIED**: Pathless namespace objects other than the three explicitly supported system icons are ignored.
- **VERIFIED**: Changing Hidden attributes on Public Desktop files can fail without sufficient permissions; errors are caught and the collection operation does not complete.
- **UNKNOWN**: Third-party Explorer replacements and future Windows Shell changes may not expose the expected Desktop list view or COM behavior.
