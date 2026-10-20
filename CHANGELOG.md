# Changelog

## 0.5.1 — Preserve existing GPU placement

- Preserve saved GPU color spans below 5% when reconnecting or restoring at sign-in. Default placement now applies only to newly discovered GPU outputs.
- Extended hidden-startup checks to use fake motherboard and GPU transports with a saved narrow GPU span.

## 0.5.0 — Quiet Windows startup and retained exit colors

- Added opt-in per-user Windows sign-in startup, hidden tray launch and saved-lighting restore with three bounded connection attempts.
- Added single-instance activation and graceful `--quit`, cancellation on Stop/Quit, and deferred exit while a controller connection finishes.
- Added an optional last-frame retention path that releases controller handles without clearing colors. Stop & restore keeps its clear/restore behavior.
- Added Windows-session-end preparation, settings controls and a boot-lighting guide distinguishing software startup from BIOS/powered-off hardware behavior.
- Kept volatile-only controller writes; no BIOS edits or persistent firmware-save commands were added. Actual cold-boot/powered-off retention remains unverified.
- Passed 40 core and 25 interface checks with fake transports/registration, including hidden startup, conflicts, cancellation, retained release and shutdown preparation.

## 0.4.0 — Effect color studio and eyedropper

- Added independent 1–8-color palettes for every effect, including recolored Rainbow, Ember and Screen ambience.
- Added a saturation/value area, hue slider, RGB/hex editing, quick swatches, eight palette presets, add/remove/reverse and restore-original controls.
- Added a screen eyedropper with frozen images, magnification, physical-pixel selection and Escape/right-click cancellation; temporary images/windows/thread are released afterward.
- Saved all custom effect palettes in local settings and exported profiles, with normalization and older-profile compatibility.
- Retained the native hardware paths, output rates and static/hidden timer sleep. Parsed custom palette stops are cached.
- Passed 34 core and 19 interface checks, including color-dialog apply/cancel and returning from the eyedropper without losing its modal session.

## 0.3.0 — Visible ARGB output, individual pixels, Gigabyte GPU

- Fixed missing visible MSI colors by configuring complete stream routing and supplying compact per-LED data. The owner confirmed the working ARGB test visually.
- Added 1–120 configured LEDs per JRAINBOW header, direction/span controls, and per-LED color overrides.
- Added exact Gigabyte RTX 4070 Ti Gaming OC fan/logo lighting through its installed NVIDIA driver; the owner confirmed two GPU tests visually.
- Increased motherboard effects toward 30 Hz, with a separate coalescing 15 Hz GPU path and 8 Hz preview. Cached unchanged GPU register payloads.
- Added cleanup of streamed colors, GPU error notifications for idle scenes, and hardware diagnostic commands. Previous external pixel scenes are not recoverable.
- Passed 29 core and 15 interface checks; added bounded, typed protocol fixtures and GPU shutdown/error coverage.


## 0.2.0 — Standalone MSI / low power

- Removed the OpenRGB backend and all network/server dependencies.
- Direct MSI control validates the exact board, collection, and individual report descriptor.
- Eight updates/second maximum, duplicate USB suppression, static/hidden timer sleep, cached drawing/color resources, and tiny conditional screen capture.
- Software-rendered preview avoids requesting GPU-accelerated WPF rendering.
- Serialized native I/O, failure handling, verified header restoration, and explicit hardware diagnostics.
- Fixed recursive window-close error and guarded tray reopening during shutdown.
- Added reproducible CPU/GPU benchmarks and updated usage documentation.
- Passed 15 core and 11 interface checks; live target-controller readback and header restoration verified. Physical LED appearance still requires user observation.

## 0.1.0 â€” Initial alpha

- Native Windows desktop studio with seven independently implemented effects.
- Animated preview, editable virtual layout, palettes, brightness, and motion speed.
- Local scene profiles with JSON import/export.
- Read-only USB HID discovery and a 462-entry reference catalog.
- Local OpenRGB SDK integration with connection and device-list failure handling.
- Experimental whole-header lighting adapter restricted to MSI B550M PRO-VDH WIFI (MS-7C95).
- Optional tray behavior and controller state restoration.
- Usage, architecture, inspection notes, reproducible build/package scripts, and Windows CI.

The release passed 13 core checks and 9 interface checks. Hardware lighting output and real-desktop screen capture remain unverified. Native SignalRGB application reconstruction and full feature parity are incomplete.
