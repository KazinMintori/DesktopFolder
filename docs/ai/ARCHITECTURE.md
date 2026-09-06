# ARCHITECTURE.md — DesktopFolders Deep Subsystem Architecture

This document provides a precise, verified breakdown of the core architectural components of DesktopFolders.

Every important architectural claim is classified into one of three epistemic categories:
- **`VERIFIED`**: Directly confirmed by inspecting the source code in `DirectDesktopFolders.cs`.
- **`INFERRED`**: Strongly supported by circumstantial code patterns and comments, but not directly proven by a single line of code.
- **`UNKNOWN`**: Requires dynamic runtime execution or environment-specific testing to verify.

---

## 1. DirectDesktopController

The central orchestration controller extending `System.Windows.Forms.ApplicationContext` ([DirectDesktopFolders.cs:3034](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3034)). It runs the tray lifecycle without a persistent taskbar window.

### 1.1 Pointer Monitoring
- **`VERIFIED`**: Uses a dedicated 15 ms `pointerMonitor` timer polling `Control.MouseButtons & MouseButtons.Left` and `Cursor.Position` ([DirectDesktopFolders.cs:3089-3091](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3089-L3091)).
- **`VERIFIED`**: Installs **no global mouse hooks** (`WH_MOUSE_LL`). Tracking occurs only when `ExplorerDesktop.IsDesktopForeground()` and `ExplorerDesktop.IsPointOnDesktopSurface(point)` are both true ([DirectDesktopFolders.cs:3256](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3256)).
- **`VERIFIED`**: Distinguishes clicks from drags using system drag thresholds `thresholdX = SystemInformation.DragSize.Width / 2`, `thresholdY = SystemInformation.DragSize.Height / 2` ([DirectDesktopFolders.cs:3263](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3263)).
- **`VERIFIED`**: Quick clicks on collection tiles trigger deferred activation (`Queue(...)`) to open the collection popup ([DirectDesktopFolders.cs:3291](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3291)).
- **`INFERRED`**: Read-only polling was chosen specifically to prevent antivirus/EDR heuristic flags commonly triggered by global input hooks.
- **`UNKNOWN`**: Whether high-DPI displays with fractional scaling (e.g. 125%, 175%) ever cause `Cursor.Position` to slightly misalign with `SysListView32` item rectangles during fast pointer sweeps.

### 1.2 Drag State Machine
- **`VERIFIED`**: Managed by the `DragOwnership` enum: `Windows`, `HoverPending`, `MergeArmed`, `Cancelled` ([DirectDesktopFolders.cs:3036](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3036)).
- **`VERIFIED`**: **Quick Drops Pass-Through**: While in `DragOwnership.Windows` (< 280 ms), drops are completely handled by native Windows Explorer drag-and-drop. DesktopFolders does not interfere ([DirectDesktopFolders.cs:3265](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3265)).
- **`VERIFIED`**: **Hover Delay Arming**: If a dragged icon hovers over a different desktop icon for `Settings.FolderHoverDelay` (default 280 ms, range 120–800 ms), the `hoverArm` timer fires `ArmHoverTarget()` ([DirectDesktopFolders.cs:3277-3278](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3277-L3278), [3310-3327](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3310-L3327)).
- **`VERIFIED`**: **Targeted Drag Cancellation**: `ArmHoverTarget()` cancels Explorer's OLE drag by sending `WM_CANCELMODE` to `SysListView32` and its root window, followed by posting `WM_KEYDOWN`/`WM_KEYUP` with `VK_ESCAPE`. No global inputs are injected ([DirectDesktopFolders.cs:3320-3322](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3320-L3322)).
- **`VERIFIED`**: Sets state to `DragOwnership.MergeArmed` and triggers `FolderPreviewOverlay.Arm()` ([DirectDesktopFolders.cs:3324-3325](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3324-L3325)).
- **`VERIFIED`**: Moving away from the armed target cancels the merge gesture, resets state to `Cancelled`, and hides the preview ([DirectDesktopFolders.cs:3269](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3269)).
- **`VERIFIED`**: On mouse-up while `MergeArmed`, `CommitPendingFolder()` calls `AddOrCreateGroup()` to commit the collection ([DirectDesktopFolders.cs:3294](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3294), [3329-3342](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3329-L3342)).
- **`INFERRED`**: The 140 ms `postDrop` timer delay before `CommitPendingFolder()` ensures Explorer completely finishes cleaning up its cancelled OLE drag state before DesktopFolders modifies file attributes.
- **`UNKNOWN`**: Whether rapid consecutive drag-and-drop operations on touchscreens or precision trackpads can cause `MergeArmed` to trigger without a preceding mouse-move event.

