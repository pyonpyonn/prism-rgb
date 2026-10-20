# Native lighting architecture — 0.5.1

Prism is a C# WPF/Windows Forms app targeting .NET 10, without a network backend, browser engine, vendor app dependency, or third-party NuGet package. The portable build bundles Microsoft's desktop runtime and notices. GPU access loads the installed system `nvapi64.dll`.

| Source | Responsibility |
| --- | --- |
| App.xaml / App.xaml.cs | Styling, startup, software rendering, diagnostic modes |
| MainWindow.cs | Studio/devices/profiles/catalog/settings, tray, lifecycle, scheduling, interface checks |
| Models.cs | Persistence/normalization, scene effects, per-output pixel sampling and overrides |
| ColorEditor.cs | Saturation/value/hue editor, RGB/hex validation, swatches and palette presets |
| ScreenEyedropper.cs | Temporary physical-pixel display snapshots, magnifier overlays, click/cancel and cleanup |
| WindowsStartup.cs | Per-user sign-in registration, bounded startup sequence, single-instance activation/Quit signals |
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

## Custom effect palettes

`StudioState.EffectPalettes` stores a separate 1–8-stop hex palette for each customized effect. Missing entries preserve original effect behavior, so older settings/profiles remain valid. Normalization removes unknown effects/invalid colors and limits stop counts. Palette changes replace lists; a weak cache parses each list into RGB values once and reuses them for preview and hardware sampling. Profiles deep-clone and export the full palette dictionary. No hardware protocol or output cadence changes are needed for recoloring.

The color editor commits only after confirmation. RGB/hex input cannot pass invalid values into the scene. The screen picker runs on one temporary STA thread with a scoped per-monitor DPI context, without changing the WPF thread's DPI settings. It captures display bitmaps before opening full-screen Windows Forms overlays, maps physical coordinates to snapshot pixels, and consumes the selection click. The magnifier repaints on pointer movement only. Selection, Escape/right-click or closing ends the modal session; `finally` disposes overlays/bitmaps and the worker exits. No screenshot file or persistent capture timer is created. The WPF modal editor becomes transparent while picking rather than hiding/ending its dialog loop.

Software checks exercise captured-image selection/cancellation and negative monitor origins. A local diagnostic verifies a real known-color screen pixel; real mixed-DPI/HDR coverage remains limited to available displays.

## Startup and exit lifecycle

The opt-in Windows `Run` entry points to the quoted current executable plus `--startup`, without elevation or a service. Startup can avoid showing the WPF window, restore persisted device placements/scene colors, and retry connection at most three times after 2/3/5-second waits. Stop/Quit or disabling automatic restore cancels pending work; competing RGB apps block it. Failed startup is logged locally and reported through the tray. Manual launches do not independently auto-enable output.

A named kernel-object lease admits one normal instance per user/session. Event signals activate the existing window or request Quit. Thread-pool waits sleep until a signal; no activation polling loop runs. Test/benchmark modes bypass the normal-instance guard and UI tests inject fake startup registration and transports.

Normal Stop always clears/restores. Opt-in Keep colors changes only the quit path: stop the frame clock, wait for an in-flight frame, stop/drain the GPU worker and dispose transports without sending black/config restoration. The most recently delivered colors remain in volatile controller state while powered. On Windows session end with retention enabled, timers stop and configuration is saved without blocking shutdown; Windows reclaims process handles. No flash-save bit, BIOS configuration or firmware command is introduced. Retention is not proof of powered-off/cold-boot behavior; see [boot lighting](BOOT-LIGHTING.md).

## MSI protocol

Recognition requires the exact board name, VID/PID 1462:7C95, usage page 1 / usage 0, maximum feature length 725, an 8-bit report 0x52 with 184 data bytes, and report 0x53 with 724 data bytes. The API buffer is allocated at collection maximum; writes use the selected report's actual length (185 or 725). Connection only reads configuration.

The 0.2.0 adapter changed mode/color fields in report 0x52 and verified them by reading back. The owner reported no visible color change. Working output requires both a correctly routed configuration and report 0x53 pixel data. The visible compact test confirmed that combination; independent-port writes without the correct controller-wide route did not work on the tested setup.

