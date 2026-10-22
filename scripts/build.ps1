$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $env:MSBuildEnableWorkloadResolver = 'false'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_CLI_HOME = Join-Path (Get-Location) 'artifacts/dotnet-home'
    dotnet build src/Nmoonfox.Desktop/Nmoonfox.Desktop.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    dotnet run --project tests/Nmoonfox.Core.Tests -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
} finally { Pop-Location }
