#Requires -Version 5.1
<#
.SYNOPSIS
Registers Prism's static lighting helper to run when Windows starts.
.DESCRIPTION
Run from an elevated PowerShell window belonging to the same Windows user as
Prism. Supply that user's SID explicitly, including when accepting a UAC prompt.
The task runs as that user with limited rights and S4U logon; it never runs as
SYSTEM, stores no password, and does not start Prism's desktop interface.
The script exports a snapshot of saved settings, then registers the task. It
does not start the task, interrupt the running app, or restart Windows.
.EXAMPLE
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
.\install-boot-lighting.ps1 -PrismExe "$env:USERPROFILE\Downloads\PrismRGB\app\PrismRGB.exe" -UserSid $sid
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory = $true)][string]$PrismExe,
    [Parameter(Mandatory = $true)][string]$UserSid,
    [string]$SnapshotPath,
    [string]$ResultPath
)

$ErrorActionPreference = 'Stop'

function Get-LocalAbsolutePath([string]$Value, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -match '["\r\n\x00]' -or $Value -notmatch '^[A-Za-z]:[\\/]') {
        throw "$Label must be an absolute local drive path without quotation marks or line breaks."
    }
    $full = [IO.Path]::GetFullPath($Value)
    $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($full))
    if ($drive.DriveType -eq [IO.DriveType]::Network) { throw "$Label must be on a local drive; S4U tasks cannot use network resources." }
    return $full
}

