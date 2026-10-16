# Using Prism RGB

Prism RGB v0.1.0 is a portable Windows x64 application. It works in preview mode without RGB hardware, OpenRGB, or SignalRGB installed. The downloadable release includes the .NET runtime; building from source requires the .NET 10 SDK.

## First launch

1. Download **PrismRGB-Windows-x64.zip** from the [v0.1.0 release](https://github.com/pyonpyonn/prism-rgb/releases/tag/v0.1.0).
2. Extract the entire archive to a folder where you can write files. Do not run the executable from inside the ZIP.
3. Open `app/PrismRGB.exe` or `Launch Prism RGB.cmd`.
4. Begin in **Studio**. The status should read **PREVIEW MODE**.

Keep the release's executable, catalog, documentation, and runtime notices together. Hardware output starts disabled on every launch and is never resumed automatically after a failure.

## Create a scene

In **Studio**, select an effect and adjust **Brightness** and **Motion speed**. Palette inputs accept six-digit hex colors, such as `#68E0C2`; invalid text does not replace the active color.

| Effect | Appearance | Palette use |
| --- | --- | --- |
| Aurora | Moving, blended color fields | Both colors |
| Rainbow | Hue gradient moving across the scene | Built-in rainbow |
| Solid | Constant color | Primary color |
| Breathing | Repeating brightness pulse | Primary color |
| Wave | Traveling blend | Both colors |
| Ember | Animated orange and red variation | Built-in warm colors |
| Screen ambience | Average color from the primary screen | Captured color |

Brightness applies to all effects. Motion speed affects animated effects; it has no visible effect on Solid or the screen-capture sampling rate.

Choose **Edit layout** to drag the virtual monitor, keyboard, mouse, PC, and strip. These shapes illustrate the scene and do not indicate that the corresponding devices are connected. **Settings → Reset virtual layout** restores their default positions.

**Pause preview** freezes animation time. If real lighting is active, it continues sending the paused scene; screen capture can still update. Use **Stop & restore** to stop hardware output.

Screen ambience captures an average of the primary display about twice per second while selected, including in preview mode. It does not map screen regions to individual LEDs. Capture samples are held in memory and are not written or uploaded. Screen-capture behavior on real desktops remains unverified.

## Save and share profiles

1. Configure a scene in **Studio**.
2. Open **Profiles**, enter a name, and choose **Save profile**.
3. Choose **Apply** beside a saved profile to load it into Studio.
4. Use **Export** to save a profile as JSON, or **Import profile** to load one.

Saving a name that already exists replaces that profile's scene, ignoring letter case. **Delete** removes a saved profile immediately.

A profile contains the effect, palette, brightness, speed, and virtual layout. Actual hardware output selection and position sliders are saved separately in preferences and are not included in exported scene profiles. Imported profiles must be valid Prism JSON and no larger than 1 MiB.

## Connect through OpenRGB

This route uses [OpenRGB](https://openrgb.org/) for hardware discovery and control. Hardware support depends on your installed OpenRGB version and devices; Prism does not execute SignalRGB plugins.

1. Install OpenRGB from its official website and verify your intended devices appear there.
2. Close SignalRGB and other competing RGB programs, including their active background controllers. Keep OpenRGB running.
3. Start OpenRGB's SDK server using **127.0.0.1**, port **6742**. These values are currently fixed in Prism's interface.
4. Open **Devices → Connect OpenRGB** in Prism. Connection reads the current device list and state. It does not start lighting output.
5. Enable the desired output checkboxes and set their positions as described below.
6. Choose **Start lighting**.
7. To finish, choose **Stop & restore**, then **Disconnect** if desired.

Prism sends effect colors at up to 10 frames per second. It attempts to restore previous colors and mode on Stop, Disconnect, or Quit. Restoration cannot be guaranteed after hardware removal, loss of the server, or another application taking ownership.

If OpenRGB's device list changes, Prism stops output to avoid sending colors to stale device indexes. Disconnect and reconnect to refresh it. Only one Prism backend can be connected at a time; disconnect before switching to the MSI adapter.

## Position real outputs

Real devices sample colors from a normalized scene. Use the controls below each connected output on the **Devices** page:

| Control | Meaning |
| --- | --- |
| Output checkbox | Includes the output in hardware frames; stop lighting before changing the selection |
| Horizontal | Starting position from left (0%) to right (100%) |
| Vertical | Position from top (0%) to bottom (100%) |
| LED span | Horizontal distance covered by the output's LED sequence |

For a keyboard or strip, increasing **LED span** spreads its LED colors across a wider part of the effect. The current mapping treats the LEDs as a horizontal sequence, not their physical two-dimensional positions. Samples beyond the right edge use the rightmost scene color. A single-color MSI header uses only Horizontal and Vertical; its LED span does not affect output.

These sliders are independent of the draggable virtual layout in Studio.

## Experimental MSI adapter

**Live lighting writes have not been verified.** Use the direct adapter only with the exact supported board and a clear understanding that incorrect controller packets can damage an RGB controller. Source-level packet checks cannot establish physical hardware safety.

The adapter is restricted to **MSI B550M PRO-VDH WIFI (MS-7C95), USB 1462:7C95**. It exposes two 12V RGB and two 5V ARGB headers, with one color for each complete header. It does not set LED counts, implement individual addressable LEDs, or support other motherboard models.

1. Confirm the exact board model; the app also checks it before connecting.
2. Close SignalRGB and other MSI/RGB lighting tools.
3. Open **Devices → Connect MSI headers**. Prism must successfully read an existing 185-byte lighting report before connecting.
4. Select the intended headers and set their scene positions.
5. Choose **Start lighting** only when ready to perform experimental hardware output.
6. Choose **Stop & restore** when finished.

The adapter changes selected header mode/color fields in a copy of the original report and clears the flash-save flag. It does not issue firmware commands or per-LED probes. Stop and Quit attempt to restore the report read during connection. See [inspection notes](INSPECTION.md) and [architecture](ARCHITECTURE.md) for the implementation limits.

## USB discovery and compatibility catalog

**Devices → Refresh USB devices** enumerates HID interfaces without sending lighting frames. Detection alone does not establish RGB support; many keyboards and mice expose interfaces unrelated to lighting.

**Compatibility** searches descriptive metadata by device name, publisher, or USB ID. Its entries are research references, not executable drivers or a promise of Prism compatibility. The bundled catalog does not contain the original SignalRGB plugin source.

## Preferences and tray behavior

Prism stores `settings.json` under `%LOCALAPPDATA%\PrismRGB`. This contains local profiles, the current scene, real-device placement preferences, and tray settings. Use **Settings → Open profile folder** to open it. Unhandled UI errors may create `error.log` there.

The default close action exits and attempts to restore lighting. If **Keep running in the system tray when I close the window** is enabled, closing or minimizing the window hides it and active lighting continues. Use the tray menu's **Stop lighting and restore** or **Quit** when finished.

To reset all preferences, quit Prism, back up `settings.json`, and remove that file. The next launch creates default settings. Deleting the extracted app folder does not remove preferences.

## Troubleshooting

| Problem | Check |
| --- | --- |
| OpenRGB connection fails | Confirm the SDK server is running at `127.0.0.1:6742`, and close competing RGB controllers. |
| A device appears in USB discovery but cannot be controlled | Discovery is read-only; try OpenRGB if it supports that device. A catalog reference does not supply a Prism driver. |
| A controller connects but lights do not change | Select at least one output with LEDs, set nonzero brightness, then choose Start lighting. Physical output is still unverified in this release. |
| Lighting stops after plugging in hardware | Disconnect and reconnect in Prism to refresh OpenRGB's device list. |
| Previous lighting is not restored | Reconnect or reopen your normal controller software. Hardware disconnection or another controller can prevent restoration. |
| Moving virtual devices does not change physical lighting | Adjust the real output's position sliders under Devices. |
| Compatibility list is empty | Confirm `compatibility-catalog.json` is beside `PrismRGB.exe`. |
| Palette edits appear ignored | Enter a complete color in `#RRGGBB` format and choose an effect that uses that palette. |
| Screen ambience is black or unavailable | Try another effect. Primary-display capture needs real-desktop testing and may fail for protected content or unusual display configurations. |
| Closing the window leaves lights active | Check tray settings; use the tray menu's Stop or Quit action. |

For a bug report, include the version, backend, device model, steps to reproduce, and any visible error. Review screenshots and logs for personal details before posting them.

## Developer commands

Run from the repository root on Windows with the .NET 10 SDK:

```powershell
dotnet build source/PrismRGB.csproj -c Release
dotnet run --project source/PrismRGB.csproj -c Release
dotnet run --project source/PrismRGB.csproj -c Release -- --self-test
```

The core self-test uses a local simulated OpenRGB server and writes `test-results.json` beside the built executable. It does not send physical lighting commands. It does enumerate HID interfaces read-only.

The executable also supports `--data-dir <folder>` to use a separate preferences folder, `--render-preview <png-path>` to render a preview and exit, and `--ui-smoke <folder>` to exercise the UI and save screenshots/results. Use an isolated data folder for screenshot or UI checks because those modes initialize and modify preferences.

```powershell
dotnet run --project source/PrismRGB.csproj -c Release -- --data-dir .test-data --ui-smoke .test-output
```

See the root [README](../README.md#build-from-source) for publishing a portable build, and [architecture](ARCHITECTURE.md) for extending the application.
