$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '需要 .NET SDK 10.0.100 以上版本。' }
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { throw '需要 Node.js 20。' }
if ((dotnet --version).Split('.')[0] -ne '10') { throw '目前 .NET SDK 不是 10.x。' }
if ((node --version).TrimStart('v').Split('.')[0] -ne '20') { throw '目前 Node.js 不是 20.x。' }
dotnet restore "$Root/Workspace.slnx"
dotnet tool restore
npm install --prefix "$Root/src/Workspace.Client"
Write-Host '開發依賴已準備完成。'
