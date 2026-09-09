$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
Remove-Item "$Root/artifacts/web" -Recurse -Force -ErrorAction SilentlyContinue
npm install --prefix "$Root/src/Workspace.Client"
dotnet publish "$Root/src/Workspace.Web" -c Release -o "$Root/artifacts/web"
Write-Host "發布成果：$Root/artifacts/web"
