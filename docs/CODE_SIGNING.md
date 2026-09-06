# Code signing releases

Public releases must use an Authenticode code-signing certificate issued by a CA trusted by Windows. A self-signed certificate is useful only for local testing and does not establish trust on other computers.

## Sign locally

Install the certificate and its private key in `Cert:\CurrentUser\My`, then run:

```powershell
.\build.ps1
.\sign-release.ps1 -CertificateThumbprint "YOUR_CERTIFICATE_THUMBPRINT"
.\verify-release.ps1 -RequireSignature -RequireTimestamp
```

`sign-release.ps1` requires the Code Signing EKU, signs with SHA-256, obtains an RFC 3161 SHA-256 timestamp, and verifies the resulting executable. Install the Windows SDK if `signtool.exe` is not already available.

Never commit a `.pfx`, `.p12`, password, private key, or encoded certificate bundle. The repository ignores PFX and P12 files as a last line of defense, but ignored files still need secure storage and access control.

## Configure GitHub Actions

1. Obtain an OV or EV Authenticode certificate from a Windows-trusted certificate authority and export it as a password-protected PFX if the provider permits export. Hardware-token and cloud-HSM certificates require the provider's signing integration instead of the PFX workflow.
2. Convert the PFX to a single-line Base64 value locally:

   ```powershell
   [Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\secure\code-signing.pfx")) | Set-Clipboard
   ```

3. In the repository, create a `release-signing` environment. Require a reviewer and restrict deployment branches/tags as appropriate.
4. Add these environment secrets:
   - `CODE_SIGNING_CERTIFICATE_BASE64`: the Base64 PFX value.
   - `CODE_SIGNING_CERTIFICATE_PASSWORD`: the PFX password.
5. Push a protected version tag such as `v1.0.1`. `.github/workflows/release.yml` builds, imports the certificate into the ephemeral runner, signs and timestamps the EXE, verifies the signature, publishes a SHA-256 checksum, and creates the GitHub Release.

The manual workflow trigger produces a signed Actions artifact but deliberately does not create a GitHub Release.

## Defender and SmartScreen expectations

Authenticode establishes publisher identity and detects post-signing modification. It does not guarantee that Microsoft Defender or SmartScreen will allow every file. New certificates and low-download releases may still show reputation warnings. Keep the publisher name stable, timestamp every release, avoid packers or obfuscators, and submit false positives to Microsoft rather than weakening or bypassing security checks.
