# Native lighting architecture — 0.3.0

Prism is a C# WPF/Windows Forms app targeting .NET 10, without a network backend, browser engine, vendor app dependency, or third-party NuGet package. The portable build bundles Microsoft's desktop runtime and notices. GPU access loads the installed system `nvapi64.dll`.

| Source | Responsibility |
| --- | --- |
| App.xaml / App.xaml.cs | Styling, startup, software rendering, diagnostic modes |
| MainWindow.cs | Studio/devices/profiles/catalog/settings, tray, lifecycle, scheduling, interface checks |
| Models.cs | Persistence/normalization, scene effects, per-output pixel sampling and overrides |
| LightingSurface.cs | Cached preview drawing, layout dragging, tiny desktop sample |
| HardwareFrameClock.cs | Scoped native frame timing, cancellation, bounded callbacks |
| Hardware.cs | Read-only HID enumeration, exact descriptor checks, MSI stream routing and transport |
| GigabyteGpu.cs | Exact GPU identification, constrained NVAPI I2C access, GPU protocol, combined scheduler |
| Diagnostics.cs | Read-only inspection and bounded visible hardware tests |
| SelfTests.cs | Fake transports, packet boundaries, failure/cleanup/concurrency and sampling checks |

## Scheduling

A scoped high-resolution Windows waitable timer runs the motherboard cadence at 30 Hz independently of WPF timers. Its worker sleeps in the kernel and posts at most one pending UI callback; no global timer period or system power setting is changed. Stop cancels and joins the worker, so static and idle scenes have no frame-clock wakeups. There is one motherboard frame in flight. GPU work is separate, capped at 15 Hz, with one pending latest color array instead of a backlog. A GPU error raises an event even if a static scene has stopped the main timer. Stop drops pending GPU frames, awaits in-flight work, clears colors, and restores board configuration before disposing handles.

Preview redraws are capped at 8 Hz and stop on hidden/minimized/inactive pages. Static output sleeps between settings changes. Complete duplicate packets and unchanged GPU register payloads are suppressed. GPU lighting mode is initialized once per output session. No permanent GPU polling loop runs when there is no pending work. Color parsing and drawing resources are cached. Screen sampling uses StretchBlt directly into 32x18 pixels at most twice/second.

WPF uses SoftwareOnly. Windows composition and screen readback may still consume GPU resources outside the app. [Measurements](PERFORMANCE.md) report observations and their limits.

## MSI protocol

Recognition requires the exact board name, VID/PID 1462:7C95, usage page 1 / usage 0, maximum feature length 725, an 8-bit report 0x52 with 184 data bytes, and report 0x53 with 724 data bytes. The API buffer is allocated at collection maximum; writes use the selected report's actual length (185 or 725). Connection only reads configuration.

The 0.2.0 adapter changed mode/color fields in report 0x52 and verified them by reading back. The owner reported no visible color change. Working output requires both a correctly routed configuration and report 0x53 pixel data. The visible compact test confirmed that combination; independent-port writes without the correct controller-wide route did not work on the tested setup.

Compact frames begin 53/25/06/00/00, then RGB1, RGB2, JRAINBOW1 pixels, JRAINBOW2 pixels as RGB triples. The combined ARGB count is at most 232 in this mode. Configuration retains unrelated fields while setting both ARGB lengths/options, analog routing, and the controller-wide selector at offsets 74/78/82. The high-capacity route switches both ARGB modes and the selector together, then uses 53/25/04 port packets and a separate analog packet. Per-header bounds are 1–120; high-capacity physical coverage remains unverified.

Controller ownership covers all motherboard outputs. Unchecked outputs are black while streaming. No flash save is requested (configuration byte 184 stays zero), and no firmware, BIOS, or fan command is emitted. Stop clears streamed pixels, sends the saved configuration, waits 250 ms, and verifies header fields. An earlier external per-LED stream cannot be reconstructed; unused byte 62 can be normalized by firmware.

## Gigabyte RTX 4070 Ti

The adapter requires NVAPI PCI ID 278210DE and subsystem 40C61458, then checks the response to the known eight-byte identity query at internal I2C address 0x71 on port 1. Mode/color commands remain disabled until identity verification succeeds. It uses the installed NVIDIA driver with the 64-byte version-3 I2C structure and internal I2CReadEx/I2CWriteEx entries; these are undocumented driver interfaces and may change across driver versions.

Every controller write is exactly eight bytes and checked against a narrow allowlist: static lighting mode commands and the known fan/logo color registers only. This avoids the incorrect-write-length issue noted by the inspected protocol. Three fan colors map to zone 0; the logo maps to zone 3. Unused pixel payloads are zeroed. Unchanged payloads are cached, so routine updates typically touch only changed color registers. GPU fan speed, power limits, sensors, firmware, and clocks are outside the adapter.

GPU state is not readable as a complete prior scene. Stop clears the controlled GPU lights; it does not claim to restore their earlier colors or effects. A missing/unsupported GPU does not block MSI connection. Runtime GPU I/O errors stop output and require reconnection.

## Tests and extension

Core checks use fake USB/GPU transports, including queued-frame shutdown, static-scene error notification, packet allowlists, routing, bounds, ordering, duplicate suppression, and cleanup. Interface checks use fake connected devices to validate counts, direction, pixel editor population, GPU names, navigation/profiles, idle behavior, and closing. They do not issue real lighting writes.

Extend support using independently written protocols, strict identification, constrained I/O, tests, and explicit cleanup. The reference catalog is descriptive metadata rather than executable drivers. Original SignalRGB scripts, account data, installation inventories, and raw private logs are excluded.
