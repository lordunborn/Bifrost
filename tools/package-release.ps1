<#
.SYNOPSIS
  Builds the Omega Launcher release files: a Windows zip and a Linux tar.gz, plus the raw programs for `publish-launcher`.

.PARAMETER Version
  Release version, for example 1.0.1. It is stamped into the program and used in the file names.

.PARAMETER DeploymentFile
  Your filled-in Mho.Deployment.json (server address, public key, content token secret). It is copied INTO the release files and never into this repository.
  Without it the release is a plain launcher with no server and no updater.

.PARAMETER Out
  Output folder (default: .\release). Keep it outside any folder you commit.

.EXAMPLE
  .\tools\package-release.ps1 -Version 1.0.1 -DeploymentFile C:\Private\Mho.Deployment.json -Out C:\Private\release
#>
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [string]$DeploymentFile = '',
    [string]$Out = '.\release'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'OmegaLauncher.Avalonia\OmegaLauncher.Avalonia.csproj'

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.0.1 (got '$Version')." }
if ($DeploymentFile -ne '' -and -not (Test-Path $DeploymentFile)) { throw "Deployment file not found: $DeploymentFile" }
if ($DeploymentFile -eq '') { Write-Warning 'No deployment file: this release has no server and no updater.' }

$out = New-Item -ItemType Directory -Force -Path $Out
$programs = New-Item -ItemType Directory -Force -Path (Join-Path $out 'programs')
$work = Join-Path $out 'work'
if (Test-Path $work) { Remove-Item $work -Recurse -Force }

$playerReadme = @"
Omega Launcher $Version

1. You need the stock Marvel Heroes client (version 1.53.0.203, for example from Steam).
2. Run the launcher. It finds the game, or asks you for its folder.
3. Press Update when it asks, then Play.

The launcher is not code-signed, so antivirus software may warn about it. The full guide is in docs/PLAYERS.md of the project.
"@

foreach ($platform in @('win-x64', 'linux-x64')) {
    Write-Host "== $platform"
    $publishDir = Join-Path $work $platform
    dotnet publish $project -c Release -r $platform --self-contained `
        -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none `
        -p:AssemblyVersion="$Version.0" -p:FileVersion="$Version.0" -v q -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $platform" }

    $programName = if ($platform -eq 'win-x64') { 'OmegaLauncher.exe' } else { 'OmegaLauncher' }
    $built = Join-Path $publishDir $programName
    Copy-Item $built (Join-Path $programs "OmegaLauncher-$platform$([IO.Path]::GetExtension($programName))") -Force

    $package = Join-Path $work "package-$platform"
    New-Item -ItemType Directory -Force -Path $package | Out-Null
    Copy-Item $built $package
    Set-Content (Join-Path $package 'README.txt') $playerReadme
    if ($DeploymentFile -ne '') { Copy-Item $DeploymentFile (Join-Path $package 'Mho.Deployment.json') }

    if ($platform -eq 'win-x64') {
        $zip = Join-Path $out "OmegaLauncher-$Version-win-x64.zip"
        if (Test-Path $zip) { Remove-Item $zip }
        Compress-Archive -Path (Join-Path $package '*') -DestinationPath $zip
        Write-Host "   $zip"
    }
    else {
        $start = "#!/bin/sh`ncd `"`$(dirname `"`$0`")`"`nchmod +x ./OmegaLauncher 2>/dev/null`nexec ./OmegaLauncher `"`$@`"`n"
        [IO.File]::WriteAllText((Join-Path $package 'start-omega-launcher.sh'), $start.Replace("`r`n", "`n"))
        $tar = Join-Path $out "OmegaLauncher-$Version-linux-x64.tar.gz"
        if (Test-Path $tar) { Remove-Item $tar }

        # Written with .NET's tar writer so the program and the start script keep their executable bit (a Windows-made archive would lose it).
        $executable = [System.IO.UnixFileMode]'UserRead,UserWrite,UserExecute,GroupRead,GroupExecute,OtherRead,OtherExecute'
        $plain = [System.IO.UnixFileMode]'UserRead,UserWrite,GroupRead,OtherRead'
        $fileStream = [System.IO.File]::Create($tar)
        $gzip = New-Object System.IO.Compression.GZipStream($fileStream, [System.IO.Compression.CompressionLevel]::SmallestSize)
        $writer = New-Object System.Formats.Tar.TarWriter($gzip, [System.Formats.Tar.TarEntryFormat]::Pax)
        try {
            foreach ($file in Get-ChildItem $package -File) {
                $entry = New-Object System.Formats.Tar.PaxTarEntry([System.Formats.Tar.TarEntryType]::RegularFile, "OmegaLauncher/$($file.Name)")
                $entry.Mode = if ($file.Name -in @('OmegaLauncher', 'start-omega-launcher.sh')) { $executable } else { $plain }
                $entry.DataStream = [System.IO.File]::OpenRead($file.FullName)
                $writer.WriteEntry($entry)
                $entry.DataStream.Dispose()
            }
        }
        finally {
            $writer.Dispose()
            $gzip.Dispose()
            $fileStream.Dispose()
        }
        Write-Host "   $tar"
    }
}

Remove-Item $work -Recurse -Force
Write-Host ''
Write-Host "Release files are in $out"
Write-Host "For self-update, publish the programs too:"
Write-Host "  OmegaLauncher.Publisher publish-launcher $programs\OmegaLauncher-win-x64.exe --platform win-x64 --version $Version --key <private.pem> --out host"
Write-Host "  OmegaLauncher.Publisher publish-launcher $programs\OmegaLauncher-linux-x64 --platform linux-x64 --version $Version --key <private.pem> --out host"
