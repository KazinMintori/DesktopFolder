# AGENTS.md — DesktopFolders

This file is the canonical entry point for AI coding agents working on this repository.

## 1. Project goal

DesktopFolders is a Windows desktop utility that creates iPhone-style virtual collections on the Windows Desktop. It must preserve native Explorer behavior as much as possible: quick drag/drop must remain native, collection grouping is armed only after a hover delay, files are never physically moved into folders, and collection state is stored as metadata.

## 2. Repository shape

The codebase is intentionally compact and unusual:

- `DirectDesktopFolders.cs` — almost the entire application in one large C# source file.
- `build.ps1` — reproducible .NET Framework build.
- `generate-icon.ps1` — icon generation.
- `CollectionBackground.png` — embedded collection-panel artwork.
- `DesktopFolders.ico` / `DesktopFolders.png` — app assets.
- `README.md` — current public architecture and feature documentation.
- `README-DesktopFolders.txt` — Vietnamese documentation.
- `release/` and release binaries — outputs, not primary implementation sources.

Because most logic is concentrated in one file, agents MUST avoid broad, opportunistic refactors during feature or bug-fix tasks.

## 3. Major subsystems inside `DirectDesktopFolders.cs`

Use the README architecture map as the first orientation source. Important logical areas include:

- `Program` — entry point, single-instance behavior, command-line routing.
- `DirectDesktopController` — application lifecycle, tray, desktop scan, pointer monitoring, drag/merge state machine, IPC handling.
- `FolderPanel` — collection popup UI, search, rename, grid/list, reordering, nested groups, transfer and dissolve behavior.
- `FolderPreviewOverlay` — merge-armed visual preview.
- `WindowMorphOverlay` / `CompositionMotionOverlay` — animation paths.
- `AppTile` — collection item card.
- `GroupTileFactory` — collection shortcut/composite icon generation.
- `ExplorerDesktop` — Desktop discovery, UI Automation scan, hit testing, Shell interaction.
- `DataStore` — settings/layout persistence and drag diagnostics.
- `VirtualLayoutGraph` — nested-group graph normalization/cycle protection.
- `IconLoader` — icon extraction/cache.
- `SettingsForm` — settings UI.
- `Native` — Win32 P/Invoke declarations.

## 4. Non-negotiable behavioral constraints

Unless a task explicitly changes them, preserve these invariants:

1. Do not physically move user files into real folders.
2. Preserve original file attributes when items enter/leave virtual collections.
3. Quick Desktop drops must continue to pass through to Explorer.
4. Do not introduce a global mouse hook unless the user explicitly approves an architectural change.
5. Do not inject broad/global input to cancel a drag when a targeted Shell/window mechanism is available.
6. Avoid Explorer process-memory manipulation.
7. Do not create cyclic nested collections.
8. Preserve single-instance behavior and `--open-group` routing.
9. Preserve `ReduceMotion` behavior and graceful animation fallback.
10. Do not replace Shell positioning with fragile coordinate hacks when supported Shell APIs are already used.
11. Do not modify unrelated UI/drag/persistence behavior in the same patch.
12. Never delete tests/diagnostics merely to make a build pass.

## 5. Build and diagnostics

Release build:

```powershell
.\build.ps1
```

Drag diagnostic build:

```powershell
.\build.ps1 -TraceDrag -Output DesktopFolders-test.exe
```

Optional test-symbol build:

```powershell
.\build.ps1 -TestBuild -Output DesktopFolders-test.exe
```

The build uses the .NET Framework compiler at `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`, WinForms/UIAutomation references, and conditionally enables Windows Composition when the required Windows SDK assemblies are found.

When debugging drag behavior, prefer a `-TraceDrag` build and inspect `%APPDATA%\DesktopFolders\drag-diagnostic.log` before changing the state machine.

## 6. Local-first agent workflow

Both Codex and Antigravity may operate directly on the local checkout. Use that capability instead of pasting large portions of the repository into chat.

Before editing:

1. Read this file.
2. Read `README.md`.
3. Read `docs/ai/PROJECT_COMPLETION_PLAYBOOK.md`.
4. Read `docs/ai/CURRENT_STATE.md`.
5. Inspect only the subsystem relevant to the current task.
6. Build a hypothesis and a small file/symbol scope before editing.
7. Prefer a minimal patch.
8. Build locally.
9. For drag/Explorer behavior, run a diagnostic build and reproduce manually when needed.
10. Review the diff for unrelated changes.

## 7. Model routing policy

Model availability changes quickly. Check the current product model picker before starting a large task.

Current verified routing baseline (2026-09-05):

### Antigravity Pro

- **Gemini 3.8 Flash Medium/High** — default local repo exploration, broad code reading, dependency mapping, first-pass implementation for bounded tasks.
- **Gemini 3.1 Pro High** — escalate for architecture/business-logic reasoning where a broad Flash scan is not enough.
- **Claude Sonnet 4.6 (Thinking)** — alternative local implementation/review path inside Antigravity for model diversity.
- **Claude Opus 4.6 (Thinking)** — reserve for unusually difficult reasoning if quota justifies it.

### Codex

- **GPT-5.6 Luna** — mechanical/low-risk tasks.
- **GPT-5.6 Terra** — default Codex implementation model for ordinary bounded fixes/features.
- **GPT-5.6 Sol** — hard multi-subsystem debugging, difficult refactors, subtle Windows/Shell/interop behavior.

### Native Claude Free

- **Claude Sonnet 5** — independent diff/spec/architecture review when available on the user's Free plan; especially valuable before spending another Codex pass.
- **Haiku 4.5** — lightweight review/extraction if it is actually selectable/fallback-available in the current account.

Do not choose a model only because it is newer or labeled Pro. Choose by task, harness, quota and expected rework.

## 8. Codex quota protection

Codex should not spend tokens rediscovering the whole project.

Before a substantial Codex task, prepare a compact task packet containing:

- Goal
- Relevant subsystem/symbols
- Current behavior
- Expected behavior
- Constraints/invariants
- Reproduction steps or diagnostic evidence
- Suggested hypothesis (if known)
- Exact build/test/verification commands
- Definition of done

Do NOT include long chat history, rejected ideas, unrelated files, or full documentation dumps.

For normal work, try `Terra` first. Escalate to `Sol` only when there is a concrete reason.

## 9. Change discipline for this repository

Because `DirectDesktopFolders.cs` is a monolithic file:

- Keep patches narrow.
- Avoid formatting the whole file.
- Avoid renaming unrelated symbols.
- Do not reorganize classes while fixing a behavior bug.
- If structural decomposition is desired, propose it as a separate refactor milestone with regression protection first.
- For state-machine changes, document old state → event → new state and verify cancellation/cleanup paths.
- For persistence changes, preserve compatibility with existing `%APPDATA%\DesktopFolders\virtual-layout.json` unless an explicit migration is included.

## 10. Definition of done

A task is not complete merely because code compiles.

Minimum completion criteria:

- Patch addresses the requested behavior only.
- Release build succeeds.
- Relevant manual reproduction succeeds.
- Diagnostic logs show no new obvious error loop for affected drag/IPC behavior.
- Existing invariants are preserved.
- Diff contains no unrelated refactor.
- Documentation/current-state notes are updated when architecture or user-visible behavior changes.

See `docs/ai/PROJECT_COMPLETION_PLAYBOOK.md` for the full project-completion workflow.
