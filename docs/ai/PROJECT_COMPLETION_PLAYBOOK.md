# Project Completion Playbook — DesktopFolders

This playbook is the recommended end-to-end workflow for finishing DesktopFolders using a local checkout plus GitHub, with Antigravity and Codex both allowed to operate directly on the repository.

The optimization target is not simply “use the strongest model.” It is:

> Finish the project with the fewest expensive Codex passes while preserving correctness, data safety, Explorer compatibility, and UX quality.

---

## 0. Operating model

Use one local repository as the single source of truth.

Recommended setup:

```text
DesktopFolder/
├─ .git/
├─ AGENTS.md
├─ DirectDesktopFolders.cs
├─ build.ps1
├─ README.md
├─ docs/ai/
│  ├─ CURRENT_STATE.md
│  └─ PROJECT_COMPLETION_PLAYBOOK.md
└─ ...
```

Both Antigravity and Codex should open the SAME local checkout or separate Git worktrees/branches of the same repository. Do not manually copy code between IDEs.

For concurrent experimentation, prefer Git worktrees or separate branches so two agents do not overwrite the same large C# file.

Example:

```powershell
git switch main
git pull

git worktree add ..\DesktopFolder-antigravity -b ai/antigravity-task
git worktree add ..\DesktopFolder-codex -b ai/codex-task
```

Do not let two agents edit `DirectDesktopFolders.cs` concurrently on the same working tree.

---

# PHASE 1 — Establish a stable baseline

## Step 1.1 — Sync and checkpoint

On the primary local checkout:

```powershell
git status
git switch main
git pull
.\build.ps1
```

If local uncommitted work exists, commit it to a named checkpoint branch before AI work.

Example:

```powershell
git switch -c checkpoint/pre-completion
# inspect carefully
git add -A
git commit -m "checkpoint: preserve current DesktopFolders state"
```

Why:
- every agent starts from a known state;
- bad AI edits are reversible;
- regressions can be bisected.

## Step 1.2 — Record the exact baseline

Record:
- commit SHA;
- whether release build passes;
- whether diagnostic build passes;
- current known visible defects;
- Windows version / display scaling for UI bugs;
- whether Windows Composition path is enabled on the machine.

Do not begin a broad feature pass until the baseline build is known.

---

# PHASE 2 — Build a reliable project understanding

This is where Antigravity should carry most of the broad exploration load instead of Codex.

## Step 2.1 — Antigravity / Gemini 3.8 Flash High: broad repo scan

Open the local repo in Antigravity.

Use Gemini 3.8 Flash High for the first exploration pass.

Why this model:
- repository reading is broad, high-volume work;
- it is strong at software-engineering context and tool use;
- it prevents Codex from spending quota rediscovering repository structure;
- most of the repository is one large source file, so fast symbol mapping has high value.

Ask it to produce, WITHOUT editing production code:

1. symbol map for `DirectDesktopFolders.cs`;
2. drag/merge state-machine map;
3. persistence/data-safety map;
4. nested-group graph operations;
5. UI/animation path map;
6. IPC/single-instance path;
7. Shell/UI Automation interaction map;
8. list of likely regression hot spots;
9. list of behavior that cannot be validated statically and requires manual Windows testing.

Recommended instruction:

```text
Read AGENTS.md and README.md first.
Do not edit production code.
Map the repository and DirectDesktopFolders.cs by subsystem and symbol.
For every important behavior, identify the owning methods/classes and the invariants that must be preserved.
Mark uncertain claims as UNKNOWN instead of guessing.
Return a concise handoff suitable for a later implementation agent.
```

## Step 2.2 — Gemini 3.1 Pro High only for deep architecture questions

Do NOT automatically rerun the entire repository through 3.1 Pro.

Use 3.1 Pro High selectively when 3.8 Flash finds a genuinely difficult reasoning question, such as:

- conflicting ownership of drag state;
- subtle cancellation race;
- unclear lifetime between collection windows and controller;
- nested graph mutation with multiple parent effects;
- need to decide whether a subsystem boundary should be refactored.

Why:
3.1 Pro should be used for “why is the system behaving this way?” rather than spending its deeper reasoning on repository inventory.

## Step 2.3 — Update AI documentation

After exploration, update only factual documentation:

- `docs/ai/CURRENT_STATE.md`
- optionally add `docs/ai/REPO_MAP.md`
- optionally add `docs/ai/ARCHITECTURE.md`

Do not let generated documentation claim a feature is proven working merely because code exists.

---

# PHASE 3 — Build the project-completion backlog

Do not ask one agent to “finish the whole project.” Convert the remaining work into bounded tasks.

## Step 3.1 — Inventory defects and unfinished behavior

Sources:

