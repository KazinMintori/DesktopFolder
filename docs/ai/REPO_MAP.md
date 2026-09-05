# REPO_MAP.md — DesktopFolders Component & Subsystem Map

This document maps the key components of DesktopFolders across its 4 primary functional domains.

Every important architectural claim is classified as:
- **`VERIFIED`**: Directly confirmed by inspecting the source code.
- **`INFERRED`**: Strongly inferred from patterns and design rationale.
- **`UNKNOWN`**: Requires dynamic runtime investigation or environment testing.

---

## 1. Drag / Merge Subsystem

Responsible for observing user desktop pointer actions, maintaining native Explorer drag-and-drop pass-through, detecting hold-to-merge gestures, cancelling Windows OLE drag cleanly, and displaying visual preview cues.

### 1.1 DirectDesktopController
- **File / Lines**: [DirectDesktopFolders.cs:3034–3439](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3034-L3439)
- **Primary Role**: Lifecycle orchestration, pointer monitor timer, and drag state machine.
- **Key Methods**:
  - `PollDesktopPointer()` ([line 3244](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3244)): 15 ms timer callback checking mouse state and desktop foreground status.
  - `BeginDesktopPointer()` ([line 3253](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3253)): Identifies source desktop icon on left button press.
  - `ContinueDesktopPointer()` ([line 3261](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3261)): Detects drag distance, initiates hover timer over target icons.
  - `ArmHoverTarget()` ([line 3310](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3310)): Cancels Explorer OLE drag and enters `MergeArmed`.
  - `EndDesktopPointer()` ([line 3284](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3284)): Evaluates drop condition, delegates commit or cleanup.
  - `AddOrCreateGroup()` ([line 3342](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3342)): Performs attribute updates and registers virtual group.
- **Dependencies**: `ExplorerDesktop`, `FolderPreviewOverlay`, `DataStore`, `GroupTileFactory`.
- **Architectural Claims**:
  - **`VERIFIED`**: Quick drops (< 280 ms) remain natively controlled by Explorer without app interference ([line 3265](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3265)).
  - **`VERIFIED`**: Moving away from the armed target cancels the merge gesture cleanly without committing ([line 3269](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3269)).
  - **`INFERRED`**: `dragOwnership` state transitions protect against duplicate collection commits when mouse-up events fire during animation.
  - **`UNKNOWN`**: Whether merge cancellation race conditions can occur if Explorer receives input events while `WM_CANCELMODE` is in flight.

### 1.2 FolderPreviewOverlay
- **File / Lines**: [DirectDesktopFolders.cs:854–914](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L854-L914)
- **Primary Role**: Visual feedback overlay displayed during `MergeArmed` state.
- **Key Methods**:
  - `Arm(DesktopItem item, bool reduceMotion)` ([line 871](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L871)): Positions the overlay over target icon bounds and starts expansion animation.
  - `AnimateArmed()` ([line 887](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L887)): 15 ms timer easing opacity and bounds over 120 ms.
  - `OnPaint(PaintEventArgs e)` ([line 900](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L900)): Draws semi-transparent rounded rectangle and 4 mini app preview tiles.
- **Dependencies**: `DesktopItem`, `Native` (`WS_EX_TRANSPARENT`, `WS_EX_NOACTIVATE`).
- **Architectural Claims**:
  - **`VERIFIED`**: Uses `WS_EX_TRANSPARENT | WS_EX_NOACTIVATE` window styles so mouse events pass straight through to the desktop ([line 869](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L869)).
  - **`VERIFIED`**: Respects `reduceMotion`: instantly displays full-opacity overlay without timer animation ([line 874](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L874)).
  - **`INFERRED`**: Magenta transparency key (`TransparencyKey = Color.Magenta`) is used for GDI non-rectangular form clipping.

### 1.3 ExplorerDesktop
- **File / Lines**: [DirectDesktopFolders.cs:396–715](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L396-L715)
- **Primary Role**: Window hierarchy resolution, UI Automation scan, hit testing, and collision-free Shell grid placement.
- **Key Methods**:
  - `GetListView()` ([line 605](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L605)): Locates `SysListView32` under `Progman` or active worker windows.
  - `Scan()` ([line 640](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L640)): Enumerates desktop icons via UI Automation and cross-references filesystem paths.
  - `HitTest()` ([line 680](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L680)): Magnetic hit testing with 8 px inflated bounding box.
  - `TryPlaceAtNearestFreeSlot()` ([line 446](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L446)): Interacts with `IFolderView` COM interface to position items without collisions.
- **Dependencies**: Windows UI Automation (`AutomationElement`), Shell COM interfaces (`IShellWindows`, `IFolderView`, `IShellFolder`), `Native`.
- **Architectural Claims**:
  - **`VERIFIED`**: File-backed items retain their existing path flow. Recycle Bin, This PC, and Network are additionally recognized by localized Shell display name and stable `KNOWNFOLDERID`; other virtual system icons remain excluded (`DirectDesktopFolders.cs:851-883`).
  - **`VERIFIED`**: Restores icons into unoccupied desktop grid slots using expanding radial search from preferred point ([lines 479-493](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L479-L493)).
  - **`INFERRED`**: `BuildPathIndex()` indexes both personal and public desktops to handle shortcuts installed machine-wide.
  - **`UNKNOWN`**: Whether Windows Explorer restarts or crash recoveries require re-fetching `cachedListView`.

