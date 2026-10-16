param([ValidateSet('Debug','Release')][string]$Configuration='Release')
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$outputPath=Join-Path $repoRoot 'dist\app'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
& dotnet publish (Join-Path $repoRoot 'source\PrismRGB.csproj') -c $Configuration -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true --nologo -v minimal -o $outputPath
if($LASTEXITCODE -ne 0){throw 'Prism RGB publish failed.'}
Write-Output "Portable app: $outputPath"
