param(
    [string]$Path = (Join-Path $PSScriptRoot "DesktopFolders.exe"),
    [string]$CertificateThumbprint = $env:SIGNING_CERT_THUMBPRINT,
    [string]$TimestampUrl = "https://timestamp.digicert.com",
    [string]$SignToolPath
)

$ErrorActionPreference = "Stop"

function Find-SignTool {
    param([string]$ExplicitPath)

    if ($ExplicitPath) {
        if (-not (Test-Path -LiteralPath $ExplicitPath -PathType Leaf)) {
            throw "SignTool không tồn tại: $ExplicitPath"
        }
        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    $command = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $kitsBin = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (Test-Path -LiteralPath $kitsBin) {
        $candidate = Get-ChildItem -LiteralPath $kitsBin -Directory |
            Sort-Object { try { [version]$_.Name } catch { [version]"0.0" } } -Descending |
            ForEach-Object { Join-Path $_.FullName "x64\signtool.exe" } |
            Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
            Select-Object -First 1
        if ($candidate) { return $candidate }
    }

    throw "Không tìm thấy signtool.exe. Hãy cài Windows SDK hoặc truyền -SignToolPath."
}

if (-not $CertificateThumbprint) {
    throw "Thiếu thumbprint. Truyền -CertificateThumbprint hoặc đặt SIGNING_CERT_THUMBPRINT."
}

$resolved = (Resolve-Path -LiteralPath $Path).Path
$thumbprint = $CertificateThumbprint.Replace(" ", "").ToUpperInvariant()
$certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$thumbprint" -ErrorAction SilentlyContinue
if (-not $certificate) { throw "Không tìm thấy chứng thư $thumbprint trong Cert:\CurrentUser\My." }
if (-not $certificate.HasPrivateKey) { throw "Chứng thư $thumbprint không có private key." }
if ($certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -lt (Get-Date)) {
    throw "Chứng thư $thumbprint chưa có hiệu lực hoặc đã hết hạn."
}
$codeSigningOid = "1.3.6.1.5.5.7.3.3"
$hasCodeSigningEku = $certificate.Extensions |
    Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } |
    ForEach-Object { $_.EnhancedKeyUsages } |
    Where-Object { $_.Value -eq $codeSigningOid }
if (-not $hasCodeSigningEku) { throw "Chứng thư $thumbprint không có Code Signing EKU." }

$signTool = Find-SignTool -ExplicitPath $SignToolPath
& $signTool sign /sha1 $thumbprint /s My /fd SHA256 /tr $TimestampUrl /td SHA256 /d "DesktopFolders" $resolved
if ($LASTEXITCODE -ne 0) { throw "SignTool sign thất bại với exit code $LASTEXITCODE." }

& $signTool verify /pa /v $resolved
if ($LASTEXITCODE -ne 0) { throw "SignTool verify thất bại với exit code $LASTEXITCODE." }

$signature = Get-AuthenticodeSignature -LiteralPath $resolved
if ($signature.Status -ne [Management.Automation.SignatureStatus]::Valid) {
    throw "Authenticode signature không hợp lệ: $($signature.Status)."
}
if (-not $signature.TimeStamperCertificate) { throw "Chữ ký không có timestamp." }

[pscustomobject]@{
    Path = $resolved
    Signer = $signature.SignerCertificate.Subject
    Thumbprint = $signature.SignerCertificate.Thumbprint
    TimestampAuthority = $signature.TimeStamperCertificate.Subject
    Status = $signature.Status
    SHA256 = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash
}
