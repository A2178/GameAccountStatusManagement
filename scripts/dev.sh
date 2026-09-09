#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
command -v docker >/dev/null || { echo "錯誤：開發流程需要 Docker Compose 啟動本機 PostgreSQL。" >&2; exit 1; }
docker compose -f "$root/compose.yaml" up -d --wait
dotnet ef database update --project "$root/src/Workspace.Infrastructure" --startup-project "$root/src/Workspace.Web"
trap 'kill 0' EXIT INT TERM
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:5080 dotnet run --no-launch-profile --project "$root/src/Workspace.Web" &
npm run dev --prefix "$root/src/Workspace.Client" &
wait
