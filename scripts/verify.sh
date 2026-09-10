#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet restore "$root/Workspace.slnx"
dotnet build "$root/Workspace.slnx" --no-restore
dotnet test "$root/Workspace.slnx" --no-build
npm install --prefix "$root/src/Workspace.Client"
npm run typecheck --prefix "$root/src/Workspace.Client"
npm test --prefix "$root/src/Workspace.Client"
npm run build --prefix "$root/src/Workspace.Client"
