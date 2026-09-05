# CURRENT_STATE.md — DesktopFolders Project Status & Known Risks

**Last updated**: 2026-09-05  
**Codebase Version**: 6.0.0.0 (`DirectDesktopFolders.cs`)

---

## 1. Baseline Summary

DesktopFolders is a Windows-only WinForms desktop utility. The entire implementation is concentrated in a single monolithic C# source file: [DirectDesktopFolders.cs](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs) (~3,518 lines), compiled with .NET Framework 4.x (`csc.exe`).

Recent commits confirm active engineering across three critical areas:
1. **Drag / Pointer Monitor Refactor**: Migration away from global mouse hooks to a read-only 15 ms polling timer (`pointerMonitor`), targeted `WM_CANCELMODE`/`VK_ESCAPE` cancellation, and state machine stabilization (`DragOwnership`).
2. **Settings UI / UX Overhaul**: Modern card-based Fluent Dark theme with slider, toggles, and live Explorer diagnostic reporting.
3. **Nebula Glass Collection UI**: Embedded background artwork (`CollectionBackground.png`), true blue $\to$ purple neon gradients, responsive 3-column layout, and spring-physics reordering.

---

## 2. Verified Capabilities & Working Features

The following features have been verified directly in the codebase:

- **`VERIFIED`**: **Hold-to-Merge Gesture**: Holding an icon over a target icon for ~280 ms triggers `MergeArmed` preview; releasing commits the group without moving files ([DirectDesktopFolders.cs:3261-3294](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3261-L3294)).
- **`VERIFIED`**: **Explorer Quick Drop Pass-Through**: Quick drops (< 280 ms) remain native to Windows Explorer; the app does not interfere ([DirectDesktopFolders.cs:3265](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3265)).
- **`VERIFIED`**: **Data & Attribute Safety**: Original file paths and `FileAttributes` are stored in `virtual-layout.json`. Items are set to `Hidden`; original attributes are restored when removed ([DirectDesktopFolders.cs:3370-3378](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3370-L3378), [2225](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2225)).
- **`VERIFIED`**: **Composite 256×256 ICO**: Renders a 3×3 grid of member icons onto an anti-aliased gradient plate saved as a modern PNG-backed `.ico` ([DirectDesktopFolders.cs:795-844](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L795-L844)).
- **`VERIFIED`**: **Collision-Free Desktop Restore**: Restores standalone icons sequentially using COM `IFolderView::SelectAndPositionItems` via spiral grid search ([DirectDesktopFolders.cs:446-504](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L446-L504)).
- **`VERIFIED`**: **Collection UI Features**:
  - Compact (440×520) and Expanded (840×600) view modes with 3 cards per row ([DirectDesktopFolders.cs:1648-1649](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1648-L1649)).
  - Real-time search filtering by app name ([DirectDesktopFolders.cs:1774](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1774)).
  - Grid / List view toggle ([DirectDesktopFolders.cs:1742-1743](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1742-L1743)).
  - Pin / Unpin favorites into dedicated sections ([DirectDesktopFolders.cs:2207-2212](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2207-L2212)).
  - Direct-grab card reordering with critically-damped spring animation ([DirectDesktopFolders.cs:2142-2162](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2142-L2162)).
  - Nested collections with cycle rejection (`WouldCreateCycle`) ([DirectDesktopFolders.cs:2311](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2311), [2329](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2329)).
  - Cross-collection drag transfer between open panels ([DirectDesktopFolders.cs:2300](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2300)).
  - Configurable auto-dissolve when remaining member count $\le 1$ ([DirectDesktopFolders.cs:2227](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2227)).
  - Instant context menu with sub-item invoking native Windows Explorer Shell context menu ([DirectDesktopFolders.cs:1041-1066](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1041-L1066)).
