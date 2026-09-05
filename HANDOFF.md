# HANDOFF.md — DesktopFolders Agent Handoff

**Target Audience**: Incoming AI Coding Agent (Antigravity / Codex / Claude)  
**Date**: 2026-09-05  
**Current Baseline**: v6.0.0.0 (`DirectDesktopFolders.cs`, ~3,518 lines)  
**Primary Language / Stack**: C# (.NET Framework 4.x), WinForms, Shell COM, Windows UI Automation, P/Invoke

---

## 1. Quick Orientation & Context

- **What DesktopFolders does**: Virtual iPhone/macOS-style app grouping directly on the classic Windows Desktop (`SysListView32`).
- **Core Guarantee**: **Zero physical file moves / deletions**. Files receive the `FileAttributes.Hidden` flag. Original attributes and paths are safely kept in `%APPDATA%\DesktopFolders\virtual-layout.json`.
- **Gesture Mechanism**: Dragging icon A over icon B and holding for ~280 ms (`FolderHoverDelay`) enters `MERGE_ARMED`. Targeted `WM_CANCELMODE` and `VK_ESCAPE` messages cancel Explorer's OLE drag without global hooks. Releasing commits a collection `.lnk` with a 3×3 composite `.ico`.
- **UI**: Clicking opens `FolderPanel` (dark Nebula glass popup with search, grid/list toggle, reorder, nested groups, and drag-to-desktop restore).
- **Virtual Shell members**: Recycle Bin, This PC, and Network use schema-v4 `KNOWNFOLDERID` identities. PIDLs are resolved only for the active Shell call and freed immediately; each icon's pre-collection visibility is restored on remove or dissolve.

---

## 2. Documentation Map (Read in this order)

1. [AGENTS.md](file:///C:/Users/Ai/OneDrive/Documents/DesktopFolder/AGENTS.md): **Canonical Rulebook**. Contains the 12 non-negotiable invariants, model routing, and definition of done.
2. [docs/ai/CURRENT_STATE.md](file:///C:/Users/Ai/OneDrive/Documents/DesktopFolder/docs/ai/CURRENT_STATE.md): Baseline features, known limitations, and technical risks.
3. [docs/ai/ARCHITECTURE.md](file:///C:/Users/Ai/OneDrive/Documents/DesktopFolder/docs/ai/ARCHITECTURE.md): Deep architectural breakdown of `DirectDesktopController`, `FolderPanel`, `DataStore`, and `ExplorerDesktop`.
4. [docs/ai/REPO_MAP.md](file:///C:/Users/Ai/OneDrive/Documents/DesktopFolder/docs/ai/REPO_MAP.md): Component responsibilities, method references, and subsystem boundaries.

---

## 3. Golden Rules & Invariants (Must Never Break)

1. **No Physical File Moves**: Keep user files in their original locations; only toggle `FileAttributes.Hidden`.
2. **Quick Drops Pass Through**: Drops $< 280\text{ ms}$ must remain natively handled by Windows Explorer.
3. **No Global Mouse Hooks**: Read pointer state strictly via 15 ms read-only timer polling.
4. **Targeted OLE Drag Cancel**: Send `WM_CANCELMODE` and `VK_ESCAPE` only to `SysListView32`.
5. **No Cyclic Containment**: Use `VirtualLayoutGraph.WouldCreateCycle()` to reject loops ($A \to B \to A$).
6. **Narrow Patches Only**: `DirectDesktopFolders.cs` is monolithic (~3,518 lines). Do not reformat, rename unrelated symbols, or opportunistically refactor.

---

## 4. Build, Run, and Diagnostic Cheatsheet

```powershell
# 1. Standard release build -> DesktopFolders.exe
.\build.ps1

# 2. Trace build -> logs to %APPDATA%\DesktopFolders\drag-diagnostic.log
.\build.ps1 -TraceDrag -Output DesktopFolders-test.exe

# 3. Test build -> enables CLI verification switches
.\build.ps1 -TestBuild -Output DesktopFolders-test.exe

# 4. Verify graph normalization & cycle checks (exit code 0 = pass)
.\DesktopFolders-test.exe --test-layout-graph
```

---

## 5. Current Priority Backlog for Next Agent

1. **Public Desktop Permission Handling**:
   - *Issue*: Grouping items from `C:\Users\Public\Desktop` throws `UnauthorizedAccessException` if unelevated.
   - *Task*: Add user-friendly feedback or graceful elevation prompt in `AddOrCreateGroup()`.
2. **Explorer Auto-Arrange Detection**:
   - *Issue*: When Windows Explorer has "Auto arrange icons" enabled, restored icons snap to default columns despite spiral placement calculation.
   - *Task*: Query `IFolderView::GetAutoArrange()` before placement to handle auto-arrange mode gracefully.
3. **Multi-Monitor Seam Clamping**:
   - *Issue*: `FolderPanel` clamping logic confines popup bounds strictly to `Screen.FromRectangle(source).WorkingArea`.
   - *Task*: Refine anchor calculations when a desktop icon sits near a multi-monitor boundary.
4. **Manual Regression Execution**:
   - *Task*: Perform full test run against the regression matrix in `AGENTS.md` (quick-drop, hold-to-merge, cancel-by-moving-away, open, transfer, dissolve, restore).

---

## 6. Epistemic Standards Reminder

Always classify architectural claims as:
- **`VERIFIED`**: Directly confirmed by inspecting code lines.
- **`INFERRED`**: Strongly supported by circumstantial code patterns.
- **`UNKNOWN`**: Requires active runtime investigation.
*Never state assumptions as facts.*