### 1.3 Desktop Scanning
- **`VERIFIED`**: Runs a background worker thread (`scanner`, interval 1,200 ms) via `System.Threading.Timer` ([DirectDesktopFolders.cs:3123-3130](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3123-L3130)).
- **`VERIFIED`**: Actively scans only when `ExplorerDesktop.IsDesktopForeground()` is true ([DirectDesktopFolders.cs:3124](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3124)).
- **`VERIFIED`**: When non-desktop windows are foregrounded, `inactiveTicks` increments. After 10 ticks (~12 seconds), it calls `Native.EmptyWorkingSet()` to trim process memory, reducing footprint to ~4 MB ([DirectDesktopFolders.cs:3125-3129](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3125-L3129)).
- **`VERIFIED`**: Scanned item snapshots are guarded by `cacheLock` and synced to open `FolderPanel` instances via `FolderPanel.SyncDesktopItems()` ([DirectDesktopFolders.cs:3229-3232](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3229-L3232)).
- **`INFERRED`**: `EmptyWorkingSet()` forces memory pages to the standby/paging list, which reduces resident set size in Task Manager but may cause minor soft page faults upon reactivation.
- **`UNKNOWN`**: Whether frequent `EmptyWorkingSet()` calls cause measurable latency when reopening large collections on low-RAM systems with slow HDD paging.

### 1.4 IPC
- **`VERIFIED`**: Uses a dedicated hidden `NativeWindow` named `SingleInstanceCommandWindow` with caption `"DesktopFolders.Direct.Command.v6"` ([DirectDesktopFolders.cs:279](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L279)).
- **`VERIFIED`**: Communication uses `WM_COPYDATA` with Unicode payload strings (`Marshal.StringToHGlobalUni` / `Marshal.PtrToStringUni`) ([DirectDesktopFolders.cs:298](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L298), [323](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L323)).
- **`VERIFIED`**: Commands supported:
  - `"DesktopFolders.Direct.Activate"`: Activates running process and opens `SettingsForm`.
  - `<groupId>`: Requests opening a collection popup.
