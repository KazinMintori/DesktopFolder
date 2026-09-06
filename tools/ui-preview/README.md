# Isolated native UI preview

Optional .NET Framework harness that loads a separately built application assembly and invokes its real `SettingsForm` and `FolderPanel` constructors. It never calls the application entry point or constructs a `DirectDesktopController`.

From the repository root, build an isolated app copy and the harness:

```powershell
New-Item -ItemType Directory -Force -Path .\outputs\ui-preview | Out-Null
.\build.ps1 -Output .\outputs\ui-preview\DesktopFolders-preview.exe
& 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe' /nologo /target:exe /optimize+ /out:outputs\ui-preview\PreviewHarness.exe /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll tools\ui-preview\PreviewHarness.cs
```

Render Settings and collection compact, expanded, list, empty, and unsuccessful search states in English and Vietnamese:

```powershell
.\outputs\ui-preview\PreviewHarness.exe --assembly .\outputs\ui-preview\DesktopFolders-preview.exe --output .\outputs\ui-preview --render-all
```

Open one interactive native preview; close it normally to exit:

```powershell
.\outputs\ui-preview\PreviewHarness.exe --assembly .\outputs\ui-preview\DesktopFolders-preview.exe --output .\outputs\ui-preview --show compact --language vi
```

Accepted `--show` values: `settings`, `compact`, `expanded`, `list`, `empty`, `nomatch`. An automated background launch should use `Start-Process -WindowStyle Hidden`; that hides the console while the requested preview Form remains available for inspection.

`VERIFIED` from the harness code: before any application data methods execute, all five `DataStore` paths are redirected to a fresh `run-<timestamp>-<id>` directory below the output directory. The real layout loader must return the fixture group or the harness fails. Settings receives a null controller, so Save and Refresh cannot register startup or alter live collections. Twelve inert files provide realistic extensions, long names and three pinned members. External drops, tile launch/drag/context actions and title commits are disabled in the preview process; hover, focus, search, scrolling, view switching and expansion remain available. The application binary and source are not modified.

Each scenario produces a PNG using WinForms `DrawToBitmap` plus a JSON control tree with actual DPI, bounds and `OutsideParent` flags. Flow content may intentionally extend beneath its scroll viewport and is identified separately. `UNKNOWN` until inspected: a bounds flag alone cannot prove text clipping or a visual regression. Bitmap rendering does not verify desktop drag/merge/restore, animation, real monitor DPI changes, or Shell integration. Native screenshots and interaction remain necessary for those cases.

Fixtures are retained for inspection. To clean up, first close the harness and resolve the exact printed `PREVIEW_OUTPUT` path. Verify that the directory is a direct `run-*` child of the intended `outputs\ui-preview` directory and contains `PREVIEW-FIXTURE.txt`; remove only that explicit directory with PowerShell `Remove-Item -LiteralPath ... -Recurse`. No cleanup of APPDATA, real desktop members, registry entries or the output root is performed by the harness.

`--render-all` opts the harness into system-DPI awareness, then verifies the baseline content geometry plus the softened translucent 140×44px search-gradient action group, four 32×36px action controls and aligned right edges. It also checks the exact saturated title colors, single-pass continuous-perimeter search mode, selected/keyboard states without a square outline, immediate close without a shrink overlay, resize without an auxiliary composition window, opening search focus, 20 identical repaints for each action in idle/hover/pressed/disabled states, and hot/pressed/Expand-to-Collapse transitions painted successively onto the same bitmap. Remaining checks cover selected-view accessibility, temporary-position bookkeeping and anchor suppression, filtering/clearing, scrollbar states, keyboard visibility, Settings controls and diagnostics access.

Launching the harness without arguments opens a Vietnamese compact preview using `DesktopFolders-preview.exe` beside the harness. This supports native window inspection tools that launch executables without arguments.
