$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) { throw '開發流程需要 Docker Compose 啟動本機 PostgreSQL。' }
docker compose -f "$Root/compose.yaml" up -d --wait
dotnet ef database update --project "$Root/src/Workspace.Infrastructure" --startup-project "$Root/src/Workspace.Web"
$env:ASPNETCORE_ENVIRONMENT = 'Development'
$env:ASPNETCORE_URLS = 'http://localhost:5080'
Start-Process dotnet -ArgumentList @('run', '--no-launch-profile', '--project', "$Root/src/Workspace.Web") -NoNewWindow
npm run dev --prefix "$Root/src/Workspace.Client"
