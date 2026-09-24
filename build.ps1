param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$artifacts = Join-Path $root "artifacts"
$output = Join-Path $artifacts "publish"
$version = "1.0.0"
$zip = Join-Path $artifacts "NAT64Switch-$Runtime-v$version.zip"

dotnet publish (Join-Path $root "NAT64Switch.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -o $output

New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
Compress-Archive -Path (Join-Path $output "*") -DestinationPath $zip -Force

Write-Host "发布完成：$zip"