- **`VERIFIED`**: **Lifecycle & Settings**:
  - Single-instance Mutex with `WM_COPYDATA` command window and file-based fallback ([DirectDesktopFolders.cs:3502-3512](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3502-L3512), [277-339](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L277-L339)).
  - Second launch activates Settings instead of silently terminating ([DirectDesktopFolders.cs:3510](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3510)).
  - Windows startup registration via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` with `--startup` silent flag ([DirectDesktopFolders.cs:3423](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3423)).
  - Automatic idle memory trimming (`EmptyWorkingSet`) when Desktop is inactive ([DirectDesktopFolders.cs:3128](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3128)).

---

## 3. Known Limitations & Edge Cases

- **`VERIFIED`**: **Virtual System Icons Unsupported**: Non-file system icons on Desktop (e.g. Recycle Bin, This PC, Control Panel) lack physical file paths and cannot be grouped. They are identified and logged as unsupported ([DirectDesktopFolders.cs:659](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L659), [672-674](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L672-L674)).
- **`VERIFIED`**: **Public Desktop Elevation Requirement**: If a user attempts to group an icon residing in `C:\Users\Public\Desktop` without running as Administrator, setting `FileAttributes.Hidden` throws an `UnauthorizedAccessException`. The exception is caught, but group creation fails with an error dialog ([DirectDesktopFolders.cs:3394](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3394)).
- **`VERIFIED`**: **Multi-Monitor Boundary Constraint**: Popup placement calculations (`CalculateAnchoredLocation`, `FindOpenLocation`) clamp bounds within `Screen.FromRectangle(source).WorkingArea`. If an icon is located right on a multi-monitor seam, the popup cannot span or overflow into the adjacent monitor ([DirectDesktopFolders.cs:1727](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1727), [1901](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1901)).
- **`VERIFIED`**: **Mixed Localization**: User interface labels are hardcoded in a mix of Vietnamese ("Cài đặt Desktop Folders", "Tự giải thể khi còn 1 shortcut", "Đưa ra Desktop") and English ("Desktop Folders Settings", "New Folder", "Close") without an external localization engine.

---

## 4. Architectural Risks & Code Smells

### 4.1 Monolithic Architecture
- **Status**: **`VERIFIED`**
- **Impact**: All ~3,518 lines live in [DirectDesktopFolders.cs](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs). Small edits can produce unexpected merge conflicts or accidental syntax regressions across unrelated features.
- **Mitigation Rule**: Enforce narrow patches, symbol-targeted editing, and zero unsolicited reformatting.

### 4.2 Auto-Arrange Conflict
- **Status**: **`INFERRED`**
- **Impact**: If the user has "Auto arrange icons" enabled in Windows Explorer, `IFolderView::SelectAndPositionItems` will succeed, but Explorer will immediately snap the restored icon back to the first available slot in its auto-arrange order, bypassing the collision-free spiral calculation.

### 4.3 STA Worker Thread Message Pump
- **Status**: **`INFERRED`**
- **Impact**: `ExplorerDesktop.ProcessPlacementQueue()` executes on an STA thread ([DirectDesktopFolders.cs:421](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L421)) but does not pump Windows messages. Certain Shell COM operations or cross-thread marshaling could theoretically hang if Explorer broadcasts synchronous window messages.

### 4.4 Composition Motion Overlay Fallback
- **Status**: **`VERIFIED`**
- **Impact**: Systems without Windows 10/11 SDK WinMD assemblies compile with the stub fallback `CompositionMotionOverlay` ([DirectDesktopFolders.cs:1545-1551](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1545-L1551)), which provides no hardware-accelerated motion cue. The app relies on `WindowMorphOverlay` (GDI) instead.
- **Verification**: Tested at compile-time via `#if WINDOWS_COMPOSITION`.

---

## 5. Next Stabilization Priorities

1. **Manual Regression Run**: Systematically verify the drag/merge state machine, cancel gesture, and restore placement against the regression matrix in `AGENTS.md`.
2. **Public Desktop Permission Handling**: Provide clear user feedback or graceful elevation prompt when an item in Public Desktop cannot be hidden.
3. **Auto-Arrange Detection**: Query `IFolderView::GetAutoArrange()` before positioning to avoid unnecessary spiral placement calculations when auto-arrange is active.
4. **Structural Decomposition Milestone**: Once behavioral stability is 100% locked down, consider splitting `DirectDesktopFolders.cs` into modular files (`Core/`, `Shell/`, `UI/`, `Controller/`) as a dedicated, behavior-neutral refactor.
