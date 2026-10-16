$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$distPath=Join-Path $repoRoot 'dist'
$appPath=Join-Path $distPath 'app'
$stagePath=Join-Path $distPath ('package-'+[Guid]::NewGuid().ToString('N'))
$packageRoot=Join-Path $stagePath 'PrismRGB'
if(-not(Test-Path -LiteralPath (Join-Path $appPath 'PrismRGB.exe'))){throw 'Build the app with scripts/build.ps1 first.'}
New-Item -ItemType Directory -Path (Join-Path $packageRoot 'app') -Force | Out-Null
foreach($name in @('PrismRGB.exe','compatibility-catalog.json')){Copy-Item -LiteralPath (Join-Path $appPath $name) -Destination (Join-Path $packageRoot 'app')}
foreach($name in @('README.md','LICENSE','CHANGELOG.md','Launch Prism RGB.cmd','compatibility-catalog.json')){Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination $packageRoot}
foreach($name in @('docs','notices','scripts')){Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination $packageRoot -Recurse}
New-Item -ItemType Directory -Path (Join-Path $packageRoot 'source') | Out-Null
Get-ChildItem -LiteralPath (Join-Path $repoRoot 'source') -File | ForEach-Object{Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $packageRoot 'source')}
$manifest=@(Get-ChildItem -LiteralPath $packageRoot -Recurse -File | Sort-Object FullName | ForEach-Object{
    $relative=[IO.Path]::GetRelativePath($packageRoot,$_.FullName).Replace('\','/')
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(),$relative
})
$manifest | Set-Content -LiteralPath (Join-Path $packageRoot 'SHA256SUMS.txt') -Encoding utf8NoBOM
$archivePath=Join-Path $distPath 'PrismRGB-Windows-x64.zip'
Compress-Archive -LiteralPath $packageRoot -DestinationPath $archivePath -CompressionLevel Optimal -Force
$archiveHash=(Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
'{0}  PrismRGB-Windows-x64.zip' -f $archiveHash | Set-Content -LiteralPath (Join-Path $distPath 'SHA256SUMS.txt') -Encoding utf8NoBOM
Write-Output "Release archive: $archivePath"
Write-Output "SHA256: $archiveHash"
