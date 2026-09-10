#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
rm -rf "$root/artifacts/web"
npm install --prefix "$root/src/Workspace.Client"
dotnet publish "$root/src/Workspace.Web" -c Release -o "$root/artifacts/web"
echo "發布成果：$root/artifacts/web"
