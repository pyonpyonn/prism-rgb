# Prism RGB

[![Windows build and checks](https://github.com/pyonpyonn/prism-rgb/actions/workflows/windows.yml/badge.svg)](https://github.com/pyonpyonn/prism-rgb/actions/workflows/windows.yml)

An independent Windows desktop RGB studio with animated lighting, local profiles, and optional hardware output through OpenRGB.

Prism RGB is an early **v0.1.0** release for **Windows x64**. You can explore every effect in preview mode without connecting hardware. Hardware output is disabled on each launch, and live LED output has not yet been verified on physical devices.

![Prism RGB Studio](docs/images/preview.png)

## Download and run

1. Open [Releases](https://github.com/pyonpyonn/prism-rgb/releases/tag/v0.1.0).
2. Download **PrismRGB-Windows-x64.zip** and extract the complete archive to a writable folder.
3. Open `app/PrismRGB.exe` (or use `Launch Prism RGB.cmd`). Keep the extracted files together, including `app/compatibility-catalog.json` and the runtime notices.
4. Choose an effect in **Studio**. Connecting hardware is optional.

The portable release includes the .NET desktop runtime; a separate .NET installation is unnecessary. The release is portable; no installer or auto-updater is included.

See the [usage guide](docs/USAGE.md) for profiles, scene positioning, hardware setup, and troubleshooting.

## Features

- Seven effects: **Aurora, Rainbow, Solid, Breathing, Wave, Ember, and Screen ambience**.
- Brightness, motion speed, hex-color palettes, and an animated setup preview.
- Drag-and-drop virtual layout editing.
- Named local profiles with JSON import and export.
- Read-only USB HID discovery and a searchable device-reference catalog.
- Optional OpenRGB SDK output for devices supported by your OpenRGB installation.
- A restricted, experimental direct adapter for MSI B550M PRO-VDH WIFI headers.
- Optional system-tray behavior and **Stop & restore** controls.

The virtual monitor, keyboard, mouse, PC, and strip are scene illustrations. Dragging them edits the preview; actual hardware uses the separate **Horizontal**, **Vertical**, and **LED span** controls under **Devices**. A catalog match identifies a research candidate and does not install a working driver.

## Control real lighting

### Through OpenRGB

1. Install [OpenRGB](https://openrgb.org/) separately and confirm it detects your devices.
2. Close SignalRGB and other competing lighting controllers. Keep OpenRGB running as the backend.
3. Enable OpenRGB's SDK server on **127.0.0.1:6742**.
4. In Prism, select **Devices → Connect OpenRGB**.
5. Select outputs, position their LEDs with the scene sliders, and choose **Start lighting**.
6. Choose **Stop & restore** to stop streaming and attempt to restore the previous colors and mode.

OpenRGB supplies hardware support. Prism supplies the effects and sends colors through the local SDK connection. Prism currently connects to one backend at a time. Unplugging a device, changing OpenRGB's device list, or losing the server connection stops output; reconnect before trying again. Restoration is best-effort and may be unavailable after a disconnect.

### Experimental MSI headers

The direct adapter is restricted to **MSI B550M PRO-VDH WIFI (MS-7C95), USB 1462:7C95**. It provides one color for each of two 12V RGB and two 5V ARGB headers. Individual addressable LEDs and strip-length configuration are not implemented.

**Live lighting writes are unverified.** This is a hardware experiment, not general MSI motherboard support. The implementation reads the existing 185-byte report before enabling output, preserves unrelated report bytes, and avoids flash-save and firmware commands. These checks do not establish hardware safety. Read the [usage guide](docs/USAGE.md#experimental-msi-adapter) and [inspection notes](docs/INSPECTION.md) before using it.

## Build from source

Use Windows with the **.NET 10 SDK** installed. From a terminal in the repository root:

```powershell
dotnet build source/PrismRGB.csproj -c Release
dotnet run --project source/PrismRGB.csproj -c Release
```

Run the automated core checks:

```powershell
dotnet run --project source/PrismRGB.csproj -c Release -- --self-test
```

Publish a portable Windows x64 executable:

```powershell
dotnet publish source/PrismRGB.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o dist/app
```

Keep the published `compatibility-catalog.json` with the executable. `dist/app` is build output and should not be committed. The C# application uses WPF/Windows Forms and has no third-party NuGet dependencies.

To build, check, and assemble the complete release archive with documentation and runtime notices:

```powershell
./scripts/build.ps1
./scripts/verify.ps1
./scripts/package.ps1
```

The archive and its SHA-256 checksum are written to `dist/`. Use PowerShell 7 to run these scripts. GitHub Actions runs the same workflow on Windows for pushes and pull requests.

The release build passed 13 core checks and 9 UI smoke checks. Automated Windows builds run the same checks. Tests use a simulated OpenRGB server and do not send hardware lighting commands. They establish software behavior rather than compatibility with physical RGB devices.

## Status and limits

This is an independent prototype informed by local protocol inspection. It is not affiliated with SignalRGB and does not provide full SignalRGB feature parity. No SignalRGB executable, plugin source, paid effect, account data, or artwork is included.

Not implemented: general SignalRGB plugin execution, fan control, hardware monitoring, audio-reactive effects, game integrations, macros, LCD control, account services, and automatic startup. Compiled SignalRGB application logic was not recovered during the initial inspection.

Screen ambience samples the average color of the primary display about twice per second. It does not provide per-region or multi-monitor capture. Capture results stay in memory; physical output and real-screen behavior still need user testing.

## Documentation and contributing

- [Usage and troubleshooting](docs/USAGE.md)
- [Architecture and extension points](docs/ARCHITECTURE.md)
- [Inspection findings and evidence limits](docs/INSPECTION.md)
- [Changelog](CHANGELOG.md)
- [License](LICENSE)

Bug reports should include the Prism version, Windows version, selected backend, device model, expected result, and observed result. Remove personal information from logs before sharing. For hardware support, include public protocol references and tests; do not submit proprietary plugin source or private installation dumps.

## License and acknowledgements

Original Prism RGB source is released under the [MIT license](LICENSE). Third-party runtimes retain their own licenses and notices. SignalRGB, OpenRGB, MSI, and other product names belong to their respective owners.

Research tools and references: [REA](https://github.com/morluto/rea), [SignalRGB plugin documentation](https://docs.signalrgb.com/developer/plugins/), and the [OpenRGB SDK protocol](https://github.com/CalcProgrammer1/OpenRGB/blob/master/Documentation/OpenRGBSDK.md).
