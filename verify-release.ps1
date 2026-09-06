param(
    [string]$Path = (Join-Path $PSScriptRoot "DesktopFolders.exe"),
    [switch]$RequireSignature,
    [switch]$RequireTimestamp
)

$ErrorActionPreference = "Stop"
$resolved = (Resolve-Path -LiteralPath $Path).Path
$assembly = [Reflection.Assembly]::LoadFile($resolved)
$requiredResources = @(
    "DesktopFolders.CollectionBackground.png",
    "DesktopFolders.Resources.strings.en.json",
    "DesktopFolders.Resources.strings.vi.json"
)
$allowedReferences = @(
    "mscorlib", "System", "System.Core", "System.Drawing", "System.Web.Extensions",
    "System.Windows.Forms", "UIAutomationClient", "UIAutomationTypes", "WindowsBase"
)

$resources = $assembly.GetManifestResourceNames()
$missingResources = $requiredResources | Where-Object { $_ -notin $resources }
if ($missingResources) { throw "Missing embedded resources: $($missingResources -join ', ')" }

$references = $assembly.GetReferencedAssemblies() | ForEach-Object { $_.Name }
$unexpectedReferences = $references | Where-Object { $_ -notin $allowedReferences }
if ($unexpectedReferences) { throw "Unexpected runtime dependencies: $($unexpectedReferences -join ', ')" }

$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($resolved)
if ($version.FileVersion -match '^6(?:\.|$)' -or $version.ProductVersion -match '^6(?:\.|$)') { throw "Obsolete version metadata detected" }

$signature = Get-AuthenticodeSignature -LiteralPath $resolved
if ($RequireSignature -and $signature.Status -ne [Management.Automation.SignatureStatus]::Valid) { throw "Authenticode signature is not valid: $($signature.Status)" }
if ($RequireTimestamp -and $signature.Status -ne [Management.Automation.SignatureStatus]::Valid) { throw "A valid Authenticode signature is required before checking its timestamp" }
if ($RequireTimestamp -and -not $signature.TimeStamperCertificate) { throw "Authenticode signature does not contain a timestamp" }

$hash = Get-FileHash -LiteralPath $resolved -Algorithm SHA256
[pscustomobject]@{
    Path = $resolved
    FileVersion = $version.FileVersion
    EmbeddedResources = $resources.Count
    RuntimeReferences = $references -join ", "
    Signature = $signature.Status
    Signer = if ($signature.SignerCertificate) { $signature.SignerCertificate.Subject } else { $null }
    TimestampAuthority = if ($signature.TimeStamperCertificate) { $signature.TimeStamperCertificate.Subject } else { $null }
    SHA256 = $hash.Hash
}
