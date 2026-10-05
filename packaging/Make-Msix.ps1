<#
.SYNOPSIS
    Builds the MSIX package for Microsoft Store submission.
.DESCRIPTION
    dotnet publish -> packaging\layout -> makeappx pack -> dist\SfUi_<version>_x64.msix
    Identity values (Name / Publisher / PublisherDisplayName) come from
    packaging\AppxManifest.xml (reserved in Partner Center as HKS.SfUi).
    NOTE: This script is intentionally ASCII-only (Windows PowerShell 5.1 reads
    BOM-less UTF-8 scripts as ANSI, which corrupts non-ASCII literals).
.EXAMPLE
    .\Make-Msix.ps1                  # publish + layout + pack
    .\Make-Msix.ps1 -SkipPublish     # reuse existing dist\SfUi.exe
    .\Make-Msix.ps1 -Register        # build layout and register locally (Developer Mode required)
    .\Make-Msix.ps1 -Unregister      # remove the local test install
    .\Make-Msix.ps1 -Version 0.2.1.0 # explicit 4-part version
#>
[CmdletBinding()]
param(
    [string]$Version,
    [switch]$SkipPublish,
    [switch]$Register,
    [switch]$Unregister
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$layout = Join-Path $PSScriptRoot 'layout'
$manifestSrc = Join-Path $PSScriptRoot 'AppxManifest.xml'
$dist = Join-Path $repo 'dist'
$appName = 'HKS.SfUi'
$aumid = 'HKS.SfUi_m66ayswnkrx7j!SfUi'

if ($Unregister) {
    Get-AppxPackage -Name $appName -ErrorAction SilentlyContinue | Remove-AppxPackage
    Write-Host "Unregistered: $appName"
    exit 0
}

# ---- resolve version (4-part) from the csproj unless given explicitly ----
if (-not $Version) {
    [xml]$proj = Get-Content (Join-Path $repo 'src\SfUi.App\SfUi.App.csproj')
    $raw = @($proj.Project.PropertyGroup.Version | Where-Object { $_ })[0]
    if (-not $raw) { $raw = '0.0.0' }
    $raw = $raw -replace '[^0-9\.]', ''
    $parts = @($raw.Split('.') | Where-Object { $_ -ne '' })
    while ($parts.Count -lt 4) { $parts += '0' }
    $Version = ($parts[0..3] -join '.')
}

# ---- publish ----
if (-not $SkipPublish) {
    Write-Host 'Publishing single-file exe ...'
    & dotnet publish (Join-Path $repo 'src\SfUi.App\SfUi.App.csproj') -c Release -r win-x64 `
        --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $dist
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
}

$exe = Join-Path $dist 'SfUi.exe'
if (-not (Test-Path $exe)) { throw "Not found: $exe  (run without -SkipPublish first)" }

# ---- build layout ----
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
New-Item -ItemType Directory -Path (Join-Path $layout 'Assets') -Force | Out-Null
Copy-Item $exe $layout
Copy-Item (Join-Path $PSScriptRoot 'Assets\*.png') (Join-Path $layout 'Assets')

# Set ONLY the Identity Version via XML (regex would also corrupt MinVersion="...")
$doc = New-Object System.Xml.XmlDocument
$doc.PreserveWhitespace = $true
$doc.Load($manifestSrc)
$doc.Package.Identity.Version = $Version
$layoutManifest = Join-Path $layout 'AppxManifest.xml'
$doc.Save($layoutManifest)

# Sanity check: reload and verify what Partner Center validates
$chk = New-Object System.Xml.XmlDocument
$chk.Load($layoutManifest)
if ($chk.Package.Identity.Version -ne $Version) { throw 'Manifest identity version mismatch after save.' }
if ($chk.Package.Dependencies.TargetDeviceFamily.MinVersion -notlike '10.*') {
    throw ("Unexpected MinVersion '{0}' in manifest (Store requires > 10.0.17134.0)." -f $chk.Package.Dependencies.TargetDeviceFamily.MinVersion)
}
Write-Host ("Layout ready (version {0}, MinVersion {1})." -f $Version, $chk.Package.Dependencies.TargetDeviceFamily.MinVersion)

# ---- generate resources.pri (localization + target-size/unplated assets) ----
$makepri = (Get-Command makepri.exe -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if (-not $makepri) {
    $makepri = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d+(\.\d+)+$' } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64\makepri.exe' } |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if ($makepri) {
    $priconfig = Join-Path $PSScriptRoot 'priconfig.xml'
    & $makepri createconfig /cf $priconfig /dq en-US /o | Out-Null
    & $makepri new /pr $layout /cf $priconfig /of (Join-Path $layout 'resources.pri') /o | Out-Null
    Write-Host 'resources.pri generated.'
} else {
    Write-Warning 'makepri.exe not found - skipping resources.pri (unplated assets may be ignored).'
}

# ---- locate makeappx.exe ----
$makeappx = (Get-Command makeappx.exe -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if (-not $makeappx) {
    $makeappx = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d+(\.\d+)+$' } |
        Sort-Object { [version]$_.Name } -Descending |
        ForEach-Object { Join-Path $_.FullName 'x64\makeappx.exe' } |
        Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $makeappx) {
    $found = Get-ChildItem (Join-Path $PSScriptRoot 'tools') -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $makeappx = $found.FullName }
}
if (-not $makeappx) {
    throw 'makeappx.exe not found. Install the Windows SDK, or extract Windows SDK Build Tools into packaging\tools.'
}
Write-Host "makeappx: $makeappx"

# ---- pack ----
$out = Join-Path $dist ('SfUi_{0}_x64.msix' -f $Version)
& $makeappx pack /d $layout /p $out /o
if ($LASTEXITCODE -ne 0) { throw 'makeappx pack failed.' }
Write-Host "MSIX created: $out"

# ---- optional local signing (mirrors the VS temporary-key pattern; Store re-signs anyway) ----
$pfx = Join-Path $PSScriptRoot 'sign\SfUi_TemporaryKey.pfx'
if (Test-Path $pfx) {
    $signtool = (Get-Command signtool.exe -ErrorAction SilentlyContinue | Select-Object -First 1).Source
    if (-not $signtool) {
        $signtool = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin' -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^\d+(\.\d+)+$' } |
            Sort-Object { [version]$_.Name } -Descending |
            ForEach-Object { Join-Path $_.FullName 'x64\signtool.exe' } |
            Where-Object { Test-Path $_ } | Select-Object -First 1
    }
    if ($signtool) {
        & $signtool sign /fd SHA256 /f $pfx /p 'sfui-test' /tr http://timestamp.digicert.com /td SHA256 $out | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'signtool sign failed.' }
        Write-Host 'MSIX signed (local temporary key).'

        # Export the public certificate for users (install to Trusted People before the MSIX)
        $cer = Join-Path $dist ('SfUi_{0}_x64.cer' -f $Version)
        $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfx, 'sfui-test')
        Export-Certificate -Cert $cert -FilePath $cer -Force | Out-Null
        Write-Host "Public certificate exported: $cer"
    } else {
        Write-Warning 'signtool.exe not found - MSIX left unsigned.'
    }
}

# ---- optional local register for testing ----
if ($Register) {
    $dev = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense
    if ($dev -ne 1) {
        throw 'Developer Mode is OFF. Enable it (Settings > System > For developers = ON) and run -Register again.'
    }
    Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml')
    Write-Host "Registered for testing: $appName"
    Write-Host "Launch with: explorer.exe `"shell:appsFolder\$aumid`""
}
