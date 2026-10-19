# Prism RGB source guide

Prism is a standalone C# WPF/Windows Forms app targeting .NET 10. It has no network backend, service, vendor app dependency, or third-party NuGet package. Portable releases bundle the Microsoft desktop runtime and notices.

| Source | Responsibility |
| --- | --- |
| App.xaml / App.xaml.cs | Styling, startup, software rendering, diagnostic entry points |
| MainWindow.cs | Pages, tray, output lifecycle, activity-based scheduling, benchmark hook |
| Models.cs | Scene/profile validation, persistence, independently written effects |
| LightingSurface.cs | Cached drawing resources, scene dragging, tiny desktop downsample |
| Hardware.cs | HID enumeration, descriptor validation, native transport and MSI protocol |
| Diagnostics.cs | Read-only inspection and explicit brief hardware/readback test |
| SelfTests.cs | Protocol/lifecycle tests using fake native transport |

## Scheduling and rendering

Effects map normalized `(x,y,time)` to color; global brightness applies last. Parsed palette colors are cached per thread. A 125 ms dispatcher timer exists only while animated preview/output, needed screen capture, or dirty output requires it. Hidden/minimized or inactive-page preview is not rendered. Solid output sends changes once and leaves the effect timer stopped. Settings changes wake it; duplicate reports are suppressed by the backend.

WPF is set to `SoftwareOnly`. The drawing caches frozen background/grid/style brushes and bounded label resources. The maximum animation rate is 8 Hz, favoring low resource use over high frame-rate smoothness. Screen capture uses GDI StretchBlt directly into a 32x18 bitmap, at most twice/second; no full-desktop bitmap is allocated. Windows composition/capture may still involve GPU work.

One output frame is in flight, with no unbounded queue. USB operations are serialized by a semaphore on worker tasks. Process conflicts are checked on connect/start/restore and at most every two seconds during animated output. Static output has no conflict-polling loop; its next write checks again. This is a best-effort process-name check, not an exclusive hardware lock.

## Native MSI transport

The target must match board B550M PRO-VDH WIFI (MS-7C95), VID/PID 1462:7C95, usage page 1 / usage 0, maximum feature length 725, and one 8-bit value array for report 0x52 with 184 data bytes. `HidP_GetValueCaps` validates the individual report size. Windows feature calls use the collection's 725-byte buffer, with report ID 0x52 selecting the 185-byte lighting report and zero padding for the API buffer.

Connection reads the original report without writing. Frames clone that snapshot, change selected header mode/color fields at offsets 1, 174, 31, and 42, and clear byte 184 (no flash save). No 0x53 report, firmware command, per-LED probe, or header configuration command is emitted.

Stop/Quit sends the saved report with its no-save flag cleared, allows 250 ms for controller processing, then verifies all four original mode/color fields. Full report identity is not guaranteed: live tests observed firmware changing unused byte 62. Earlier per-LED streams cannot be reconstructed from the header snapshot. API/readback success does not establish physical wiring or visible LED appearance.

Failed reads dispose the handle. Failed writes fault the backend and retain restoration responsibility even if the API reports failure; reconnect is required for further output. Disconnect restores then disposes. Unexpected unplugging or another controller taking ownership can prevent restoration.

The close event cancels first and queues cleanup on the dispatcher, allowing WPF to finish its initial Closing event before final Close. UI verification exercises this actual close path to prevent recursive-close errors.

## Extending support

Add independently written native adapters behind `ILightingBackend` with strict identification, protocol fixtures, bounded writes, restoration, and disposal. `IMsiTransport` isolates physical I/O from tests. Do not broaden the MSI whitelist merely because another board shares a PID. The descriptive reference catalog is not a driver.

General SignalRGB plugin execution, per-LED support, audio/game/LCD services, and other board families remain separate work. REA and SignalRGB are research tools/references; they are not runtime dependencies. [Inspection evidence](INSPECTION.md).
