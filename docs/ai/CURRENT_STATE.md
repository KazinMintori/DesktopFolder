# Current state

Last audited: 2026-11-11

## Verified baseline

- **VERIFIED**: Standard, trace, and test builds share `src/DesktopFolders.cs`; required background and string resources are embedded into each executable and the build has no Windows SDK metadata dependency.
- **VERIFIED**: The collection feature bar is a stable rounded group aligned above the search bar with grid, list, expand/collapse, and close actions.
- **VERIFIED**: Recycle Bin, This PC, and Network work in Shell-only and mixed collections using stable identities and operation-scoped PIDLs.
- **VERIFIED**: Filesystem-item persistence and attribute restoration remain unchanged; schema version 4 is additive and accepts existing layouts.
- **VERIFIED**: Normal exit preserves collections. Safe uninstall requires restoring or dissolving collections before deleting application data.
- **VERIFIED**: The tray **Prepare uninstall** action now performs that restoration transaction, removes per-user registration/data, and exits without trying to self-delete the running executable.
- **VERIFIED**: Existing collection shortcuts self-heal when the executable is relocated; link targets are inspected at startup rather than inferred from icon timestamps.
- **VERIFIED**: The portable executable has no external asset, localization, or third-party DLL requirement.

- **VERIFIED**: Settings scales its 96-DPI layout by system DPI; descriptions no longer clip at 125%. Collection tiles show two-line names, expanded mode uses four columns, and icons load at 96px from the Shell.
- **VERIFIED**: All dialogs use the dark `ThemedDialog`; Public Desktop permission failures show an actionable explanation.
- **VERIFIED**: Restore placement defers to Explorer when "Auto arrange icons" is enabled.
- **INFERRED**: The collection popup geometry is still authored in pixels tuned at 120 DPI; at 150%+ system scaling, its text may feel crowded. Not reproducible on the 125% development display.

## Release boundaries

- **VERIFIED**: Building requires the .NET Framework developer compiler; running the compiled executable does not require developer tools.
- **VERIFIED**: There is no installer or automatic updater. Startup is per-user through the Windows Run key; safe uninstall preparation is built into the tray menu.
- **VERIFIED**: The repository provides local and GitHub Actions Authenticode signing paths; an actual release is signed only when a trusted certificate is supplied through the protected release environment.
- **INFERRED**: SmartScreen or antivirus reputation warnings are likely for a newly downloaded unsigned build, especially while download prevalence is low.
- **UNKNOWN**: Full compatibility has not been physically tested on every supported Windows 10/11 build, DPI combination, multi-monitor topology, remote session, or clean-machine policy configuration.

## Maintainer cautions

- Keep edits surgical: most behavior remains in one large source file.
- Preserve diagnostic and test compilation paths.
- Do not use broad directory notifications, global input hooks, Explorer injection, or physical member moves.
- Validate Shell COM/PIDL lifetime whenever extending virtual namespace support.
