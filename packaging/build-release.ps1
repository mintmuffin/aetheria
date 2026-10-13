$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$DotnetExe = "C:\Users\Administrator\.dotnet\dotnet.exe"
if (-not (Test-Path $DotnetExe)) {
    $DotnetExe = "dotnet"
}

$SdkBinDir = "C:\Users\Administrator\.nuget\packages\microsoft.windows.sdk.buildtools\10.0.22621.3233\bin\10.0.22621.0\x64"
$MakePriExe = Join-Path $SdkBinDir "makepri.exe"
$MakeAppxExe = Join-Path $SdkBinDir "makeappx.exe"

$DistDir = Join-Path $RepoRoot "dist"
$PortableDir = Join-Path $DistDir "Aetheria-Portable-win-x64"
$MsixStagingDir = Join-Path $RepoRoot "packaging\msix-staging"
$MsixPath = Join-Path $DistDir "Aetheria-win-x64.msix"
$MsiPath = Join-Path $DistDir "Aetheria-Setup-win-x64.msi"
$PortableZipPath = Join-Path $DistDir "Aetheria-Portable-win-x64.zip"

Write-Host "=== [1/6] Running Automated xUnit Test Suite ===" -ForegroundColor Cyan
& $DotnetExe test (Join-Path $RepoRoot "tests\Aetheria.Tests\Aetheria.Tests.csproj") -c Release

Write-Host "=== [2/6] Publishing Self-Contained WinUI 3 Release Build ===" -ForegroundColor Cyan
if (Test-Path $PortableDir) { Remove-Item $PortableDir -Recurse -Force }
New-Item -ItemType Directory -Path $PortableDir -Force | Out-Null

& $DotnetExe publish (Join-Path $RepoRoot "src\Aetheria.App\Aetheria.App.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:Platform=x64 `
    -o $PortableDir

# Copy Sample Roleplay Packages into Portable folder for immediate out-of-the-box import
$SamplesOut = Join-Path $PortableDir "SamplePackages"
New-Item -ItemType Directory -Path $SamplesOut -Force | Out-Null

Write-Host "=== [3/6] Running WinUI 3 Runtime Smoke Test ===" -ForegroundColor Cyan
$AppExe = Join-Path $PortableDir "Aetheria.exe"
if (-not (Test-Path $AppExe)) {
    throw "Aetheria.exe was not found in $PortableDir"
}
$SmokeFile = Join-Path $env:LOCALAPPDATA "Aetheria\smoke_test_ok.txt"
$StartupErrFile = Join-Path $env:LOCALAPPDATA "Aetheria\startup_error.log"
$RuntimeErrFile = Join-Path $env:LOCALAPPDATA "Aetheria\runtime_error.log"
if (Test-Path $SmokeFile) { Remove-Item $SmokeFile -Force }
if (Test-Path $StartupErrFile) { Remove-Item $StartupErrFile -Force }
if (Test-Path $RuntimeErrFile) { Remove-Item $RuntimeErrFile -Force }

$proc = Start-Process -FilePath $AppExe -ArgumentList "--smoke-test" -PassThru
if (-not $proc.WaitForExit(15000)) {
    Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
    throw "Aetheria.exe timed out during smoke test!"
}
if (Test-Path $StartupErrFile) {
    throw "Startup Error: $(Get-Content $StartupErrFile -Raw)"
}
if (Test-Path $RuntimeErrFile) {
    throw "Runtime Error: $(Get-Content $RuntimeErrFile -Raw)"
}
if (-not (Test-Path $SmokeFile)) {
    throw "smoke_test_ok.txt was not created!"
}
Write-Host "Smoke Test Verified: $(Get-Content $SmokeFile -Raw)" -ForegroundColor Green

$LocalAppDataSamples = Join-Path $env:LOCALAPPDATA "Aetheria\Samples"
if (Test-Path $LocalAppDataSamples) {
    Copy-Item (Join-Path $LocalAppDataSamples "*") $SamplesOut -Force
}

$Tut1 = Join-Path $RepoRoot "TUTORIAL_1_HOST_AND_KOBOLDCPP_GUIDE.md"
$Tut2 = Join-Path $RepoRoot "TUTORIAL_2_GIRLFRIEND_INSTALLATION_AND_COUPLE_COOP_GUIDE.md"
if (Test-Path $Tut1) {
    Copy-Item $Tut1 $PortableDir -Force
    Copy-Item $Tut1 $DistDir -Force
}
if (Test-Path $Tut2) {
    Copy-Item $Tut2 $PortableDir -Force
    Copy-Item $Tut2 $DistDir -Force
}

Write-Host "=== [4/6] Creating Portable ZIP Archive ===" -ForegroundColor Cyan
if (Test-Path $PortableZipPath) { Remove-Item $PortableZipPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($PortableDir, $PortableZipPath)

Write-Host "=== [5/6] Building Windows 11 MSIX Package (makeappx.exe) ===" -ForegroundColor Cyan
if (Test-Path $MsixStagingDir) { Remove-Item $MsixStagingDir -Recurse -Force }
New-Item -ItemType Directory -Path $MsixStagingDir -Force | Out-Null
Copy-Item -Path "$PortableDir\*" -Destination $MsixStagingDir -Recurse -Force

$AssetsDir = Join-Path $MsixStagingDir "Assets"
New-Item -ItemType Directory -Path $AssetsDir -Force | Out-Null

# Generate crisp Fluent PNG visual assets for MSIX manifest
Add-Type -AssemblyName System.Drawing
function New-FluentAssetPng($Path, [int]$W, [int]$H) {
    $bmp = New-Object System.Drawing.Bitmap($W, $H)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $rect = New-Object System.Drawing.Rectangle(0, 0, $W, $H)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 28, 25, 54),
        [System.Drawing.Color]::FromArgb(255, 88, 62, 168),
        45.0)
    $g.FillRectangle($brush, $rect)
    $fontSize = [Math]::Max(10, [int]($H * 0.36))
    $font = New-Object System.Drawing.Font("Segoe UI Semibold", $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center
    $textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 240, 238, 255))
    $g.DrawString("AE", $font, $textBrush, [System.Drawing.RectangleF]$rect, $sf)
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose()
    $bmp.Dispose()
}

