# Prism RGB

[![Windows build and checks](https://github.com/pyonpyonn/prism-rgb/actions/workflows/windows.yml/badge.svg)](https://github.com/pyonpyonn/prism-rgb/actions/workflows/windows.yml)

A standalone Windows RGB app with direct MSI motherboard control, seven effects, and local profiles. **No OpenRGB, SignalRGB, SDK server, account, or background service is required.**

Version **0.2.0 alpha**, Windows x64. Native hardware support currently targets **MSI B550M PRO-VDH WIFI (MS-7C95), USB 1462:7C95**. Each of its two RGB and two ARGB headers receives one color; individual addressable LEDs are a future feature.

![Prism RGB Studio](docs/images/preview.png)

## Download and use

1. Download **PrismRGB-Windows-x64.zip** from [v0.2.0 Releases](https://github.com/pyonpyonn/prism-rgb/releases/tag/v0.2.0).
2. Extract the complete ZIP and open **Launch Prism RGB.cmd** or **app/PrismRGB.exe**. The .NET desktop runtime is included.
3. Close competing lighting tools, including SignalRGB and MSI lighting services.
4. Open **Devices → Connect MSI headers**. Connecting reads the current state; lighting output stays off.
5. Select headers, choose an effect and brightness, then **Start lighting**.
6. **Stop & restore** returns the four headers to their previous colors and modes. Quit restores them too; optional tray mode keeps active lighting running.

Preview mode works on other PCs. Other devices shown in USB discovery or the reference catalog do not yet have native Prism drivers. [Full usage and troubleshooting](docs/USAGE.md).

## Low CPU and GPU use

- Animated preview and hardware effects are capped at **8 updates per second** with one USB frame in flight.
- **Solid** sends a color when it changes, then sleeps. Identical USB reports are suppressed.
- Hidden/minimized windows and other pages stop preview animation. Hardware effects continue independently.
- The preview uses software rendering, cached drawing resources, and cached color parsing. Prism does not request GPU-accelerated WPF rendering.
- Screen ambience downsamples directly to **32 × 18** pixels, at most twice per second, only when the preview or hardware needs it.
- There is no browser engine, network client, polling service, or automatic startup.

See [measured CPU/GPU results and reproducible commands](docs/PERFORMANCE.md). Resource use depends on your PC, window size, effect, and screen capture. Windows desktop composition may still use the GPU.

## Features and limits

Effects: Aurora, Rainbow, Solid, Breathing, Wave, Ember, Screen ambience. Includes brightness, speed, two-color palettes, draggable scene preview, JSON profile import/export, USB discovery, tray controls, and a searchable research catalog.

The MSI adapter verifies board name, USB IDs, collection usage, maximum feature buffer length, and the 185-byte report descriptor before opening output. It uses only volatile report **0x52**, preserves unrelated outgoing bytes, and never requests flash-save, firmware operations, or per-LED probes.

Live tests on the target motherboard confirmed test-color readback and restoration of all four header colors/modes. The controller changes an unused report field internally, so full byte-for-byte restoration is not claimed. Physical LED appearance still needs user observation; these are controller readback checks. Restoration can fail after disconnection or another RGB app taking over.

Not implemented: other motherboard/device adapters, individual ARGB LEDs, fan control, monitoring, audio/game integrations, LCD control, general SignalRGB plugin execution, or automatic startup. This is independent software and does not provide full SignalRGB feature parity. No vendor executable, original plugin source, paid content, or artwork is included.

## Build, verify, package

Use Windows, the **.NET 10 SDK**, and PowerShell 7:

```powershell
dotnet run --project source/PrismRGB.csproj -c Release
./scripts/build.ps1
./scripts/verify.ps1
./scripts/package.ps1
```

The portable ZIP and SHA-256 checksum are written to `dist/`. Windows CI runs the same build and **15 core checks + 11 interface checks**, including the normal close-window path. Automated tests use a fake native transport and perform no lighting writes. The app has no third-party NuGet dependencies.

To measure resources on your own PC:

```powershell
./scripts/measure.ps1
# Optional: these two scenarios actually drive the supported motherboard, then restore it.
./scripts/measure.ps1 -Scenarios hardware-animated,hardware-solid
```

## Documentation

- [Usage and troubleshooting](docs/USAGE.md)
- [Performance measurements](docs/PERFORMANCE.md)
- [Architecture and native adapter design](docs/ARCHITECTURE.md)
- [Inspection findings and evidence limits](docs/INSPECTION.md)
- [Changelog](CHANGELOG.md)

Bug reports should include version, board/device model, effect, reproduction steps, and observed result. Review logs for personal information before posting. Contribute independently written native adapters with protocol tests and public references.

## License and acknowledgements

Original Prism source is [MIT licensed](LICENSE). Bundled Microsoft runtime licenses and notices are in `notices/`. Product names belong to their owners.

Research references: [REA](https://github.com/morluto/rea), [SignalRGB developer documentation](https://docs.signalrgb.com/developer/plugins/). Neither is needed at runtime.
