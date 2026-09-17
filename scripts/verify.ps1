$ErrorActionPreference = 'Stop'
$Root = Split-Path -Parent $PSScriptRoot
dotnet restore "$Root/Workspace.slnx"
dotnet build "$Root/Workspace.slnx" --no-restore
dotnet test "$Root/Workspace.slnx" --no-build --logger "console;verbosity=detailed"
dotnet run --project "$Root/tools/Workspace.Contracts" -- "$Root/src/Workspace.Client/src/contracts.generated.ts" --check
npm install --prefix "$Root/src/Workspace.Client"
npm run typecheck --prefix "$Root/src/Workspace.Client"
npm test --prefix "$Root/src/Workspace.Client"
npm run build --prefix "$Root/src/Workspace.Client"