---

## 2. Collection Data Subsystem

Responsible for representing virtual groups and their members, persisting metadata to disk, and maintaining a valid hierarchical DAG (directed acyclic graph).

### 2.1 VirtualLayout
- **File / Lines**: [DirectDesktopFolders.cs:66–70](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L66-L70)
- **Primary Role**: Root schema model containing `Version` and `Groups` (`List<VirtualGroup>`).
- **Architectural Claims**:
  - **`VERIFIED`**: Current layout version is `4`; version-3 layouts deserialize without migration loss because new member fields are additive (`DirectDesktopFolders.cs:47-80`, `184-205`).
  - **`VERIFIED`**: Directly serialized to JSON in `%APPDATA%\DesktopFolders\virtual-layout.json` ([lines 77](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L77), [120](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L120)).

### 2.2 VirtualGroup
- **File / Lines**: [DirectDesktopFolders.cs:56–64](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L56-L64)
- **Primary Role**: Model representing an individual collection.
- **Fields**:
  - `Id`: Unique string GUID (N format).
  - `Name`: Display name of the collection.
  - `TilePath`: Absolute path to the `.lnk` file on Desktop.
  - `IconPath`: Absolute path to the generated composite `.ico`.
  - `Members`: `List<VirtualMember>` holding either filesystem `Path`/`OriginalAttributes` or Shell `ShellIdentity`/`OriginalShellVisibility`, plus optional nested `GroupId`.
  - `Pinned`: `List<string>` holding paths of favorited items.
- **Architectural Claims**:
  - **`VERIFIED`**: Retains original member file attributes in `OriginalAttributes` for exact restoration ([line 50](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L50)).
  - **`VERIFIED`**: Shell members persist a `KNOWNFOLDERID` string and logical original visibility; raw PIDLs are never serialized (`DirectDesktopFolders.cs:55-65`, `480-577`).
  - **`VERIFIED`**: Nested groups are tracked via `VirtualMember.GroupId` while keeping `Path` pointing to the child `.lnk` ([lines 51-53](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L51-L53)).

### 2.3 VirtualLayoutGraph
- **File / Lines**: [DirectDesktopFolders.cs:169–275](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L169-L275)
- **Primary Role**: Graph validation, cycle detection, hierarchy traversal, and path cascading.
- **Key Methods**:
  - `Normalize()` ([line 171](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L171)): Repairs stale member paths and reconciles `GroupId` links.
  - `WouldCreateCycle()` ([line 229](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L229)): Rejects containment if `childId == destinationParentId` or if destination is a descendant of child.
  - `ContainsGroup()` ([line 211](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L211)): Recursive depth-first search with visited set to detect cycles.
  - `ParentsOf()` ([line 234](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L234)): Returns all parent collections containing a specific group ID or tile path.
  - `ReplaceTilePath()` ([line 243](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L243)): Updates child tile paths across all parents when a group is renamed.
  - `RefreshGroupAndAncestors()` ([line 262](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L262)): Recursively triggers `GroupTileFactory.CreateOrUpdate()` up the parent chain so composite thumbnail icons reflect child changes.
- **Architectural Claims**:
  - **`VERIFIED`**: Nested collections support multi-parent links or multiple levels of depth without stack overflows due to cycle protection ([lines 218](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L218), [269](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L269)).
  - **`INFERRED`**: `Normalize()` ensures backward compatibility when upgrading from older virtual layout schema versions.

### 2.4 DataStore
- **File / Lines**: [DirectDesktopFolders.cs:72–167](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L72-L167)
- **Primary Role**: Atomic file persistence, JSON serialization, and drag diagnostic logging.
- **Key Methods**:
  - `LoadVirtualLayout()` / `SaveVirtualLayout()` ([lines 101-122](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L101-L122)): Atomic save using `.tmp` file and `File.Replace()`.
  - `LoadSettings()` / `SaveSettings()` ([lines 81-99](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L81-L99)): Reads and writes `settings.json`.
  - `LogDrag()` ([line 137](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L137)): Appends timestamps and trace messages when compiled with `TRACE_DRAG`.
