param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$artifacts = Join-Path $root "artifacts"
$output = Join-Path $artifacts "publish"
$version = "1.1.3"
$zip = Join-Path $artifacts "NAT64Switch-$Runtime-v$version.zip"

dotnet publish (Join-Path $root "NAT64Switch.csproj") `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -o $output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败，退出码：$LASTEXITCODE" }

Copy-Item (Join-Path $root "NAT64Switch-GUI.ps1") -Destination $output -Force
Copy-Item (Join-Path $root "NAT64Switch-GUI.cmd") -Destination $output -Force

New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
Compress-Archive -Path (Join-Path $output "*") -DestinationPath $zip -Force

Write-Host "发布完成：$zip"
