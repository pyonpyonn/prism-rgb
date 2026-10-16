param([switch]$SkipUi)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
$appPath=Join-Path $repoRoot 'dist\app\PrismRGB.exe'
if(-not(Test-Path -LiteralPath $appPath)){throw 'Build the app with scripts/build.ps1 first.'}
Push-Location -LiteralPath $repoRoot
try{
    $coreProcess=Start-Process -FilePath $appPath -ArgumentList '--self-test' -WindowStyle Hidden -PassThru -Wait
    if($coreProcess.ExitCode -ne 0){throw "Core checks failed with exit code $($coreProcess.ExitCode)."}
    $core=Get-Content -LiteralPath (Join-Path $repoRoot 'dist\app\test-results.json') -Raw | ConvertFrom-Json
    if($core.Failed -ne 0){throw 'One or more core checks failed.'}
    Write-Output "Core checks passed: $($core.Passed)"
    if(-not $SkipUi){
        $uiPath=Join-Path $repoRoot 'dist\ui-checks'
        $dataPath=Join-Path $repoRoot 'dist\ui-test-data'
        $argumentList=@('--ui-smoke',('"{0}"' -f $uiPath),'--data-dir',('"{0}"' -f $dataPath))
        $uiProcess=Start-Process -FilePath $appPath -ArgumentList $argumentList -WindowStyle Hidden -PassThru -Wait
        if($uiProcess.ExitCode -ne 0){throw "Interface checks failed with exit code $($uiProcess.ExitCode)."}
        $ui=Get-Content -LiteralPath (Join-Path $uiPath 'ui-test-results.json') -Raw | ConvertFrom-Json
        if($ui.Failed -ne 0){throw 'One or more interface checks failed.'}
        Write-Output "Interface checks passed: $($ui.Passed)"
    }
}finally{Pop-Location}
