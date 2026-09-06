param(
    [switch]$TraceDrag,
    [switch]$TestBuild,
    [string]$Output = "DesktopFolders.exe"
)

$compiler = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$gac = "C:\Windows\Microsoft.NET\assembly\GAC_MSIL"
$sourceFile = Join-Path $PSScriptRoot "src\DesktopFolders.cs"
$backgroundAsset = Join-Path $PSScriptRoot "assets\CollectionBackground.png"
$iconAsset = Join-Path $PSScriptRoot "assets\DesktopFolders.ico"
$outputPath = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $PSScriptRoot $Output }
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
$compositionAvailable = $false
$windowsRuntime = Get-ChildItem -LiteralPath "$gac\System.Runtime.WindowsRuntime" -Recurse -Filter "System.Runtime.WindowsRuntime.dll" -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
$systemRuntime = Get-ChildItem -LiteralPath "$gac\System.Runtime" -Recurse -Filter "System.Runtime.dll" -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
$vectorsRuntime = Get-ChildItem -LiteralPath "$gac\System.Numerics.Vectors" -Recurse -Filter "System.Numerics.Vectors.dll" -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
$numericsRuntime = Get-ChildItem -LiteralPath "$gac\System.Numerics" -Recurse -Filter "System.Numerics.dll" -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
$windowsWinMd = Get-ChildItem -LiteralPath "C:\Program Files (x86)\Windows Kits\10\UnionMetadata" -Recurse -Filter "Windows.winmd" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
$universalContract = Get-ChildItem -LiteralPath "C:\Program Files (x86)\Windows Kits\10\References" -Recurse -Filter "Windows.Foundation.UniversalApiContract.winmd" -ErrorAction SilentlyContinue | Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
if ($windowsRuntime -and $systemRuntime -and $numericsRuntime -and $vectorsRuntime -and $windowsWinMd -and $universalContract) {
    $references += @($windowsRuntime, $systemRuntime, $numericsRuntime, $vectorsRuntime, $windowsWinMd, $universalContract)
    $compositionAvailable = $true
}

if (-not (Test-Path -LiteralPath $compiler)) { throw "Không tìm thấy .NET Framework C# compiler: $compiler" }
if (-not (Test-Path -LiteralPath $sourceFile)) { throw "Thiếu src\DesktopFolders.cs" }
if (-not (Test-Path -LiteralPath $backgroundAsset)) { throw "Thiếu assets\CollectionBackground.png" }
$stringsEn = Join-Path $PSScriptRoot "Resources\strings.en.json"
$stringsVi = Join-Path $PSScriptRoot "Resources\strings.vi.json"
if (-not (Test-Path -LiteralPath $stringsEn)) { throw "Thiếu Resources\strings.en.json" }
if (-not (Test-Path -LiteralPath $stringsVi)) { throw "Thiếu Resources\strings.vi.json" }
if (-not (Test-Path -LiteralPath $iconAsset)) {
    $iconGenerator = Join-Path $PSScriptRoot "generate-icon.ps1"
    if (-not (Test-Path -LiteralPath $iconGenerator)) { throw "Thiếu assets\DesktopFolders.ico và generate-icon.ps1" }
    & $iconGenerator
}
$arguments = @("/nologo", "/target:winexe", "/optimize+", "/platform:anycpu", "/win32icon:$iconAsset", "/out:$outputPath")
$arguments += "/resource:$backgroundAsset,DesktopFolders.CollectionBackground.png"
$arguments += "/resource:$stringsEn,DesktopFolders.Resources.strings.en.json"
$arguments += "/resource:$stringsVi,DesktopFolders.Resources.strings.vi.json"
$symbols = @()
if ($TraceDrag) { $symbols += "TRACE_DRAG" }
if ($TestBuild) { $symbols += "TEST" }
if ($compositionAvailable) { $symbols += "WINDOWS_COMPOSITION" }
if ($symbols.Count -gt 0) { $arguments += "/define:$($symbols -join ';')" }
$arguments += $references | ForEach-Object { "/reference:$_" }
$arguments += $sourceFile

& $compiler $arguments
if ($LASTEXITCODE -ne 0) { throw "Build thất bại với exit code $LASTEXITCODE" }
Get-Item -LiteralPath $outputPath | Select-Object FullName, Length, LastWriteTime