- your own observed defects;
- README claims that fail manual verification;
- drag diagnostic logs;
- build warnings/errors;
- UI problems;
- recent commit regression areas;
- GitHub issues if present.

Create backlog items with this structure:

```text
Title
Severity: blocker / high / medium / polish
Subsystem
Reproduction
Current behavior
Expected behavior
Likely symbols/files
Safety constraints
Verification
```

## Step 3.2 — Prioritize in this order

### P0 — data safety / destructive risk

Examples:
- hidden files not restored;
- layout corruption;
- group cycle corruption;
- wrong attributes restored.

### P1 — core interaction correctness

Examples:
- native quick drop broken;
- hold-to-merge fails;
- duplicate group creation;
- drag cancellation fails;
- cross-collection moves lose state.

### P2 — lifecycle / IPC / persistence

Examples:
- second launch does not activate;
- `--open-group` fails;
- restart loses state;
- auto-dissolve breaks.

### P3 — UX / animation / layout

Examples:
- popup positioning;
- icon-grid reorder feel;
- animation jerk;
- reduced-motion inconsistencies.

### P4 — polish

Examples:
- microcopy;
- spacing;
- cosmetic gradients;
- non-critical visual refinements.

Do not spend Codex Sol quota on P4 work unless the implementation is unexpectedly difficult.

---

# PHASE 4 — Route each task to the right model and harness

## 4.1 Cheap local implementation path: Antigravity first

Use Antigravity + Gemini 3.8 Flash High for:

- bounded UI changes;
- small drag-state fixes with clear reproduction;
- settings changes;
- simple persistence fixes;
- localized refactors;
- logging/diagnostic additions;
- straightforward performance cleanup.

Workflow:

```text
AGENTS.md
+ task packet
+ local repo
    ↓
Antigravity 3.8 Flash High
    ↓
inspect → edit → build → diff
```

If it succeeds cleanly, there may be no need to spend Codex quota.

## 4.2 Use Antigravity Sonnet 4.6 Thinking for diversity

Good uses:
- second implementation proposal;
- review of a Gemini patch;
- subtle state-machine reasoning;
- UI logic where a different model perspective helps.

Do not run Sonnet 4.6 simply because it sounds stronger than Flash; use it when diversity or reasoning depth is useful.

## 4.3 Codex Terra: default high-value execution path

Use Codex Terra when:

- Antigravity failed or produced questionable code;
- the task needs a more reliable repo-edit/test loop;
- multiple adjacent methods must be changed coherently;
- Windows/interop behavior needs careful implementation;
- the patch is important enough that expected rework dominates token savings.

Codex should receive a NARROW packet, not “read the whole project.”

Example:

```text
Goal:
Fix duplicate group commit when mouse-up happens immediately after MERGE_ARMED.

Relevant subsystem:
DirectDesktopController drag/merge state machine.

Evidence:
TraceDrag log attached / exact reproduction steps.

Constraints:
- quick Explorer drops must remain native;
- do not add a global hook;
- commit at most one collection operation;
- moving away after arm must cancel cleanly.

Verification:
1. release build;
2. TraceDrag build;
3. quick drop;
4. hold-to-merge;
5. move-away cancellation;
6. repeat 20 times without duplicate commit.
```

## 4.4 Codex Sol: escalation, not default

Use Sol when one or more are true:

- Terra has already failed for a reasoning reason;
- race/state ownership spans multiple subsystems;
- Win32/Shell/COM behavior is subtle;
- proposed refactor carries high regression risk;
- bug is intermittent and requires evidence synthesis across logs and code;
- a wrong patch could hide files, corrupt layout, or break native Explorer behavior.

Use higher reasoning only after the problem has been narrowed.

Never burn Sol tokens on broad repository orientation if Antigravity/Gemini can do it first.

---

# PHASE 5 — The task packet protocol

Every implementation task should have a task packet committed to an issue, temporary note, or supplied directly to the local agent.

Template:

```markdown
# Goal

# Severity

# Relevant subsystem / symbols

# Current behavior

# Expected behavior

# Exact reproduction

# Evidence

# Constraints / invariants

# Suggested hypothesis

# Build / diagnostics

# Verification matrix

# Definition of done
```

The packet is the main Codex token-saving device.

Before sending a task to Codex, use ChatGPT/Gemini/Claude to reduce noisy context into this packet.

---

# PHASE 6 — Implementation discipline

## Step 6.1 — One behavioral concern per branch

Examples:

```text
fix/drag-cancel-after-arm
fix/ipc-early-startup
ui/popup-anchor-regression
perf/icon-cache
```

Avoid mixing:
- drag fix;
- visual redesign;
- persistence migration;
- source-file decomposition

in one branch.

## Step 6.2 — Inspect before edit

The local agent should:

