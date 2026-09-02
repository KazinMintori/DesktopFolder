param(
    [switch]$TraceDrag,
    [switch]$TestBuild,
    [string]$Output = "DesktopFolders.exe"
)

$compiler = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$gac = "C:\Windows\Microsoft.NET\assembly\GAC_MSIL"
$references = @(
    "System.dll",
    "System.Core.dll",
    "System.Drawing.dll",
    "System.Windows.Forms.dll",
    "System.Web.Extensions.dll",
    "$gac\WindowsBase\v4.0_4.0.0.0__31bf3856ad364e35\WindowsBase.dll",
    "$gac\UIAutomationClient\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationClient.dll",
    "$gac\UIAutomationTypes\v4.0_4.0.0.0__31bf3856ad364e35\UIAutomationTypes.dll"
)

if (-not (Test-Path -LiteralPath $compiler)) { throw "Không tìm thấy .NET Framework C# compiler: $compiler" }
if (-not (Test-Path -LiteralPath ".\DesktopFolders.ico")) {
    if (-not (Test-Path -LiteralPath ".\generate-icon.ps1")) { throw "Thiếu DesktopFolders.ico và generate-icon.ps1" }
    & ".\generate-icon.ps1"
}
$arguments = @("/nologo", "/target:winexe", "/optimize+", "/platform:anycpu", "/win32icon:DesktopFolders.ico", "/out:$Output")
$symbols = @()
if ($TraceDrag) { $symbols += "TRACE_DRAG" }
if ($TestBuild) { $symbols += "TEST" }
if ($symbols.Count -gt 0) { $arguments += "/define:$($symbols -join ';')" }
$arguments += $references | ForEach-Object { "/reference:$_" }
$arguments += "DirectDesktopFolders.cs"

& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw "Build thất bại với exit code $LASTEXITCODE" }
Get-Item -LiteralPath $Output | Select-Object FullName, Length, LastWriteTime