- **Architectural Claims**:
  - **`VERIFIED`**: Data directory is strictly `%APPDATA%\DesktopFolders\` ([line 75](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L75)).
  - **`VERIFIED`**: `SaveVirtualLayout()` guarantees atomicity through `File.Replace()` ([line 121](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L121)).

---

## 3. Collection UI Subsystem

Responsible for rendering the popup window, handling user interactions, item activation, internal drag reordering, and visual motion effects.

### 3.1 FolderPanel
- **File / Lines**: [DirectDesktopFolders.cs:1622–2372](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1622-L2372)
- **Primary Role**: The main popup Form displaying collection contents.
- **Key Methods**:
  - `ShowOrActivate()` ([line 1676](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1676)): Factory method enforcing single-popup-per-group policy via `OpenPanels` dictionary.
  - `RenderApps()` ([line 1937](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1937)): Rebuilds controls, groups by Pinned vs All, computes responsive 3-column widths.
  - `PreviewGridReorder()` ([line 2087](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2087)): Calculates real-time card slot swaps during internal drag.
  - `AnimateReorderFrame()` ([line 2142](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2142)): Critically damped spring simulation updating displaced card locations.
  - `MoveOut()` ([line 2220](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2220)): Restores member to desktop and dissolves group if 1 item remains.
  - `ToggleExpanded()` ([line 1817](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1817)): Switches between compact (440×520) and expanded (840×600) view.
- **Dependencies**: `AppTile`, `DragTileGhost`, `WindowMorphOverlay`, `CompositionMotionOverlay`, `DarkScrollBar`, `GradientPanel`.
- **Architectural Claims**:
  - **`VERIFIED`**: Multiple collection panels can be open concurrently without closing each other ([lines 1629](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1629), [1895](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1895)).
  - **`VERIFIED`**: `TopMost` is set only transiently upon opening/promotion and cleared after 260 ms to allow other applications to overlay the collection ([lines 1809-1815](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1809-L1815)).
  - **`UNKNOWN`**: Whether animation frame dropping occurs under heavy GPU load during simultaneous open transitions of multiple panels.

### 3.2 AppTile
- **File / Lines**: [DirectDesktopFolders.cs:1102–1232](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1102-L1232)
- **Primary Role**: Individual card control inside `FolderPanel`.
- **Key Methods**:
  - `OnPaint()` ([line 1163](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1163)): Renders translucent glass card, gradient border, icon, label, and pinned star badge.
  - `AnimateHover()` ([line 1156](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1156)): Elapsed-time exponential response for smooth hover transitions.
  - `SetDragVisual()` ([line 1148](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1148)): Toggles between placeholder mode, drop target mode, and nesting highlight.
- **Dependencies**: `IconLoader`, `CollectionTheme`, `DrawExtensions`.
- **Architectural Claims**:
  - **`VERIFIED`**: Accessible name and role (`AccessibleRole.ListItem`) are explicitly provided for screen readers ([line 1125](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1125)).
  - **`VERIFIED`**: Disposed cleanly including internal timers and context menus ([line 1231](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1231)).

---

## 4. IPC Subsystem

Responsible for ensuring single-instance execution, command routing from secondary processes, and opening requested collections.

### 4.1 Program
- **File / Lines**: [DirectDesktopFolders.cs:3454–3516](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3454-L3516)
- **Primary Role**: Application entry point and Mutex ownership.
- **Key Methods**:
  - `Main(string[] args)` ([line 3457](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3457)): Evaluates CLI arguments (`--open-group`, `--startup`, test switches).
  - Mutex evaluation (`DesktopFolders.Direct.SingleInstance`) ([lines 3502-3514](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3502-L3514)):
    - If mutex exists: sends command via `SingleInstanceCommandWindow.SendCommand()` and terminates immediately.
    - If mutex acquired: runs `Application.Run(new DirectDesktopController(...))`.
- **Dependencies**: `DirectDesktopController`, `SingleInstanceCommandWindow`, `DataStore`.
- **Architectural Claims**:
  - **`VERIFIED`**: Launching the EXE while already running brings Settings to the foreground instead of silently exiting ([lines 3510](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3510), [3073](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3073)).
  - **`VERIFIED`**: `--startup` argument suppresses opening the Settings window on system boot ([line 3497](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3497)).

### 4.2 SingleInstanceCommandWindow
- **File / Lines**: [DirectDesktopFolders.cs:277–339](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L277-L339)
- **Primary Role**: Native window receiving `WM_COPYDATA` messages.
- **Key Methods**:
  - `WndProc(ref Message message)` ([line 291](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L291)): Handles `WM_COPYDATA`, unmarshals `COPYDATASTRUCT`, and invokes the command receiver delegate.
  - `SendCommand(string command)` ([line 310](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L310)): Searches for target window by caption (`DesktopFolders.Direct.Command.v6`) with 30 retries, marshals Unicode payload, and invokes `SendMessage`.
- **Dependencies**: `Native` (`FindWindow`, `SendMessage`, `COPYDATASTRUCT`), `DataStore`.
- **Architectural Claims**:
  - **`VERIFIED`**: Uses Unicode string marshalling (`StringToHGlobalUni` / `PtrToStringUni`) to support any international group name ([lines 298](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L298), [323](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L323)).
  - **`VERIFIED`**: Memory allocated for `COPYDATASTRUCT` is freed in `finally` blocks to prevent unmanaged memory leaks ([lines 332](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L332)).
  - **`INFERRED`**: `NativeWindow` was chosen over a hidden WinForms `Form` to avoid creating a heavy Form handle with message loop overhead.
