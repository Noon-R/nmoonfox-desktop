$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    $env:MSBuildEnableWorkloadResolver = 'false'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $env:DOTNET_CLI_HOME = Join-Path (Get-Location) 'artifacts/dotnet-home'
    $project = [xml](Get-Content -LiteralPath src/Nmoonfox.Desktop/Nmoonfox.Desktop.csproj -Raw)
    $appVersion = $project.Project.PropertyGroup.Version
    if ($appVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid app version' }
    # Versioned folders allow packaging an update while the previous version is running.
    $publishDirectory = "artifacts/nmoonfox-desktop-$appVersion-win-x64"
    dotnet publish src/Nmoonfox.Desktop/Nmoonfox.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $publishDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    Copy-Item -LiteralPath README.md -Destination (Join-Path $publishDirectory 'README.md') -Force
    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath artifacts/nmoonfox-desktop-win-x64.zip -Force
    Write-Output (Join-Path (Get-Location) 'artifacts/nmoonfox-desktop-win-x64.zip')
} finally { Pop-Location }
