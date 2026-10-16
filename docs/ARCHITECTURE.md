# Prism RGB source guide

The application is independent of SignalRGB at runtime. The core has no third-party NuGet packages. The portable build bundles Microsoft's .NET Windows Desktop runtime and its notices.

| Source | Responsibility |
|---|---|
| `App.xaml` / `App.xaml.cs` | Shared styling, startup, diagnostics modes, and error reporting |
| `MainWindow.cs` | Studio, Devices, Profiles, Compatibility, Settings, tray actions, output lifecycle |
| `Models.cs` | Scene/profile models, normalization, atomic local persistence, effect sampling |
| `LightingSurface.cs` | Native preview drawing, dragging, primary-screen average-color capture |
| `Hardware.cs` | Read-only HID enumeration, backend contract, restricted MSI 185-byte packet adapter |
| `OpenRgbBackend.cs` | Independent TCP SDK client, version negotiation, bounded replies, restoration and topology invalidation |
| `SelfTests.cs` | Core verification and an independent local OpenRGB protocol fixture |

## Rendering and output

Every effect maps normalized `(x, y, time)` to RGB. Brightness is applied after effect sampling. The preview is rendered around 30 frames per second. Hardware output is limited to 10 frames per second, with a single frame in flight and no unbounded frame queue. OpenRGB LED positions are sampled from each device's horizontal/vertical/span settings; MSI receives one sample per header.

The preview setup is illustrative. Real output positions are edited under Devices and stored independently in preferences. The virtual layout editor changes the illustration's arrangement.

Hardware output starts disabled on every launch. Connection reads the controller state. Start enables output; Stop, Disconnect, or Quit attempts restoration. Process-conflict checks prevent Prism from overwriting an already-running SignalRGB/MSI controller. Failure, unplugging, or topology changes stop streaming. Output is never automatically resumed after a failure.

## Extending device support

Implement `ILightingBackend` with discovery, frame writing, restoration, and disposal. Keep reads and writes explicit. Add meaningful protocol fixtures before attempting hardware output. Device recognition from `compatibility-catalog.json` is a research aid, not an authorization to send a generic packet to an unfamiliar device.

For MSI, the current adapter intentionally implements only report `0x52` for the exact inspected board. Per-LED mode requires separately understanding report `0x53`, LED limits, header configuration, controller version, and verified cleanup. Do not extend the whitelist based on matching product IDs alone; the same ID can appear across related boards with different layouts.

## Research provenance

The public [inspection notes](INSPECTION.md) summarize protocol observations and their limits. Local installation inventories, evidence dumps, account files, and machine-specific logs are excluded. The application does not depend on REA, Ghidra, or SignalRGB at runtime.