- **`VERIFIED`**: Incoming requests are deduped/coalesced into `pendingOpenRequests` (`HashSet<string>`) and processed by `DrainCommands()` on a 20 ms timer ([DirectDesktopFolders.cs:3086-3088](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3086-L3088), [3157-3163](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3157-L3163)).
- **`VERIFIED`**: **File-based Fallback**: If `SendMessage(WM_COPYDATA)` fails or window is not found after 30 retries (e.g. during cold boot race), the secondary process writes a `<guid>.request` file to `%APPDATA%\DesktopFolders\open-requests\`, which the main controller drains periodically ([DirectDesktopFolders.cs:148-166](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L148-L166), [3159](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L3159)).
- **`INFERRED`**: Retrying 30 times with 60 ms sleep ([DirectDesktopFolders.cs:314-318](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L314-L318)) ensures secondary processes wait up to 1.8 seconds for the primary process message pump to initialize.

---

## 2. FolderPanel

The popup Form representing an open collection ([DirectDesktopFolders.cs:1622](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1622)).

### 2.1 Collection UI
- **`VERIFIED`**: Chromeless form (`FormBorderStyle = FormBorderStyle.None`, `ShowInTaskbar = false`, `DoubleBuffered = true`) with a custom rounded region (`CollectionTheme.RadiusWindow = 20`).
- **`VERIFIED`**: `build.ps1` embeds `CollectionBackground.Cosmic.png` under the stable resource name `DesktopFolders.CollectionBackground.png`. `CollectionArtwork.Background` renders it with aspect-aware focal cropping, a midnight veil, ambient glow, and a restrained blue-to-lavender window edge (`DirectDesktopFolders.cs`, `CollectionArtwork` / `GradientPanel`).
- **`VERIFIED`**: Title editing: Static `GradientLabel` swaps to `TextBox` on click or F2; Enter or clicking away commits rename, Escape cancels ([DirectDesktopFolders.cs:1754-1756](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1754-L1756), [1932-1936](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1932-L1936)).
- **`VERIFIED`**: Real-time search textbox filters members by stem name without round-tripping to disk ([DirectDesktopFolders.cs:1774](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1774), [1943](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1943)).
- **`VERIFIED`**: Mode toggle: Grid mode uses three cards per row in 106 px compact or 134 px expanded tile allocations; List mode uses 64 px rows.
- **`VERIFIED`**: **Smart Anchoring**: `CalculateAnchoredLocation()` places popup adjacent to the desktop tile (10 px gap: below $\to$ above $\to$ right $\to$ left). `FindOpenLocation()` scores overlapping panels and cascades to prevent obscuring other open collections ([DirectDesktopFolders.cs:1887-1931](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1887-L1931)).
- **`VERIFIED`**: Dynamically follows tile movement when Explorer rearranges desktop icons via `QueueAnchorUpdate()` ([DirectDesktopFolders.cs:1909-1918](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1909-L1918)).
- **`VERIFIED`**: Non-functional popup background surfaces support temporary click-hold movement through the popup's native `WM_NCLBUTTONDOWN/HTCAPTION` move loop. The manual location is instance-only and suppresses visual re-anchoring while live anchor metadata continues to update; close/reopen or explicit tile activation restores normal anchoring (`DirectDesktopFolders.cs`, `BindBackgroundDrag` / `QueueAnchorUpdate` / `ReanchorForActivation`).
- **`VERIFIED`**: Clicking empty desktop surface closes the popup via `CloseWithMorph()` ([DirectDesktopFolders.cs:2202](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2202)).

### 2.2 Reorder
- **`VERIFIED`**: Dragging a card within its section starts internal WinForms `DoDragDrop()` ([DirectDesktopFolders.cs:1970-1973](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1970-L1973)).
- **`VERIFIED`**: `DragTileGhost` follows cursor while the dragged card in `apps` turns into a dashed translucent placeholder ([DirectDesktopFolders.cs:1169-1174](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1169-L1174), [1234-1262](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1234-L1262)).
- **`VERIFIED`**: Hovering over neighbor cards triggers `PreviewGridReorder()`, swapping control positions inside `FlowLayoutPanel` ([DirectDesktopFolders.cs:2087-2105](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2087-L2105)).
- **`VERIFIED`**: **Critically Damped Spring Animation**: Displaced cards animate to new positions using exact second-order differential equation equations (`omega = 28f`), ensuring smooth motion even during rapid direction changes ([DirectDesktopFolders.cs:2142-2162](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2142-L2162)).
- **`VERIFIED`**: On drop, `CommitGridReorder()` persists the new order to `virtual-layout.json` and refreshes composite icons ([DirectDesktopFolders.cs:2106-2111](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2106-L2111)).
- **`VERIFIED`**: Cross-section reordering between Pinned and Unpinned items is prohibited ([DirectDesktopFolders.cs:2028](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2028), [2095](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2095)).

### 2.3 Nested Collections
- **`VERIFIED`**: Hovering over the center area of a card (`IsNestDropZone`: inside inner 60% of card) for `FolderHoverDelay` arms nest grouping (`nestDropArmed = true`) ([DirectDesktopFolders.cs:2017-2023](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2017-L2023), [2073-2086](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2073-L2086)).
- **`VERIFIED`**: If target is an individual item, drop creates a new nested `VirtualGroup` containing both items via `CreateNestedGroup()` ([DirectDesktopFolders.cs:2338-2359](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2338-L2359)).
- **`VERIFIED`**: The new sub-collection tile is marked `Hidden` on Desktop and referenced by `GroupId` in the parent collection ([DirectDesktopFolders.cs:2349-2351](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2349-L2351)).
- **`VERIFIED`**: **Cycle Prevention**: Blocks nesting if `VirtualLayoutGraph.WouldCreateCycle()` returns true ([DirectDesktopFolders.cs:2311](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2311), [2329](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2329)).
- **`VERIFIED`**: Clicking a nested collection card in `FolderPanel` activates the nested popup via `FolderPanel.ShowOrActivate()` ([DirectDesktopFolders.cs:1983](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1983)).

### 2.4 Transfer
- **`VERIFIED`**: Dragging an item from one open `FolderPanel` to another transfers it via `TransferMember()` ([DirectDesktopFolders.cs:2300](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2300), [2305-2322](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2305-L2322)).
- **`VERIFIED`**: Dragging external files from Explorer onto the panel adds them via `AddMember()` ([DirectDesktopFolders.cs:2301](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2301), [2323-2337](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2323-L2337)).
- **`VERIFIED`**: Dragging an item out of the panel onto the desktop surface calls `AnimateMoveOut()` $\to$ `MoveOut()` ([DirectDesktopFolders.cs:1975-1979](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L1975-L1979), [2179-2190](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2179-L2190), [2220-2234](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L2220-L2234)):
  - Restores original file attributes.
  - Removes item from group.
  - Queues placement at nearest free desktop grid slot via `ExplorerDesktop.QueuePlacement()`.
  - Dissolves group if $\le 1$ item remains and `DissolveSingleAppGroup` is true.

---

## 3. DataStore

The serialization and file system access layer ([DirectDesktopFolders.cs:72](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L72)).

### 3.1 `virtual-layout.json`
- **`VERIFIED`**: Located at `%APPDATA%\DesktopFolders\virtual-layout.json` ([DirectDesktopFolders.cs:77](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L77)).
- **`VERIFIED`**: Serialized and deserialized using `System.Web.Script.Serialization.JavaScriptSerializer` ([DirectDesktopFolders.cs:74](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L74), [87](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L87), [107](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L107)).
- **`VERIFIED`**: **Atomic Save Pattern**: `SaveVirtualLayout()` writes to a temporary file (`.tmp`) first, then uses `File.Replace()` (or `File.Move()` if target does not exist) to avoid partial write corruption ([DirectDesktopFolders.cs:119-121](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L119-L121)).
- **`VERIFIED`**: Calls `VirtualLayoutGraph.Normalize()` before saving and after loading to ensure structural graph validity ([DirectDesktopFolders.cs:108](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L108), [117](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L117)).
- **`VERIFIED`**: Current layout version constant is `4`; version-3 layouts remain loadable because the new Shell fields are additive and filesystem members continue to use `Path` and `OriginalAttributes` (`DirectDesktopFolders.cs:47-80`).
- **`INFERRED`**: `JavaScriptSerializer` was chosen because it is available in standard .NET 4.x GAC (`System.Web.Extensions.dll`) without external NuGet packages like `Newtonsoft.Json`.
- **`UNKNOWN`**: Whether unexpected OS crash during `File.Replace` on FAT32 formatted partitions leaves `.tmp` files uncollected.

---

## 4. ExplorerDesktop

The low-level Shell and UI Automation bridge interacting with Windows Explorer ([DirectDesktopFolders.cs:396](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L396)).

### 4.1 UI Automation
- **`VERIFIED`**: `GetListView()` navigates `Progman` $\to$ `SHELLDLL_DefView` $\to$ `SysListView32` (or enumerates top-level windows if `Progman` does not hold `SHELLDLL_DefView`) ([DirectDesktopFolders.cs:605-618](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L605-L618)).
- **`VERIFIED`**: `Scan()` connects via `AutomationElement.FromHandle(listHandle)` and enumerates child elements matching `TreeScope.Children` ([DirectDesktopFolders.cs:648-649](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L648-L649)).
- **`VERIFIED`**: Reads item bounding rectangles (`BoundingRectangle`) to build `DesktopItem` records ([DirectDesktopFolders.cs:663-670](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L663-L670)).
- **`VERIFIED`**: Correlates element names with file system entries indexed across all desktop roots (Personal Desktop, Public Desktop, OneDrive Desktop) via `BuildPathIndex()` ([DirectDesktopFolders.cs:630-638](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L630-L638), [688-707](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L688-L707)).
- **`VERIFIED`**: Recycle Bin, This PC, and Network are matched by localized Shell display name and stored by stable `KNOWNFOLDERID`; all other pathless namespace items remain unsupported (`DirectDesktopFolders.cs:408-577`, `851-883`).
- **`VERIFIED`**: Runtime PIDLs returned by `SHGetKnownFolderIDList` are scoped to individual operations and released with `ILFree`; icon handles returned by `SHGetFileInfo` are released with `DestroyIcon` (`DirectDesktopFolders.cs:480-530`).
- **`INFERRED`**: UI Automation is used instead of sending `LVM_GETITEMRECT` across process boundaries to prevent 32/64-bit cross-architecture pointer marshalling crashes.

### 4.2 Hit Testing
- **`VERIFIED`**: `IsPointOnDesktopSurface()` uses `WindowFromPoint` to verify the pointer is directly over `SysListView32` or its child window, preventing false positive drag triggers over foreground application windows ([DirectDesktopFolders.cs:531-538](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L531-L538)).
- **`VERIFIED`**: `HitTest()` inflates icon bounding rectangles by 8 px (`Rectangle.Inflate(item.Bounds, 8, 8)`) to provide a comfortable magnetic hit target ([DirectDesktopFolders.cs:684](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L684)).
- **`VERIFIED`**: Excludes the source item itself during hit testing to prevent an item from merging with itself ([DirectDesktopFolders.cs:684](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L684)).

### 4.3 Shell Positioning
- **`VERIFIED`**: Restored icons are positioned through the supported COM Shell interface chain:
  `ShellWindowsObject` $\to$ `IShellWindowsNative::FindWindowSW` $\to$ `IServiceProviderNative::QueryService` $\to$ `IShellBrowserNative::QueryActiveShellView` $\to$ `IFolderViewNative` $\to$ `IShellFolderNative` ([DirectDesktopFolders.cs:449-460](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L449-L460)).
- **`VERIFIED`**: Enumerates existing icon positions (`IFolderViewNative::GetItemPosition`) and desktop grid spacing (`IFolderViewNative::GetSpacing`) ([DirectDesktopFolders.cs:465-474](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L465-L474)).
- **`VERIFIED`**: **Spiral Grid Search**: Starting from the preferred screen drop point, `TryPlaceAtNearestFreeSlot()` executes an expanding Manhattan radius search to find the closest unoccupied grid cell ([DirectDesktopFolders.cs:479-493](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L479-L493)).
- **`VERIFIED`**: Commits position via `IFolderViewNative::SelectAndPositionItems()` with flag `0x80` ([DirectDesktopFolders.cs:495](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L495)).
- **`VERIFIED`**: `QueuePlacement()` runs placement requests sequentially in an STA background thread (`worker.SetApartmentState(ApartmentState.STA)`) with up to 18 retries per item ([DirectDesktopFolders.cs:421](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L421), [435](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L435)).
- **`VERIFIED`**: Notifies Shell of changes with targeted `SHChangeNotify(SHCNE_ATTRIBUTES | SHCNE_UPDATEITEM)` and `SHChangeNotify(SHCNE_DELETE)` without broadcasting disruptive `UPDATEDIR` events ([DirectDesktopFolders.cs:577-603](file:///C:/Users/Ai/.gemini/antigravity/worktrees/DesktopFolder/create_project_map_documentation/DirectDesktopFolders.cs#L577-L603)).
- **`INFERRED`**: The background queue with retries is necessary because Explorer can take 50–200 ms to re-enumerate a file after its `Hidden` attribute is stripped.
- **`UNKNOWN`**: Whether third-party desktop organizers (e.g. Fences, Stardock) hook `IFolderViewNative` and override `SelectAndPositionItems` calls.