Compact frames begin 53/25/06/00/00, then RGB1, RGB2, JRAINBOW1 pixels, JRAINBOW2 pixels as RGB triples. The combined ARGB count is at most 232 in this mode. Configuration retains unrelated fields while setting both ARGB lengths/options, analog routing, and the controller-wide selector at offsets 74/78/82. The high-capacity route switches both ARGB modes and the selector together, then uses 53/25/04 port packets and a separate analog packet. Per-header bounds are 1–120; high-capacity physical coverage remains unverified.

Controller ownership covers all motherboard outputs. Unchecked outputs are black while streaming. No flash save is requested (configuration byte 184 stays zero), and no firmware, BIOS, or fan command is emitted. Stop clears streamed pixels, sends the saved configuration, waits 250 ms, and verifies header fields. An earlier external per-LED stream cannot be reconstructed; unused byte 62 can be normalized by firmware.

## Gigabyte RTX 4070 Ti

The adapter requires NVAPI PCI ID 278210DE and subsystem 40C61458, then checks the response to the known eight-byte identity query at internal I2C address 0x71 on port 1. Mode/color commands remain disabled until identity verification succeeds. It uses the installed NVIDIA driver with the 64-byte version-3 I2C structure and internal I2CReadEx/I2CWriteEx entries; these are undocumented driver interfaces and may change across driver versions.

Every controller write is exactly eight bytes and checked against a narrow allowlist: static lighting mode commands and the known fan/logo color registers only. This avoids the incorrect-write-length issue noted by the inspected protocol. Three fan colors map to zone 0; the logo maps to zone 3. Unused pixel payloads are zeroed. Unchanged payloads are cached, so routine updates typically touch only changed color registers. GPU fan speed, power limits, sensors, firmware, and clocks are outside the adapter.

GPU state is not readable as a complete prior scene. Stop clears the controlled GPU lights; it does not claim to restore their earlier colors or effects. A missing/unsupported GPU does not block MSI connection. Runtime GPU failures never propagate into the motherboard frame writer or trigger whole-system cleanup.

NVIDIA errors carry their numeric status. Generic (-1) and busy (-108) writes retry the exact same idempotent lighting packet at most twice, after cancellable 40/160 ms delays. Cache entries update only after successful writes; an incomplete frame invalidates full-frame deduplication, and mode initialization is complete only after all eight setup commands succeed. Invalidated handles skip packet retries and go directly to recovery.

The single coalescing GPU worker owns writes, reconnects and delays. Recoverable driver/I/O failures retain the newest pending frame and reopen the transport after 1/2/5/10/30-second waits, capped at 30 seconds thereafter. Reopening discards old handles/cache, re-enumerates the exact PCI identifiers and rechecks controller identity before any mode/color write. No black frame is sent during recovery. Solid scenes retain their failed frame even without new scene ticks. Ownership checks block reconnecting while another RGB tool runs. Invalid identity/data or unsupported requests pause only the GPU, without repeated retries. Status transitions update the footer/local log without balloon popups. Stop/Quit cancels waits and drains the worker before explicit cleanup or retained-color disposal; a new Start uses a fresh cancellation source. Successful static scenes leave no periodic recovery loop running.

The observed -1 status is a [generic NVIDIA error](https://github.com/NVIDIA/nvapi/blob/main/nvapi_lite_common.h), not evidence that another RGB app owns the card. Its underlying driver cause is not established. Recovery also follows NVIDIA's guidance to discard/re-enumerate [invalidated handles](https://docs.nvidia.com/nvapi/concepts.html).

## Tests and extension

Core checks use fake USB/GPU transports, including transient and partial writes, static-scene recovery, latest-frame coalescing, invalid handles, capped backoff, ownership/identity guards, cancellation, packet allowlists, routing, bounds, duplicate suppression and cleanup. Interface checks include hidden startup GPU recovery without stopping/clearing the motherboard, alongside counts, direction, pixel editing, palettes, navigation, idle behavior and closing. They do not issue real lighting writes.

Extend support using independently written protocols, strict identification, constrained I/O, tests, and explicit cleanup. The reference catalog is descriptive metadata rather than executable drivers. Original SignalRGB scripts, account data, installation inventories, and raw private logs are excluded.
