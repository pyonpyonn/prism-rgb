#Requires -Version 5.1
<#
.SYNOPSIS
Removes only the current user's verified Prism boot-lighting task.
.DESCRIPTION
Run in an elevated PowerShell window for the same Windows account that uses
Prism. The explicit SID prevents setup under a different administrator account.
This script leaves the running app, sign-in startup setting, and saved snapshot
alone. Supply the permanent executable path used when the task was installed.
.EXAMPLE
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
.\remove-boot-lighting.ps1 -PrismExe "$env:USERPROFILE\Downloads\PrismRGB\app\PrismRGB.exe" -UserSid $sid
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory = $true)][string]$PrismExe,
    [Parameter(Mandatory = $true)][string]$UserSid
)

$ErrorActionPreference = 'Stop'

function Get-LocalAbsolutePath([string]$Value, [string]$Label) {
    if ([string]::IsNullOrWhiteSpace($Value) -or $Value -match '["\r\n\x00]' -or $Value -notmatch '^[A-Za-z]:[\\/]') {
        throw "$Label must be an absolute local drive path without quotation marks or line breaks."
    }
    $full = [IO.Path]::GetFullPath($Value)
    if ([IO.DriveInfo]::new([IO.Path]::GetPathRoot($full)).DriveType -eq [IO.DriveType]::Network) { throw "$Label must be a local drive path." }
    return $full
}

function Get-PrincipalSid([string]$Value) {
    try { return [Security.Principal.SecurityIdentifier]::new($Value).Value }
    catch { return [Security.Principal.NTAccount]::new($Value).Translate([Security.Principal.SecurityIdentifier]).Value }
}

try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $currentSid = $identity.User.Value
    $expectedSid = [Security.Principal.SecurityIdentifier]::new($UserSid).Value
    if ($identity.IsSystem -or $identity.IsAnonymous -or $currentSid -ne $expectedSid -or $currentSid -notmatch '^S-1-(5-21|12-1)-') {
        throw 'Run removal as the same Windows user that uses Prism, with that user SID in -UserSid. Do not supply credentials for a different administrator account.'
    }
    $exePath = Get-LocalAbsolutePath $PrismExe 'Prism executable'
    if ([IO.Path]::GetFileName($exePath) -ne 'PrismRGB.exe') { throw 'PrismExe must name the permanent PrismRGB.exe path used when installing this task.' }
    $exeDirectory = [IO.Path]::GetDirectoryName($exePath)
    $bootDirectory = Get-LocalAbsolutePath (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PrismRGB\boot') 'Boot directory'
    $taskName = 'PrismRGB-BootLighting-' + $currentSid
    $ownerDescription = 'Prism RGB before-sign-in static lighting; ownerSid=' + $currentSid + '; schema=1'
    $existing = @(Get-ScheduledTask -TaskPath '\' -ErrorAction Stop | Where-Object { $_.TaskName -eq $taskName })
    if ($existing.Count -eq 0) {
        [pscustomobject]@{ Success = $true; Changed = $false; TaskName = $taskName; Reason = 'Not installed'; SnapshotPreserved = $true } | ConvertTo-Json
        return
    }
    if ($existing.Count -ne 1) { throw 'More than one matching boot task was returned. No task was removed.' }
    $task = $existing[0]
    if ($task.Description -ne $ownerDescription -or
        (Get-PrincipalSid $task.Principal.UserId) -ne $currentSid -or
        [string]$task.Principal.LogonType -ne 'S4U' -or
        [string]$task.Principal.RunLevel -ne 'Limited') {
        throw 'A task with this name exists but does not match this account and Prism task ownership. No task was removed.'
    }
    $actions = @($task.Actions)
    if ($actions.Count -ne 1 -or
        (Get-LocalAbsolutePath $actions[0].Execute 'Existing task executable') -ne $exePath -or
        $actions[0].WorkingDirectory -ne $exeDirectory -or
        $actions[0].Arguments -notmatch '^--boot-frame "([^"\r\n]+)" "([^"\r\n]+)"$') {
        throw 'The task action does not match this Prism installation and boot-helper command. No task was removed.'
    }
    $snapshot = Get-LocalAbsolutePath $Matches[1] 'Existing snapshot'
    $result = Get-LocalAbsolutePath $Matches[2] 'Existing result'
    foreach ($path in @($snapshot, $result)) {
        if (-not $path.StartsWith($bootDirectory + '\', [StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetExtension($path) -ne '.json') {
            throw 'The task references a file outside this user''s Prism boot directory. No task was removed.'
        }
    }
    if ($snapshot -eq $result) { throw 'The existing task has overlapping snapshot and result paths. No task was removed.' }
    $admin = [Security.Principal.WindowsPrincipal]::new($identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $WhatIfPreference -and -not $admin) {
        throw 'Removing the before-sign-in task requires administrator rights. Open PowerShell using Run as administrator for this same account, then rerun this command with the same -UserSid. No task was removed.'
    }
    $changed = $false
    if ($PSCmdlet.ShouldProcess($taskName, 'Remove this user''s verified Prism boot-lighting task')) {
        Unregister-ScheduledTask -TaskName $taskName -TaskPath '\' -Confirm:$false -ErrorAction Stop
        $changed = $true
    }
    [pscustomobject]@{ Success = $true; Changed = $changed; TaskName = $taskName; SnapshotPreserved = $true; SignInStartupPreserved = $true; WhatIf = [bool]$WhatIfPreference } | ConvertTo-Json
}
catch {
    Write-Error $_
    exit 1
}
