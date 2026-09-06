# Handoff

Use this as orientation and verify relevant claims in `src/DesktopFolders.cs` before editing.

## Current implementation

- Application source is `src/DesktopFolders.cs`; build inputs and branding are grouped under `assets/` and `Resources/`.
- Collection header actions are custom-painted `HeaderIconButton` controls inside the `modes` rounded panel. Keep the 140×44 group, four 32×36 actions, and explicit repaint/state handling when changing it.
- Search perimeter rendering is implemented by `RoundedPanel.DrawContinuousPerimeter`; its four sides are intentionally drawn as non-overlapping pieces to prevent blue/purple seam artifacts.
- Virtual system icons are represented by `VirtualMember.ShellIdentity` and `OriginalShellVisibility`. `ShellDesktopItems` resolves and frees runtime PIDLs per operation. Never persist pointer values.
- Existing filesystem members continue to use `Path` and `OriginalAttributes`; do not route Shell objects through `File.*` APIs.
- `GroupTileFactory.TargetsCurrentExecutable` is part of startup repair. Preserve it when changing packaging or executable location behavior.
- `DirectDesktopController.PrepareUninstall` restores members before clearing persistence and rolls restoration back if the safety-critical phase fails.

## Required verification

```powershell
.\build.ps1
.\build.ps1 -TraceDrag -Output DesktopFolders-trace.exe
.\build.ps1 -TestBuild -Output DesktopFolders-test.exe
.\DesktopFolders-test.exe --test-layout-graph
.\DesktopFolders-test.exe --test-uninstall-restore
.\verify-release.ps1
```

Run the UI preview harness for rendering work and inspect `git diff` before handoff. Generated trace/test binaries are temporary and should not be committed.
