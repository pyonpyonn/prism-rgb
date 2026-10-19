param(
    [ValidateSet('animated','solid','hidden','screen','hardware-animated','hardware-solid','hardware-rainbow')][string[]]$Scenarios=@('animated','solid','hidden','screen'),
    [string]$AppPath
)
$ErrorActionPreference='Stop'
$repoRoot=Split-Path -Parent $PSScriptRoot
if(-not $AppPath){$AppPath=Join-Path $repoRoot 'dist/app/PrismRGB.exe'}
$AppPath=(Resolve-Path -LiteralPath $AppPath).Path
$resultsPath=Join-Path $repoRoot ('dist/performance-'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $resultsPath | Out-Null
foreach($scenario in $Scenarios){
    $resultPath=Join-Path $resultsPath ($scenario+'.json')
    $dataPath=Join-Path $resultsPath ($scenario+'-preferences')
    $arguments=@('--benchmark',('"{0}"' -f $resultPath),$scenario,'--data-dir',('"{0}"' -f $dataPath))
    $appProcess=Start-Process -FilePath $AppPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $gpuSamples=[Collections.Generic.List[double]]::new()
    $gpuAvailable=$true
    # The application warms up for four seconds before its CPU measurement.
    Start-Sleep -Seconds 5
    while(-not $appProcess.HasExited){
        try{
            $samples=(Get-Counter '\GPU Engine(*)\Utilization Percentage' -ErrorAction SilentlyContinue).CounterSamples
            if(-not $samples){throw 'GPU counters unavailable.'}
            $matching=@($samples | Where-Object {$_.InstanceName -like "pid_$($appProcess.Id)_*" -and $_.Status -eq 0})
            $sum=($matching | Measure-Object CookedValue -Sum).Sum
            $gpuSamples.Add([double]$sum)
        }catch{$gpuAvailable=$false}
        Start-Sleep -Seconds 1
        $appProcess.Refresh()
    }
    if($appProcess.ExitCode -ne 0){throw "Benchmark failed: $scenario. See $resultPath.error.txt"}
    $result=Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    $gpuMean=if($gpuAvailable -and $gpuSamples.Count){($gpuSamples | Measure-Object -Average).Average}else{$null}
    $gpuMax=if($gpuAvailable -and $gpuSamples.Count){($gpuSamples | Measure-Object -Maximum).Maximum}else{$null}
    $result | Add-Member -NotePropertyName GpuEngineSumMeanPercent -NotePropertyValue $gpuMean
    $result | Add-Member -NotePropertyName GpuEngineSumMaxPercent -NotePropertyValue $gpuMax
    $result | Add-Member -NotePropertyName GpuSampleCount -NotePropertyValue $gpuSamples.Count
    $result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resultPath -Encoding utf8NoBOM
    $result | Select-Object Scenario,CpuPercent,OneCorePercent,GpuEngineSumMeanPercent,WorkingSetMiB,PreviewFrames,OutputFrames,UsbWrites,I2cWrites,ScreenCaptures
}
Write-Output "Measurement files: $resultsPath"
