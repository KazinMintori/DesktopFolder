# DesktopFolders UI design

Updated 2026-11-11. This note supersedes older neon UI descriptions; current source remains authoritative.

## Intent and scope

The existing product is a compact Windows desktop organizer: open a collection, find/launch an item, organize with drag or context commands, then return to the Desktop. Its dark cosmic backdrop, compact/expanded popup, three-column grid and list mode remain the design foundation.

`VERIFIED`: This pass changes presentation and UI feedback within the existing WinForms classes. Persistence, Shell identity/visibility, file attributes, graph operations, IPC and drag ownership/cancellation logic are unchanged in the diff.

## Shared visual system

`VERIFIED`: `CollectionTheme` in `src/DesktopFolders.cs` centralizes the palette used by collection surfaces, Settings controls, app-owned menus and drag preview.

| Role | Color |
| --- | --- |
| Background | #0F1224 |
| Surface | #1C2138 |
| Hover | #29304F |
| Input | #14192E |
| Active | #303A60 |
| Primary text | #F2F5FA |
| Secondary text | #CED5EA |
| Muted text | #A3ADCC |
| Search border | #67C7FF / #9B8CFF / #C078FF |
| Title gradient | #C078FF / #67C7FF |
| Border / strong stroke | #414767 / #828CAF |

Normal text remains high contrast on the opaque card and input surfaces. The cosmic artwork stays behind a translucent midnight veil; opaque controls provide the stable reading surfaces used for interaction.

Typography stays Segoe UI: collection titles retain the baseline 18pt size; Settings uses a 14pt title and 9–12pt labels/content. Content geometry follows the supplied baseline reference: a 40px full-width search field, compact grid tiles sized for a 77px plate plus two label lines (`AppTile.CompactGridHeight`, derived from the label font height), 42px compact icons, 28px section headers and a 6px scrollbar lane. The four 32×36px header actions sit inside a softly tinted translucent 140×44px glass group whose continuous blue–purple perimeter matches the search bar. The tint suppresses busy background detail. Selected and keyboard-focused actions use a colored glyph and small underline without any square blue outline. Closing a collection is immediate, and compact/expanded resizing occurs directly on the collection without spawning a topmost composition host window. `assets/CollectionBackground.png` uses the supplied portrait cosmic landscape. Cover rendering follows an aspect-aware focal point so the planet, nebula, mountains and reflective water remain represented in both compact and expanded crops.

## Collection

- `VERIFIED`: FolderPanel places Grid, List, Expand/Collapse and Close in a rounded 140×44px action frame, with four fixed 32×36px controls, 4–7px frame padding and a 7px visual gap above search. The frame's right edge aligns with the search field at both popup sizes. The actions remain opaque owner-drawn controls; every paint clears the full surface, and glyph/focus/state changes synchronously repaint.
- `VERIFIED`: The title retains the baseline 18pt position and size while applying the stronger `#C078FF → #67C7FF` purple-to-blue gradient over measured glyph width, with no white stops. Search retains its original full-width 40px geometry, 12px radius, flat interior and fixed 2px border. The continuous perimeter no longer draws one rounded path through two overlapping clips. Each side, corner arc and transition is painted once: the left side and left corners are blue, the right side and right corners are purple, with smooth 20%-wide transitions centered at 75% of the top edge and 25% of the bottom edge.
- `VERIFIED`: Search receives opening focus; its native I-beam caret remains high-contrast on the solid dark edit surface. The reference-only `Ctrl K` chip is intentionally absent; the existing Ctrl+F accessibility shortcut remains. Escape clears a nonempty search before closing the popup. Empty and no-match states provide localized guidance; no-match offers a clear-search button.
- `VERIFIED`: Holding and dragging any non-functional background surface—including the gap between search and item content—temporarily moves the open popup through the window's native `WM_NCLBUTTONDOWN/HTCAPTION` move loop. Search, title, buttons, scrollbar and app tiles are excluded. The position is never persisted; live anchor metadata keeps updating, and explicit reactivation or close/reopen restores normal desktop-tile anchoring.
- `VERIFIED`: AppTile keeps stable bounds/icon sizes during hover and press. Surface/border response uses the existing elapsed-time hover timer and reduced-motion branch. Labels receive more space, with full names available through existing tooltips.
- `VERIFIED`: Compact AppTile geometry follows the reference: a 77px glass plate surrounds the 42px icon; the label below it wraps to at most two centered lines before ellipsis, and the tile height follows the label font so two lines fit at the current system DPI. Expanded grids use four medium columns (105px plate, 56px icon) instead of three oversized plates. Icons come from `IShellItemImageFactory` at 96px and are downscaled with high-quality bicubic filtering; `ExtractAssociatedIcon` (32px) is only a fallback. In list mode the pin badge sits vertically centered at the row end and the label reserves room for it. Long collection titles shrink toward 13pt before using an ellipsis. AppTile glass uses RGB 20/24/48 at approximately 32% idle opacity and 42% hover opacity. Its idle border uses RGB 175/184/255 at approximately 19%; hover and drop states introduce a controlled blue-to-violet glow.
- `VERIFIED`: Focused tiles scroll into view. Shift+F10 / Apps opens their existing context menu. Reorder commands at section boundaries are visibly disabled; selected view controls expose pressed accessibility state.
- `VERIFIED`: DarkScrollBar draws only a 1 px track at roughly 2–7% opacity and a centered 4 px idle / 5 px active pill thumb. The thumb is roughly 31% opaque at rest and 70% while scrolling, hovering or dragging, then fades back after 650 ms.

