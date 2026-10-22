$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $env:MSBuildEnableWorkloadResolver = 'false'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_CLI_HOME = Join-Path (Get-Location) 'artifacts/dotnet-home'
    dotnet publish src/Nmoonfox.Desktop/Nmoonfox.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o artifacts/nmoonfox-desktop-win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    Copy-Item -LiteralPath README.md -Destination artifacts/nmoonfox-desktop-win-x64/README.md -Force
    Compress-Archive -Path artifacts/nmoonfox-desktop-win-x64/* -DestinationPath artifacts/nmoonfox-desktop-win-x64.zip -Force
    Write-Output (Join-Path (Get-Location) 'artifacts/nmoonfox-desktop-win-x64.zip')
} finally { Pop-Location }
