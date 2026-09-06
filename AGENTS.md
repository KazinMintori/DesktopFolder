# AGENTS.md — DesktopFolders

This file is the canonical entry point and rulebook for AI coding agents working on the `DesktopFolders` codebase.

---

## 1. Project Overview & Core Mission

**DesktopFolders** is a lightweight Windows utility providing iOS/macOS-style virtual app grouping directly on the classic Windows Desktop (`SysListView32`).
- Users drag one desktop icon onto another and hold for ~280 ms (`FolderHoverDelay`) to merge them into a virtual collection.
- **Safety Guarantee**: Files are **never moved into real directories** and **never deleted**. Members receive the `FileAttributes.Hidden` flag, while original paths and attributes are preserved in `%APPDATA%\DesktopFolders\virtual-layout.json`.
- A representative desktop shortcut (`.lnk`) is created with an auto-generated composite 256×256 PNG-backed `.ico` displaying a 3×3 thumbnail grid of members.
- Clicking the tile opens a modern, dark-themed cosmic glass popup panel (`FolderPanel`) supporting instant search, grid/list toggle, pinning, drag reordering, nested sub-collections, and drag-to-desktop restoration.

---

## 2. Repository Shape & Architecture Constraints

The codebase has an unusual monolithic structure:
- `DirectDesktopFolders.cs`: **Contains the entire application (~3,518 lines)**. All models, Win32 P/Invoke, COM interfaces, UI controls, drag monitor, and entry point live in this single file.
- `build.ps1`: Reproducible build script invoking the .NET Framework 4.x C# compiler (`csc.exe`).
- `generate-icon.ps1`: Re-generates `DesktopFolders.ico` from canonical `DesktopFolders.png`.
- `CollectionBackground.Cosmic.png`: Required embedded resource (dark blue–indigo–lavender cosmic background artwork). It is embedded under the stable manifest name `DesktopFolders.CollectionBackground.png`.
- `DesktopFolders.ico` / `DesktopFolders.png`: Application branding assets.
- `docs/ai/`:
  - `ARCHITECTURE.md` — In-depth architectural decomposition.
  - `REPO_MAP.md` — Component map and dependency guide.
  - `CURRENT_STATE.md` — Current working baseline, verified capabilities, and technical risks.

> [!CAUTION]
> Because almost all implementation logic is concentrated in `DirectDesktopFolders.cs`, agents **MUST NOT** perform broad, opportunistic refactoring, bulk reformatting, or mass renaming while fixing bugs or implementing individual features.

---

## 3. The 12 Non-Negotiable Invariants

Unless explicitly instructed by the user, every agent must maintain these 12 invariants:

1. **No Physical File Moves**: Never move user files into real folders or subdirectories on disk.
2. **Attribute Integrity**: Always preserve and restore original `FileAttributes` when items enter or exit collections.
3. **Explorer Quick-Drop Pass-Through**: Quick drops (< 280 ms) must remain natively handled by Windows Explorer without interception.
4. **No Global Mouse Hooks**: Do not install global WH_MOUSE_LL hooks; pointer tracking is strictly read-only via timer polling.
5. **Targeted Drag Cancellation**: Do not inject global keyboard/mouse inputs to cancel OLE drag. Send `WM_CANCELMODE` and `VK_ESCAPE` directly to the `SysListView32` desktop window.
6. **No Explorer Process Injection**: Never open process handles with write access or allocate memory inside `explorer.exe`.
7. **No Cyclic Nesting**: Enforce graph cycle protection in `VirtualLayoutGraph` to prevent recursive containment loops (A → B → A).
8. **Single-Instance & IPC Integrity**: Preserve named Mutex enforcement, `WM_COPYDATA` command reception, and `--open-group` CLI routing.
9. **Motion & Remote Desktop Awareness**: Honor the `ReduceMotion` setting and terminal server session detection (`SystemInformation.TerminalServerSession`) with graceful fallback.
10. **Supported Shell APIs for Grid Placement**: Use COM `IFolderView::SelectAndPositionItems` for collision-free placement rather than hardcoded desktop pixel coordinates.
11. **Narrow Scope & Surgical Patches**: Touch only the symbols and methods directly related to the current task.
12. **Preserve Test & Diagnostic Code**: Never delete diagnostic log paths or test build hooks (`#if TEST`, `#if TRACE_DRAG`) to force a build to pass.

