# Current State — DesktopFolders

Last reviewed: 2026-09-05

## Repository baseline

The current repository is a Windows-only WinForms utility whose implementation is concentrated in `DirectDesktopFolders.cs`. The public README documents virtual collections, nested groups, cross-collection drag, direct-grab reordering, smart anchoring, IPC/single-instance behavior, composite icons, settings, diagnostics, and animation paths.

Recent commits show active work in three especially sensitive areas:

1. drag/pointer-monitor behavior and merge arming;
2. collection UI/animation and Nebula glass styling;
3. IPC/single-instance launch handling.

These areas should be treated as regression-prone until a stable manual regression suite exists.

## Working baseline reported by the repository

The repository currently claims support for:

- virtual collections stored in `%APPDATA%\DesktopFolders\virtual-layout.json`;
- quick native Explorer drag/drop pass-through;
- hold-to-merge behavior around the configured hover delay;
- nested collections with cycle rejection;
- cross-collection drag;
- direct-grab reordering;
- collision-free Desktop restore through Shell APIs;
- auto-dissolve for single-item groups;
- single-instance activation and `--open-group` IPC;
- reduced-motion fallback;
- release and diagnostic builds through `build.ps1`.

These are treated as expected invariants, not automatically proven behavior. Each touched subsystem should be manually revalidated.

## Highest-priority technical risks

### 1. Monolithic source file

Most functionality lives in one large source file. A small textual change can accidentally affect adjacent logic, and broad refactors create large review diffs.

Mitigation:
- narrow patches;
- symbol-level exploration before editing;
- no opportunistic reformatting;
- separate structural refactors from behavior fixes.

### 2. Explorer drag interaction

The app intentionally tries not to break native Explorer drag/drop. Changes around hover timing, cancellation, pointer state, foreground Desktop detection, or mouse-up handling can cause severe regressions.

Mitigation:
- use `-TraceDrag` diagnostic builds;
- reproduce both quick-drop and hold-to-merge paths;
- always test cancellation by moving away;
- verify no duplicate commit of a collection operation.

### 3. Hidden-attribute and restore safety

Items entering a virtual collection are hidden and later restored with original attributes. Bugs here can make icons appear missing even if data is not deleted.

Mitigation:
- treat persistence/attribute code as safety-critical;
- test add → remove → dissolve → restart paths;
- do not alter persistence schema casually.

### 4. Nested graph integrity

Nested groups rely on IDs, tile paths and graph normalization.

Mitigation:
- preserve cycle checks;
- verify rename/update paths propagate to parents;
- test nested move, dissolve and restore operations.

### 5. Animation / composition fallback

The build conditionally enables Windows Composition and otherwise falls back to GDI.

Mitigation:
- do not assume Composition is available on every machine;
- keep reduced-motion and fallback paths working;
- separate visual changes from drag/state changes where possible.

## Missing project-completion infrastructure

Before calling the project complete, add or strengthen:

- a written manual regression matrix;
- reproducible smoke-test steps for drag, grouping, restore and IPC;
- explicit release checklist;
- issue backlog categorized by severity;
- isolated diagnostics for failures that currently require visual observation;
- optional automated tests for pure logic such as `VirtualLayoutGraph` if practical without destabilizing the current build style.

## Current project-completion target

The next phase should not be a broad rewrite. The recommended objective is:

> Stabilize the existing feature set, close observable UX/drag/IPC defects, create repeatable regression checks, then perform narrowly justified refactors only after behavior is protected.

See `PROJECT_COMPLETION_PLAYBOOK.md` for the execution sequence.