1. locate exact symbols;
2. trace callers/callees;
3. identify cleanup paths;
4. identify persisted state touched;
5. identify fallback paths;
6. state intended patch scope;
7. only then edit.

## Step 6.3 — Build immediately after a small coherent patch

```powershell
.\build.ps1
```

For drag work:

```powershell
.\build.ps1 -TraceDrag -Output DesktopFolders-test.exe
```

Do not accumulate a massive patch before the first build.

## Step 6.4 — Review the diff locally

```powershell
git diff -- DirectDesktopFolders.cs
git diff --stat
```

Reject patches that:
- format unrelated code;
- rename broad sets of symbols;
- touch a second subsystem without need;
- silently alter a public README claim;
- remove fallback behavior.

---

# PHASE 7 — Independent review before a second Codex pass

This is a major quota-saving step.

After an implementation is built, review the diff using a DIFFERENT model.

Preferred order:

1. Claude Sonnet 5 Free, when currently available in the user's plan;
2. Gemini 3.8 Flash;
3. Antigravity Sonnet 4.6 Thinking if IDE-local inspection is valuable;
4. Haiku 4.5 only for lightweight checks when available.

Review prompt:

```text
Review this diff against AGENTS.md and the task packet.
Do not rewrite the solution by default.
Find only concrete issues:
- correctness;
- state cleanup;
- data safety;
- Explorer/native behavior regression;
- persistence compatibility;
- missing verification;
- unrelated changes.
Rank each issue by severity and name the exact symbol/path.
```

Why:
A free/cheaper external review can catch a specific defect before you pay for another broad Codex reasoning pass.

---

# PHASE 8 — Narrow patch loop

If review finds two issues, send ONLY those issues back to the implementation agent.

Bad:

```text
Review everything again and fix the feature.
```

Good:

```text
Patch only these findings:
1. Cancel path leaves mergeArmed=true after target disappears.
2. Existing quick-drop path now starts the preview timer.

Do not modify other behavior.
Run release + TraceDrag builds and retest the five drag cases.
```

This minimizes context reload and Codex token consumption.

---

# PHASE 9 — Regression matrix for DesktopFolders

Before declaring a core build stable, manually verify at least the following.

## A. Startup / lifecycle

- first launch opens expected UI/tray state;
- second launch activates existing instance;
- startup invocation remains unobtrusive;
- tray Settings entry works;
- exit and restart preserve virtual layout.

## B. Native Desktop drag compatibility

- quick icon-to-icon drop still belongs to Explorer;
- quick drop outside a grouping hold does not create a collection;
- pointer movement outside the Desktop does not trigger grouping logic.

## C. Hold-to-merge

- hold reaches preview at configured threshold;
- release creates exactly one collection;
- moving away before threshold cancels;
- moving away after arming cancels cleanly;
- repeated attempts do not create duplicate groups;
- hover-delay setting boundaries work.

## D. Collection data safety

- original items remain on disk;
- correct hidden state while grouped;
- remove item restores original attributes;
- dissolve restores final item;
- restart retains grouped state;
- collection deletion/restoration does not lose file paths.

## E. Collection UI

- open/close;
- compact/expanded;
- search;
- grid/list;
- rename;
- pin;
- keyboard navigation;
- F2/Ctrl+F/Escape;
- popup anchoring around screen edges;
- multiple open panels do not overlap incorrectly.

## F. Reorder / cross-collection / nesting

- direct reorder;
- move between groups;
- group an item into nested collection;
- move existing collection into another;
- cycle attempt rejected;
- parent composite icon updates;
- nested rename/path refresh works.

## G. Restore positioning

- multiple items restored into free Desktop slots;
- no accidental overlap;
- removed collection tile disappears without manual refresh.

## H. Animation

- standard animation path;
- `ReduceMotion=true`;
- fallback behavior when Composition unavailable;
- Remote Desktop/reduced path if relevant.

## I. IPC

- normal second launch;
- `--open-group <id>` when main process already running;
- early-startup fallback request;
- duplicate rapid requests do not open duplicate panels.

For a task, run the relevant subset plus a short smoke test of P0/P1 invariants.

---

# PHASE 10 — Stabilization before refactoring

Do NOT immediately split `DirectDesktopFolders.cs` merely because it is large.

First achieve:

1. stable build;
2. known regression matrix;
3. current high-severity defects closed;
4. persistence compatibility understood;
5. drag state behavior documented.

Then, if maintenance cost remains high, create a separate refactor milestone.

Candidate future decomposition boundaries:

```text
Core/
  DataStore.cs
  VirtualLayoutGraph.cs
  Models.cs
Shell/
  ExplorerDesktop.cs
  Native.cs
  GroupTileFactory.cs
UI/
  FolderPanel.cs
  AppTile.cs
  SettingsForm.cs
  Overlays.cs
Controller/
  DirectDesktopController.cs
Program.cs
```

