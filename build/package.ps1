<#
.SYNOPSIS
    Builds the release publish output and, when Inno Setup is available, the installer.

.EXAMPLE
    .\build\package.ps1
    .\build\package.ps1 -Version 0.2.0
    .\build\package.ps1 -SkipInstaller
#>
param(
    [string]$Version,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Blackout.Windows\Blackout.Windows.csproj'
$publishDir = Join-Path $root 'artifacts\publish'

if (-not $Version) {
    [xml]$projectXml = Get-Content $project
    $Version = ($projectXml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
    if (-not $Version) {
        $Version = '0.1.0'
    }
}

Write-Host "Version: $Version"

if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

# Self-contained single file: the target machine must not need a .NET runtime.
dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:Version=$Version `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Get-ChildItem $publishDir -File | Measure-Object -Property Length -Sum |
    ForEach-Object { Write-Host ("Published: {0} files, {1:N1} MB" -f $_.Count, ($_.Sum / 1MB)) }

if ($SkipInstaller) {
    return
}

$isccPath = (Get-Command iscc.exe -ErrorAction SilentlyContinue).Source
if (-not $isccPath) {
    $isccPath = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}

if (-not $isccPath) {
    Write-Warning 'Inno Setup 6 not found. Install it: winget install JRSoftware.InnoSetup'
    Write-Warning "Publish output is in $publishDir"
    return
}

Write-Host "ISCC: $isccPath"
& $isccPath "/DMyAppVersion=$Version" (Join-Path $root 'installer\Blackout.iss')
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE"
}

Get-ChildItem (Join-Path $root 'artifacts') -Filter '*.exe' |
    ForEach-Object { Write-Host ("Installer: {0} ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB)) }