---

## 4. Epistemic Standards

When discussing or documenting architecture, code behavior, or bugs:
- Mark claims as:
  - **`VERIFIED`**: Directly confirmed from code (with file and line references).
  - **`INFERRED`**: Strongly supported by circumstantial code evidence, but not directly proven.
  - **`UNKNOWN`**: Requires active runtime investigation or user clarification.
- **Never state assumptions as facts.**

---

## 5. Build, Run, and Diagnostic Commands

### Build Commands (PowerShell)
```powershell
# Standard release build -> DesktopFolders.exe
.\build.ps1

# Diagnostic trace build (writes to %APPDATA%\DesktopFolders\drag-diagnostic.log)
.\build.ps1 -TraceDrag -Output DesktopFolders-test.exe

# Test build (enables CLI verification switches)
.\build.ps1 -TestBuild -Output DesktopFolders-test.exe

# Regenerate application icon
.\generate-icon.ps1
```

### Run Commands
```powershell
# Normal launch (opens Settings dialog, creates system tray icon)
.\DesktopFolders.exe

# Background startup launch (silent, no initial window)
.\DesktopFolders.exe --startup

# Open a specific group by ID or shortcut path
.\DesktopFolders.exe --open-group "<group-id-or-path>"
```

### Verification Test Commands (Built with `-TestBuild`)
```powershell
# Validate VirtualLayoutGraph normalization and cycle prevention (exit 0 = pass)
.\DesktopFolders-test.exe --test-layout-graph

# Test Shell desktop free slot placement
.\DesktopFolders-test.exe --test-place "<file-path>" <preferredX> <preferredY>
```

---

## 6. Local-First Agent Workflow

Agents operating in Antigravity or Codex have direct access to the workspace.

### Step-by-Step Task Routine:
1. **Read**: Review `AGENTS.md`, `docs/ai/ARCHITECTURE.md`, `docs/ai/REPO_MAP.md`, and `docs/ai/CURRENT_STATE.md`.
2. **Isolate**: Inspect only the relevant subsystem inside `DirectDesktopFolders.cs`.
3. **Hypothesize**: Formulate a concrete hypothesis and minimal symbol scope before editing.
4. **Patch**: Apply surgical, targeted edits. Keep formatting identical to existing code style.
5. **Compile**: Run `.\build.ps1` to ensure syntax and type-check validity.
6. **Verify**: Run `.\build.ps1 -TraceDrag`, test manually or inspect diagnostic output where relevant.
7. **Diff Check**: Ensure `git diff` contains no accidental formatting or unrelated changes.

---

## 7. Model Routing Policy & Token Efficiency

- **Gemini 3.8 Flash (High)**: Default for local codebase exploration, broad file mapping, initial bounded code edits.
- **Gemini 3.1 Pro / Codex Terra / Sol**: For complex state machine debugging, COM shell interop, multi-subsystem refactors, and graph synchronization.
- **Claude Sonnet 5 (Free/Review)**: For independent diff auditing and sanity-checking before committing.

### Task Packet Protocol (for Subagents & External LLMs)
Never dump the entire 3,500-line file or long chat histories into prompt contexts. Provide a concise **Task Packet**:
- **Goal**: Exactly what bug to fix or feature to add.
- **Subsystem & Line Range**: Specific class and lines in `DirectDesktopFolders.cs`.
- **Current vs Expected Behavior**: Observed failure and expected outcome.
- **Invariants**: Any of the 12 rules specifically touched.
- **Verification Command**: Exact build and test command to run.

---

## 8. Definition of Done (DoD)

A task is complete only when:
1. The requested change is implemented cleanly with minimal line changes.
2. `.\build.ps1` succeeds with zero errors and zero warnings.
3. Relevant regression matrix cases (drag-to-merge, quick-drop, cancel, open, restore) pass.
4. No regressions are introduced into the 12 non-negotiable invariants.
5. `git diff` shows no unintended whitespace or unrelated edits.
6. Documentation in `docs/ai/` is updated if architectural behavior changed.