New-FluentAssetPng (Join-Path $AssetsDir "StoreLogo.png") 50 50
New-FluentAssetPng (Join-Path $AssetsDir "Square44x44Logo.png") 44 44
New-FluentAssetPng (Join-Path $AssetsDir "Square150x150Logo.png") 150 150
New-FluentAssetPng (Join-Path $AssetsDir "Wide310x150Logo.png") 310 150

$AppxManifestContent = @"
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap rescap">

  <Identity
    Name="AetheriaStudio.AetheriaRoleplayPlatform"
    Publisher="CN=AetheriaStudio"
    Version="1.0.1.0"
    ProcessorArchitecture="x64" />

  <Properties>
    <DisplayName>Aetheria AI Roleplay Platform</DisplayName>
    <PublisherDisplayName>Aetheria Studio</PublisherDisplayName>
    <Logo>Assets\StoreLogo.png</Logo>
    <Description>Native Windows 11 AI Roleplay &amp; Co-Op Narrative Platform built with WinUI 3.</Description>
  </Properties>

  <Dependencies>
    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.22631.0" />
  </Dependencies>

  <Resources>
    <Resource Language="en-US" />
  </Resources>

  <Applications>
    <Application Id="App" Executable="Aetheria.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements
        DisplayName="Aetheria Roleplay Platform"
        Description="Windows 11 Single-Player &amp; Multiplayer AI Roleplay Studio"
        BackgroundColor="#1C1936"
        Square150x150Logo="Assets\Square150x150Logo.png"
        Square44x44Logo="Assets\Square44x44Logo.png">
        <uap:DefaultTile Wide310x150Logo="Assets\Wide310x150Logo.png" />
      </uap:VisualElements>
    </Application>
  </Applications>

  <Capabilities>
    <rescap:Capability Name="runFullTrust" />
    <Capability Name="internetClient" />
    <Capability Name="internetClientServer" />
    <Capability Name="privateNetworkClientServer" />
  </Capabilities>
</Package>
"@

Set-Content -Path (Join-Path $MsixStagingDir "AppxManifest.xml") -Value $AppxManifestContent -Encoding UTF8

if (Test-Path $MsixPath) { Remove-Item $MsixPath -Force }
& $MakeAppxExe pack /d $MsixStagingDir /p $MsixPath /nv /o
Remove-Item $MsixStagingDir -Recurse -Force

# Sign MSIX with a Trusted Root Certificate so Windows App Installer never complains about untrusted root cert
$SignToolExe = Join-Path $SdkBinDir "signtool.exe"
$CertCerPath = Join-Path $DistDir "Aetheria-SigningCert.cer"
$CertPfxPath = Join-Path $env:TEMP "Aetheria-SigningCert.pfx"

$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq "CN=AetheriaStudio" -and $_.NotAfter -gt (Get-Date) } | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate `
        -Type Custom `
        -Subject "CN=AetheriaStudio" `
        -KeyUsage DigitalSignature `
        -FriendlyName "Aetheria Studio Windows 11 Package Certificate" `
        -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") `
        -NotAfter (Get-Date).AddYears(5)
}
Export-Certificate -Cert $cert -FilePath $CertCerPath -Force | Out-Null
try {
    Import-Certificate -FilePath $CertCerPath -CertStoreLocation "Cert:\LocalMachine\Root" -ErrorAction SilentlyContinue | Out-Null
    Import-Certificate -FilePath $CertCerPath -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" -ErrorAction SilentlyContinue | Out-Null
} catch {}
try {
    Import-Certificate -FilePath $CertCerPath -CertStoreLocation "Cert:\CurrentUser\TrustedPeople" -ErrorAction SilentlyContinue | Out-Null
} catch {}

$pfxPass = ConvertTo-SecureString -String "Aetheria2026!" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $CertPfxPath -Password $pfxPass -Force | Out-Null

if (Test-Path $SignToolExe) {
    & $SignToolExe sign /fd SHA256 /f $CertPfxPath /p "Aetheria2026!" $MsixPath
}

Write-Host "=== [6/7] Building Native Windows MSI Installer ($MsiPath) ===" -ForegroundColor Cyan
& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "New-AetheriaMsi.ps1") -SourceDir $PortableDir -OutputMsi $MsiPath -PayloadZip $PortableZipPath
if (Test-Path $SignToolExe) {
    & $SignToolExe sign /fd SHA256 /f $CertPfxPath /p "Aetheria2026!" $MsiPath
}

$SetupExePath = Join-Path $DistDir "Aetheria-Setup-win-x64.exe"
Write-Host "=== [7/7] Building Standalone Windows 11 GUI Setup EXE ($SetupExePath) ===" -ForegroundColor Cyan
& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "New-AetheriaSetupExe.ps1") -PayloadZip $PortableZipPath -OutputExe $SetupExePath
if (Test-Path $SignToolExe) {
    & $SignToolExe sign /fd SHA256 /f $CertPfxPath /p "Aetheria2026!" $SetupExePath
}

Remove-Item $CertPfxPath -Force -ErrorAction SilentlyContinue

Write-Host "=== Release Build Complete ===" -ForegroundColor Green
Get-ChildItem $DistDir | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
