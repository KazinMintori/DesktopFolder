# Current state

Last audited: 2026-09-06

## Verified baseline

- **VERIFIED**: Standard, trace, and test builds share `src/DesktopFolders.cs`; required background and string resources are embedded into each executable and the build has no Windows SDK metadata dependency.
- **VERIFIED**: The collection feature bar is a stable rounded group aligned above the search bar with grid, list, expand/collapse, and close actions.
- **VERIFIED**: Recycle Bin, This PC, and Network work in Shell-only and mixed collections using stable identities and operation-scoped PIDLs.
- **VERIFIED**: Filesystem-item persistence and attribute restoration remain unchanged; schema version 4 is additive and accepts existing layouts.
- **VERIFIED**: Normal exit preserves collections. Safe uninstall requires restoring or dissolving collections before deleting application data.
- **VERIFIED**: The tray **Prepare uninstall** action now performs that restoration transaction, removes per-user registration/data, and exits without trying to self-delete the running executable.
- **VERIFIED**: Existing collection shortcuts self-heal when the executable is relocated; link targets are inspected at startup rather than inferred from icon timestamps.
- **VERIFIED**: The portable executable has no external asset, localization, or third-party DLL requirement.

## Release boundaries

- **VERIFIED**: Building requires the .NET Framework developer compiler; running the compiled executable does not require developer tools.
- **VERIFIED**: There is no installer or automatic updater. Startup is per-user through the Windows Run key; safe uninstall preparation is built into the tray menu.
- **VERIFIED**: The executable is not digitally signed.
- **INFERRED**: SmartScreen or antivirus reputation warnings are likely for a newly downloaded unsigned build, especially while download prevalence is low.
- **UNKNOWN**: Full compatibility has not been physically tested on every supported Windows 10/11 build, DPI combination, multi-monitor topology, remote session, or clean-machine policy configuration.

## Maintainer cautions

- Keep edits surgical: most behavior remains in one large source file.
- Preserve diagnostic and test compilation paths.
- Do not use broad directory notifications, global input hooks, Explorer injection, or physical member moves.
- Validate Shell COM/PIDL lifetime whenever extending virtual namespace support.
