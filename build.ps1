#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$project = Join-Path $root 'src\K400Fn\K400Fn.csproj'
$dist = Join-Path $root 'dist'
$artifacts = Join-Path $root 'artifacts'

if (Test-Path $dist) {
    Remove-Item $dist -Recurse -Force
}
New-Item -ItemType Directory -Force $dist | Out-Null
New-Item -ItemType Directory -Force $artifacts | Out-Null

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=true `
    -o $dist

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Copy-Item (Join-Path $root 'packaging\*.cmd') $dist
Copy-Item (Join-Path $root 'README.md') $dist

$zip = Join-Path $artifacts 'k400-fn-switch-win-x64.zip'
if (Test-Path $zip) {
    Remove-Item $zip -Force
}

Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip
Write-Host "Built $zip"

$linuxTar = Join-Path $artifacts 'k400-fn-switch-linux.tar.gz'
if (Test-Path $linuxTar) {
    Remove-Item $linuxTar -Force
}

# Use git archive so the executable bits on the Linux scripts are preserved.
git archive --format=tgz --output="$linuxTar" HEAD linux
if ($LASTEXITCODE -ne 0) {
    throw "git archive failed with exit code $LASTEXITCODE"
}
Write-Host "Built $linuxTar"
