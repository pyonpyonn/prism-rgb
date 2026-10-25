# Changelog

## 0.9.1 — More continuous boot palettes and clearer device timing

- Expand the built-in library from 7 to 21 presets with additional colour palettes, Slow/Fast fade variations, and steady colours. These use the existing tested modes; no new firmware animation modes are introduced.
- Explain that the motherboard fades while the GPU runs its own colour cycle, with independent boot timing. Steady provides an identical fixed colour across devices.
- Reject near-black fade endpoints at the request and native MSI packet layers, in addition to the preset picker. Check every preset at four brightness levels, every interpolated colour step, and all GPU palette-bank slots for accidental black.
- Preserve the current animated boot selection, hardware memory and Windows scene during the update.


## 0.9.0 — Boot colour and effect preset library

- Add Settings boot presets: Lava, Ocean, Cyberpunk, Forest, Sunset and Ice fades, plus Lava steady. Choose Continuous fade or Steady, edit two colours with pickers/hex fields/screen eyedroppers, and choose brightness and motion speed.
- Save up to 32 named custom boot presets locally. Preset selection, editing and library saves do not write controller memory. Preview changes live lighting without a persistent save; Save boot lighting writes once per controller.
- Exclude the native wave modes that showed black gaps. Continuous boot palettes reject black and colours that become too dark at the selected brightness. The red/orange MSI fade and GPU custom-colour preview were accepted visually on the tested PC; other palettes use the same native modes.
- Preserve the current Windows scene and saved profiles when saving boot lighting. The Windows boot helper preserves the stored native fade or steady setting until normal Prism output resumes.
- Add Color fade to Studio. It blends palette colours without a breathing brightness envelope and samples the same scene time at every output position.
- Keep identity/readback guards and explicit single-attempt persistent commits. Independent hardware clocks can drift before Windows; exact boot phase synchronization is not claimed. Cold-boot retention of the new fade remains to be checked physically.

## 0.8.0 — Native boot colour motion and Wave profile handoff

- Add temporary preview and an explicit hardware save for MSI native Color Wave and Gigabyte Tricolor, with separate Slow/Balanced/Fast boot speed controls.
- Read colours from a saved Wave profile: two motherboard colour fields and up to three non-black GPU colours. Initialize all GPU palette slots and retain a valid off/zero-brightness scene.
- Keep source profiles unchanged when selecting a boot scene. Successful saves select a cloned full profile for normal Windows startup; preview does not change the scene or save controller memory.
- Preserve a saved hardware animation in the Windows boot helper instead of replacing it with a static frame. No ongoing boot animation work runs in Prism.
- Keep persistent commits explicit, serialized and single-attempt, with identity/readback guards and independent device results. Exclude the MSI Lava preset that did not light the tested board.
- Native firmware controls its own motion, timing and dark gaps. The exact software Wave runs in Windows; successful delivery does not establish cold-boot retention without a physical test.

## 0.7.0 — Save a static boot colour to hardware

- Add Settings colour selection and an explicit Save to hardware action for the exact supported MSI motherboard and Gigabyte RTX 4070 Ti Gaming OC.
- Prepare native static configurations and issue one persistent commit per controller. Normal animation, startup, shutdown, preference saves and GPU recovery remain volatile; uncertain persistent commits are never retried automatically.
- Pause/drain output and hold the shared controller lease during saves, report device outcomes independently, then leave the static colour active without a restore/black frame.
- Verify MSI static readback before requesting flash storage; use GPU native static routing rather than saving a streaming frame.
- Add fake protocol, failure, ownership and interface coverage. A successful save call acknowledges delivery; BIOS/power-cycle retention and lighting without standby power still require physical observation.

## 0.6.1 — Start boot lighting without an added delay

- Remove the boot task's five-second trigger delay and use normal process priority instead of background priority. Re-running setup updates the existing owned task.
- Record helper process start and separate motherboard/GPU application times to identify scheduler or driver delays.
- Fix atomic replacement of an existing boot snapshot during setup upgrades in PowerShell.
- Extend boot checks to verify successful delivery timestamps. Normal output rates, static idle behavior, ownership guards and limited-rights execution remain unchanged.
- The helper still requires Windows Task Scheduler and device drivers; fast/automatic sign-in may finish before they are ready. Initial BIOS lighting remains hardware-controlled.

## 0.6.0 — Static lighting before Windows sign-in

- Added a headless boot helper that applies saved static per-device colors and exits without clearing them. Normal sign-in startup resumes animations.
- Export only validated fixed color arrays/counts, preserve brightness/manual overrides/disabled outputs, and use a saved color instead of desktop capture for Screen ambience.
- Added limited independent driver-readiness retries, a cooperative startup deadline and controller ownership/handoff across Windows sessions.
- Added optional same-user limited-rights S4U boot-task setup/removal scripts, with no stored password, SYSTEM process, service or firmware change. Administrator registration is required.
- Refresh an existing boot frame when saved preferences change. Initial UEFI/BIOS, powered-off lighting and actual cold-boot timing remain hardware-dependent and need observation.
- Extended fake-controller/core and interface checks for snapshots, awaited GPU delivery, retention, retries, invalid data and handoff.

## 0.5.2 — Keep lights running through GPU I/O errors

- Retry brief NVIDIA generic/busy lighting failures without permanently faulting the GPU backend.
- Reconnect/reidentify the GPU automatically with increasing waits up to 30 seconds, retaining only the latest colors and supporting static scenes.
- Keep motherboard output active through GPU failures; removed the GPU error path that stopped and cleared all lights.
- Replace repeated GPU tray warnings with footer/log status transitions. Unsupported requests and identity mismatches pause only the GPU.
- Fix partial mode initialization and frame-cache handling; cancel packet/reconnect waits on Stop/Quit and preserve retained-color exit behavior.
- Add a ten-minute hardware-soak diagnostic with GPU failure/retry/recovery counters. Keep normal output rates and idle behavior unchanged.
- Passed 50 core and 26 interface checks using fake hardware, including recovery, backoff, invalid handles, ownership/identity checks and lifecycle cancellation.

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
