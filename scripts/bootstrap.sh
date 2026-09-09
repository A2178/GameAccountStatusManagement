#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

command -v dotnet >/dev/null || { echo "錯誤：需要 .NET SDK 10.0.100 以上版本。" >&2; exit 1; }
command -v node >/dev/null || { echo "錯誤：需要 Node.js 20。" >&2; exit 1; }
command -v npm >/dev/null || { echo "錯誤：找不到 npm。" >&2; exit 1; }

dotnet_major="$(dotnet --version | cut -d. -f1)"
node_major="$(node --version | sed 's/^v//' | cut -d. -f1)"
[[ "$dotnet_major" == "10" ]] || { echo "錯誤：目前 .NET SDK 不是 10.x。" >&2; exit 1; }
[[ "$node_major" == "20" ]] || { echo "錯誤：目前 Node.js 不是 20.x。" >&2; exit 1; }

dotnet restore "$root/Workspace.slnx"
dotnet tool restore
npm install --prefix "$root/src/Workspace.Client"
echo "開發依賴已準備完成。"
