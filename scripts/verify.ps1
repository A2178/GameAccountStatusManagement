$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
dotnet restore "$Root/Workspace.slnx"
dotnet build "$Root/Workspace.slnx" --no-restore
dotnet test "$Root/Workspace.slnx" --no-build --logger "console;verbosity=detailed"
npm install --prefix "$Root/src/Workspace.Client"
npm run typecheck --prefix "$Root/src/Workspace.Client"
npm test --prefix "$Root/src/Workspace.Client"
npm run build --prefix "$Root/src/Workspace.Client"