Refactor rule:
Move code without changing behavior first. Behavior improvements come in later commits.

Because source decomposition can create a huge diff, use Codex only after a cheaper model has prepared an exact extraction plan and dependency map.

---

# PHASE 11 — Release candidate process

When P0/P1 issues are closed:

## Step 11.1
Create an RC branch/tag candidate.

```powershell
git switch -c release/rc-next
.\build.ps1
```

## Step 11.2
Run the full regression matrix.

## Step 11.3
Use Claude Sonnet 5 or Gemini 3.8 Flash to audit the final diff from the previous known-good release/commit.

Focus on:
- behavior changes not documented;
- data-safety regressions;
- dead diagnostic code;
- accidental debug symbols;
- assets/binaries out of sync with source.

## Step 11.4
Check release artifacts.

- `DesktopFolders.exe`
- mirrored `release/DesktopFolders.exe` if still part of the release process;
- icon/background assets;
- README claims;
- source archive if maintained.

## Step 11.5
Only merge after final manual Windows smoke test.

---

# PHASE 12 — GitHub workflow

Use GitHub to preserve auditability even though the agents work locally.

Recommended branch lifecycle:

```text
main
  └─ fix/<single-task>
       ├─ implementation commits
       ├─ review fixes
       └─ PR → main
```

Each PR should contain:

```markdown
## Goal

## Reproduction

## What changed

## Invariants preserved

## Verification
- [ ] Release build
- [ ] Diagnostic build if applicable
- [ ] Relevant regression cases
- [ ] Independent model review

## Risks / follow-up
```

Do not merge a broad AI-generated patch directly to `main` without diff review.

---

# PHASE 13 — Recommended model-routing matrix

This is a baseline, not a permanent model registry. Verify current availability before each major work session.

| Work | First choice | Escalation | Why |
|---|---|---|---|
| Broad local repo scan | Antigravity Gemini 3.8 Flash High | Gemini 3.1 Pro High for hard questions | Large-volume context before Codex |
| Repo/architecture map | Gemini 3.8 Flash | 3.1 Pro selective audit | Avoid expensive rediscovery |
| Small UI/polish | Antigravity 3.8 Flash | Sonnet 4.6 Thinking | No need for Codex by default |
| Straightforward bounded bug | Antigravity 3.8 Flash | Codex Terra | Try cheaper local execution first |
| Important multi-method fix | Codex Terra | Codex Sol | Better execution reliability where it matters |
| Shell/Win32/race/debug hard case | Codex Sol | strongest currently available Codex model if justified | High reasoning + local tool loop |
| Architecture decision | GPT-5.6 Sol / Gemini Pro | second-opinion Sonnet 5 | Reason outside Codex first |
| Diff review | Claude Sonnet 5 Free | Gemini 3.8 Flash | Save second Codex pass |
| Lightweight review/extraction | Haiku 4.5 if available | Gemini Flash-Lite/current cheap tier | Preserve premium quotas |

---

# PHASE 14 — What NOT to do

Do not:

- tell Codex “read the whole repo and finish the project”;
- run Sol at high reasoning for every task;
- make Antigravity and Codex edit the same monolithic file concurrently;
- accept README claims as proof that behavior works;
- mix architecture refactor and bug fix;
- let an agent rewrite a large source region merely for style;
- send an entire old conversation into Codex;
- rerun a full Codex task when review identified only one narrow defect;
- trust a generated patch that compiles but was never manually tested against Explorer interaction;
- merge AI changes directly to `main` without reviewing the diff.

---

# PHASE 15 — Practical daily loop

For each work session:

```text
1. git pull
2. choose ONE backlog item
3. create branch/worktree
4. prepare task packet
5. Antigravity 3.8 Flash attempt if bounded
6. build + manual reproduce
7. if hard/failed → narrow handoff → Codex Terra/Sol
8. build + diagnostic test
9. independent Claude/Gemini review
10. targeted patch only
11. regression subset
12. commit
13. PR
14. update CURRENT_STATE if behavior changed
```

This loop should be repeated until P0 → P1 → P2 → P3 → P4 backlog is closed.

---

# Final completion definition

DesktopFolders is “complete enough to release” only when:

- no known P0 data-safety defects remain;
- no known P1 core drag/grouping regressions remain;
- release build is reproducible;
- the regression matrix has been executed on the release candidate;
- lifecycle/IPC/persistence survive restart and repeated use;
- current README matches actual behavior;
- every remaining known limitation is documented;
- final diff has an independent review;
- `main` contains only reviewed, reproducible changes.

The project can then move from feature completion to maintenance/refactor mode.
