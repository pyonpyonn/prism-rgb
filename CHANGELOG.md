# Changelog

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