## Settings

- `VERIFIED`: SettingsForm preserves all settings, language selection, Save/Close and diagnostics. Descriptions can wrap; title/version and action regions no longer overlap. Layout is authored in 96-DPI units and scaled once by `UiScale.Factor` (system DPI / 96); before this, fonts followed DPI while boxes did not, which clipped every second description line at 125%. The preview harness now fails if any Settings label clips its text.
- `VERIFIED`: Content uses the existing custom scrollbar, with a fixed header/footer and automatic scrolling to focused controls. Short windows retain access to diagnostics and actions.
- `VERIFIED`: Toggles distinguish checked, hover, pressed, focus and disabled states; keyboard Space/Enter operates the focused toggle. Slider arrows/Home/End change its value and expose it through accessibility.
- `VERIFIED`: Native language ComboBox behavior is retained with stable owner-drawn rows. The control no longer paints directly over the native handle after `WM_PAINT`, and selected rows use one background state without a second focus rectangle, preventing hover/focus flicker. Refresh shows a disabled working state, performs the existing scan off the UI thread and updates its result safely.

## Dialogs

- `VERIFIED`: `ThemedDialog` replaces every `MessageBox` with a dark rounded dialog (accent bar, tone badge for info/warning/error/question, localized buttons, MessageBox-compatible default/cancel semantics). The native MessageBox remains only as a fallback if the themed form cannot be constructed.
- `VERIFIED`: `ThemedDialog.DescribeFailure` maps access-denied failures to actionable text: items on the all-users Public Desktop explain that elevation is needed or that a personal copy can be grouped; other denials explain read-only/locked causes. Other failures keep the original localized message.

## Verification and limits

The optional tools/ui-preview/PreviewHarness.cs loads a separately built app assembly with fixture paths and no controller. It never invokes production startup or edits real collection members. See its README for commands.

`VERIFIED`: System-DPI-aware rendering was inspected at 120 DPI for English/Vietnamese compact, expanded, list, empty and no-match collections. Checks cover the transparent search-gradient action group, exact action bounds, pointer focus cleanup, keyboard focus visibility, the two-color title, single-pass perimeter mode, restored search geometry, search focus/filter/clear, 20 repeated renders per action in idle/hover/pressed/disabled states, and state/glyph transitions painted successively onto the same bitmap. Native pointer verification moved the fixture popup through the search-to-items background gap and confirmed that it remained there after the normal anchor-refresh interval.

`UNKNOWN`: Complete manual desktop drag/merge/restore regression and monitor-to-monitor DPI changes were not exercised by the fixture harness. Opening/reorder motion retains the existing logic; this pass does not establish runtime performance guarantees under RDP or heavy GPU load.
