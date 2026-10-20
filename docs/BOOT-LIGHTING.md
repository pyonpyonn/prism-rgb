# Windows startup, closing, and boot lighting

Prism 0.6.0 can apply saved static colors **before Windows sign-in**, once Windows Task Scheduler and the lighting drivers are available. Normal sign-in startup then resumes the animated scene. It can also leave its last colors on powered controllers when quitting. **Prism cannot run inside UEFI/BIOS, before Windows, or while Windows is shut down.** Lighting during those periods is controlled by the hardware and its power supply.

## Before Windows sign-in

The optional boot helper reads a saved frame, applies it to the supported motherboard/GPU, releases the controllers without clearing them, and exits. It has no preview, desktop capture or continuing effect loop. It attempts the controllers independently with limited readiness retries. The normal app takes priority after sign-in; a shared per-user controller lock prevents concurrent writes from the two Windows sessions. GPU lighting still depends on its NVIDIA driver being available in the noninteractive session.

Keep the app in its permanent folder and choose/save your scene first. Open PowerShell **as administrator using the same Windows account that uses Prism**, then run from the extracted Prism folder:

```powershell
$sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
./scripts/install-boot-lighting.ps1 -PrismExe "$PWD/app/PrismRGB.exe" -UserSid $sid
```

Setup exports only the fixed color frame and registers `PrismRGB-BootLighting-<user SID>`. It uses an **At startup** trigger delayed five seconds and the same user's limited-rights **S4U** identity. No password is stored. It does not run as SYSTEM, start a background service, restart Windows or change BIOS/firmware. Microsoft requires administrator registration for [boot-trigger tasks](https://learn.microsoft.com/en-us/windows/win32/taskschd/boottrigger); [S4U tasks](https://learn.microsoft.com/en-us/windows/win32/api/taskschd/ne-taskschd-task_logon_type) run on a noninteractive desktop with local resources only. Do not use a different administrator account for setup.

The helper uses `%LOCALAPPDATA%\PrismRGB\boot\boot-frame.json` and writes `boot-result.json` there. Existing boot frames update automatically when Prism saves color/device changes. The boot frame samples an animated scene at a fixed point; it does not animate. Screen ambience uses a saved palette color instead of capturing the sign-in screen. Brightness, header lengths, enabled outputs and manual LED colors are retained.

Keep normal Windows sign-in startup and saved-lighting restore enabled if you want the animation to resume. Re-run setup with the new permanent executable path after moving the app. To remove only the boot task, leaving normal sign-in startup and settings intact:

```powershell
./scripts/remove-boot-lighting.ps1 -PrismExe "$PWD/app/PrismRGB.exe" -UserSid $sid
```

Use `-WhatIf` to review setup/removal without changes. Setup does not test the task or reboot the PC. The actual delay and whether lights appear before the sign-in screen depend on boot speed and driver readiness. A noninteractive test can establish device access, but the next real boot is needed to verify cold-boot timing on this PC.

## Start with Windows

![Windows startup and retained-color settings](images/startup-settings.png)

Interface example from isolated tests; the startup registration in this example is simulated.

Keep the complete app folder in its permanent location, then enable these options in **Settings → Windows startup**:

- **Start Prism when I sign in to Windows** registers this executable for the current user.
- **Start quietly in the system tray** avoids opening the main window.
- **Restore my saved lighting automatically at Windows sign-in** connects the supported controllers and starts the saved scene, palettes, brightness and device settings.

Prism waits two seconds before its first connection attempt, then allows retries after three and five seconds if the controller is unavailable. There are at most three attempts, with no permanent retry loop. Startup waits for the first motherboard frame to succeed before reporting success. Stop or Quit cancels pending startup. Competing RGB software blocks output; disable its automatic startup separately. A failure is reported in the tray and the local `startup.log` beside preferences. A missing GPU does not prevent available motherboard lighting.

Sign-in startup uses the current user's `Run` entry named `PrismRGB`, pointing to the quoted app path with `--startup`. This part needs no administrator account, service, BIOS change or scheduled task; the optional earlier boot helper above is separate. Windows **Settings → Apps → Startup** can also disable the Run entry. Re-enable it from the new copy if you move the portable app. [Microsoft's startup documentation](https://support.microsoft.com/en-us/windows/experience/startup-boot/configure-startup-applications-in-windows) describes sign-in startup and the registry location.

Only one normal instance runs in the current user/session. Launching Prism again opens the existing window; an extra automatic startup launch stays quiet. `PrismRGB.exe --quit` requests a graceful exit from that instance. Diagnostics use their own isolated instances.

## When you close the window or quit

**Keep running in the system tray when I close the window** hides the window while effects continue. Use the tray menu to open it, Stop, or Quit.

**Keep the last colors when Prism quits** freezes the most recently delivered frames instead of clearing them. Controller handles and the GPU worker are released. This retains colors only as long as the controller keeps its state and receives power. It does not continue the animation or save a hardware/BIOS profile. When Windows ends the session with this option enabled, Prism stops its timers and avoids sending a black frame; Windows performs process teardown.

**Stop & restore** always clears streamed colors and restores the original motherboard configuration, even when Keep colors is enabled. With Keep colors disabled, normal Quit does the same cleanup. Previous external pixel animations and GPU colors cannot be reconstructed.

All automatic lighting and retained-color options are opt-in. Startup/closing preferences are separate from exported scene profiles.

## During BIOS boot or while the PC is off

The MSI B550M PRO-VDH WIFI has a physical **LED_SW1** switch and BIOS RGB Light Control support. MSI says the switch must be on for motherboard lighting when BIOS RGB control is enabled. This enables hardware lighting; it does not store Prism's scenes or prove that JRAINBOW receives standby power. See [MSI's model-specific RGB control FAQ](https://www.msi.com/faq/3928) and [the motherboard manual](https://download.msi.com/archive/mnu_exe/mb/E7C95v1.7.pdf), LED_SW1 on page 41.

Whether the ARGB headers or GPU remain lit in shutdown depends on their power rails, controller reset behavior and firmware. A driver or controller reset can replace a retained frame. Removing power clears volatile state. The card cannot display colors if its lighting loses power. MSI's USB charging/ErP instructions do not establish ARGB-header or GPU standby-lighting support.

Prism keeps MSI's persistent-save flag disabled. It does not modify BIOS settings, flash controller firmware, or install a pre-boot driver. **BIOS/cold-boot and powered-off lighting have not been verified on this setup.** A reliable same-scene effect before Windows requires a verified hardware-memory feature; this adapter does not currently provide one.

## Check the behavior on your PC

1. Enable Windows startup, tray mode and automatic restore. Choose a scene and start lighting.
2. Enable Keep colors, then choose **Quit** from the tray. The animation should stop at the last colors while the controllers remain powered. This is different from Stop & restore, which clears them.
3. When you next choose to restart normally, check what the LEDs do during BIOS and after signing in. Prism should resume its scene after sign-in. Record any gap or hardware-default colors during boot.
4. If the boot task is installed, check for the static saved colors after Windows begins starting but before sign-in. Inspect `boot-result.json` for separate motherboard/GPU results; a skipped helper means normal Prism already held the controllers or requested handoff.

No restart, shutdown or BIOS alteration is performed by enabling these app settings. A real power-cycle observation is needed to establish what this hardware retains outside Windows.