function Get-BootDataPath([string]$Value, [string]$Label) {
    $full = Get-LocalAbsolutePath $Value $Label
    if (-not $full.StartsWith($script:bootDirectory + '\', [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetExtension($full) -ne '.json') {
        throw "$Label must be a JSON file inside this user's LocalAppData\PrismRGB\boot directory."
    }
    return $full
}

function Get-PrincipalSid([string]$Value) {
    try { return [Security.Principal.SecurityIdentifier]::new($Value).Value }
    catch { return [Security.Principal.NTAccount]::new($Value).Translate([Security.Principal.SecurityIdentifier]).Value }
}

function Assert-OwnedTask($Task) {
    if ($Task.Description -ne $script:ownerDescription -or
        (Get-PrincipalSid $Task.Principal.UserId) -ne $script:currentSid -or
        [string]$Task.Principal.LogonType -ne 'S4U' -or
        [string]$Task.Principal.RunLevel -ne 'Limited') {
        throw 'A task with this name already exists and is not the expected Prism task for this account. No task was changed.'
    }
    $actions = @($Task.Actions)
    if ($actions.Count -ne 1 -or
        (Get-LocalAbsolutePath $actions[0].Execute 'Existing task executable') -ne $script:exePath -or
        $actions[0].WorkingDirectory -ne $script:exeDirectory -or
        $actions[0].Arguments -notmatch '^--boot-frame "([^"\r\n]+)" "([^"\r\n]+)"$') {
        throw 'The existing task does not match this Prism installation and boot-helper command. No task was changed.'
    }
    $oldSnapshot = Get-BootDataPath $Matches[1] 'Existing snapshot'
    $oldResult = Get-BootDataPath $Matches[2] 'Existing result'
    if ($oldSnapshot -eq $oldResult) { throw 'The existing Prism task has overlapping snapshot and result paths. No task was changed.' }
}

try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $currentSid = $identity.User.Value
    $expectedSid = [Security.Principal.SecurityIdentifier]::new($UserSid).Value
    if ($identity.IsSystem -or $identity.IsAnonymous -or $currentSid -ne $expectedSid -or $currentSid -notmatch '^S-1-(5-21|12-1)-') {
        throw 'Run setup as the same Windows user that uses Prism, and pass that user SID with -UserSid. Do not supply credentials for a different administrator account.'
    }
    $account = $identity.User.Translate([Security.Principal.NTAccount]).Value
    $bootDirectory = Get-LocalAbsolutePath (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PrismRGB\boot') 'Boot directory'
    $exePath = Get-LocalAbsolutePath $PrismExe 'Prism executable'
    if ([IO.Path]::GetFileName($exePath) -ne 'PrismRGB.exe' -or -not [IO.File]::Exists($exePath)) {
        throw 'PrismExe must name the permanent installed PrismRGB.exe file. Extract the app to its permanent location first.'
    }
    $exeDirectory = [IO.Path]::GetDirectoryName($exePath)
    if ([string]::IsNullOrWhiteSpace($SnapshotPath)) { $SnapshotPath = Join-Path $bootDirectory 'boot-frame.json' }
    if ([string]::IsNullOrWhiteSpace($ResultPath)) { $ResultPath = Join-Path $bootDirectory 'boot-result.json' }
    $snapshot = Get-BootDataPath $SnapshotPath 'SnapshotPath'
    $result = Get-BootDataPath $ResultPath 'ResultPath'
    if ($snapshot -eq $result) { throw 'SnapshotPath and ResultPath must be different files.' }
    $taskName = 'PrismRGB-BootLighting-' + $currentSid
    $ownerDescription = 'Prism RGB before-sign-in static lighting; ownerSid=' + $currentSid + '; schema=1'
    $arguments = '--boot-frame "' + $snapshot + '" "' + $result + '"'
    $existing = @(Get-ScheduledTask -TaskPath '\' -ErrorAction Stop | Where-Object { $_.TaskName -eq $taskName })
    if ($existing.Count -gt 1) { throw 'More than one matching boot task was returned. No task was changed.' }
    if ($existing.Count -eq 1) { Assert-OwnedTask $existing[0] }

    $admin = [Security.Principal.WindowsPrincipal]::new($identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $WhatIfPreference -and -not $admin) {
        throw 'Registering a task that runs before sign-in requires administrator rights. Open PowerShell using Run as administrator for this same Windows account, then run this command again with the same -UserSid. Setup has not changed the task or snapshot.'
    }
    if (-not $PSCmdlet.ShouldProcess($taskName, 'Export saved Prism lighting and register a limited-rights Windows startup task')) {
        [pscustomobject]@{ Success = $true; Changed = $false; TaskName = $taskName; UserSid = $currentSid; Account = $account; Executable = $exePath; Snapshot = $snapshot; Result = $result; DelaySeconds = 5; PasswordStored = $false; RunLevel = 'Limited'; WhatIf = [bool]$WhatIfPreference } | ConvertTo-Json
        return
    }

    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($snapshot)) | Out-Null
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($result)) | Out-Null
    # A new staging name proves this exporter produced the snapshot. An older
    # executable that ignores the switch cannot accidentally reuse stale data.
    $staging = Join-Path ([IO.Path]::GetDirectoryName($snapshot)) ('boot-export-' + [Guid]::NewGuid().ToString('N') + '.json')
    try {
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = $exePath
        $start.Arguments = '--export-boot-frame "' + $staging + '"'
        $start.WorkingDirectory = $exeDirectory
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        $exporter = [Diagnostics.Process]::Start($start)
        try {
            if (-not $exporter.WaitForExit(30000)) { throw 'The snapshot exporter did not finish within 30 seconds. The startup task was not changed.' }
            if ($exporter.ExitCode -ne 0) { throw "The snapshot exporter failed (exit $($exporter.ExitCode)). The startup task was not changed." }
        }
        finally { $exporter.Dispose() }
        if (-not [IO.File]::Exists($staging) -or ([IO.FileInfo]::new($staging)).Length -eq 0 -or ([IO.FileInfo]::new($staging)).Length -gt 32768) {
            throw 'The exporter did not create a new valid-sized snapshot. Use Prism RGB 0.6.0 or newer. The startup task and previous snapshot were not changed.'
        }
        $document = [IO.File]::ReadAllText($staging) | ConvertFrom-Json
        if ($null -eq $document -or $document.Version -ne 1 -or $document.Outputs -isnot [Array] -or $document.Outputs.Count -gt 5) {
            throw 'The exported file is not a supported boot lighting snapshot. The startup task and previous snapshot were not changed.'
        }
        if ([IO.File]::Exists($snapshot)) { [IO.File]::Replace($staging, $snapshot, $null) }
        else { [IO.File]::Move($staging, $snapshot) }
    }
    finally { if ([IO.File]::Exists($staging)) { [IO.File]::Delete($staging) } }

    $action = New-ScheduledTaskAction -Execute $exePath -Argument $arguments -WorkingDirectory $exeDirectory
    $trigger = New-ScheduledTaskTrigger -AtStartup
    $trigger.Delay = 'PT5S'
    $principal = New-ScheduledTaskPrincipal -UserId $currentSid -LogonType S4U -RunLevel Limited
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::FromMinutes(2)) -MultipleInstances IgnoreNew
    $settings.WakeToRun = $false
    $settings.RestartCount = 0
    $definition = New-ScheduledTask -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Description $ownerDescription
    $registration = @{ TaskName = $taskName; TaskPath = '\'; InputObject = $definition; ErrorAction = 'Stop' }
    if ($existing.Count -eq 1) { $registration.Force = $true }
    Register-ScheduledTask @registration | Out-Null
    $registered = Get-ScheduledTask -TaskName $taskName -TaskPath '\' -ErrorAction Stop
    Assert-OwnedTask $registered
    $savedSettings = $registered.Settings
    if ($registered.Actions[0].Arguments -ne $arguments -or @($registered.Triggers).Count -ne 1 -or
        $registered.Triggers[0].CimClass.CimClassName -ne 'MSFT_TaskBootTrigger' -or $registered.Triggers[0].Delay -ne 'PT5S' -or
        $savedSettings.DisallowStartIfOnBatteries -or $savedSettings.StopIfGoingOnBatteries -or -not $savedSettings.StartWhenAvailable -or
        $savedSettings.WakeToRun -or $savedSettings.RestartCount -ne 0 -or $savedSettings.ExecutionTimeLimit -ne 'PT2M' -or
        [string]$savedSettings.MultipleInstances -ne 'IgnoreNew') {
        throw 'Windows registered the task, but its saved arguments, trigger, or runtime limits did not match. Inspect the task before relying on boot lighting.'
    }
    [pscustomobject]@{ Success = $true; Changed = $true; TaskName = $taskName; UserSid = $currentSid; Account = $account; Executable = $exePath; Snapshot = $snapshot; Result = $result; DelaySeconds = 5; PasswordStored = $false; RunLevel = 'Limited'; StartedTask = $false; RestartedWindows = $false } | ConvertTo-Json
}
catch {
    Write-Error $_
    exit 1
}
