# Using Prism RGB 0.2.0

Download and extract the [Windows x64 release](https://github.com/pyonpyonn/prism-rgb/releases/tag/v0.2.0), then open `Launch Prism RGB.cmd`. Keep the app, catalog, documentation, and runtime notices together. The runtime is bundled; no server or other RGB application is required.

## Connect your MSI motherboard

Supported: **B550M PRO-VDH WIFI (MS-7C95), USB 1462:7C95**, with the inspected report descriptor. Other board models are deliberately rejected.

1. Exit SignalRGB and MSI lighting tools, including active background controllers.
2. Choose **Devices → Connect MSI headers**. This reads the current state and exposes **JRGB1, JRGB2, JRAINBOW1, JRAINBOW2**. Connecting does not change colors.
3. Select the headers you use. Stop output before changing the selection.
4. Choose an effect, palette, brightness, and speed in **Studio**.
5. Choose **Start lighting**. Each selected header uses one scene color, including the complete strip/fans on an ARGB header. Per-LED patterns are not implemented.
6. Choose **Stop & restore**, **Disconnect**, or **Quit** when finished.

Each header has **Horizontal** and **Vertical** sliders under Devices. These determine where it samples the scene. The draggable virtual devices in Studio edit the illustration separately; they are not detected physical devices.

The adapter sends volatile report 0x52 only. Stop checks readback of the original four header colors/modes. It cannot restore an earlier per-LED animation stream, firmware settings, or state after hardware removal. Another RGB tool taking ownership blocks writes/restoration; stop that tool and reconnect. Never run two controllers together.

## Effects and preview

| Effect | Color source |
| --- | --- |
| Aurora | Blended primary and secondary colors |
| Rainbow | Built-in moving hue gradient |
| Solid | Primary color |
| Breathing | Pulsing primary color |
| Wave | Traveling primary/secondary blend |
| Ember | Built-in warm orange/red variation |
| Screen ambience | Primary display average color |

Palette fields accept `#RRGGBB`; invalid text does not replace the active color. Brightness affects every effect. Motion speed affects animated effects.

**Pause preview** stops the preview; active hardware animation continues. The preview also sleeps when the window is hidden/minimized or another page is open. Animated effects are capped at 8 updates/second. Solid sleeps until you change a setting and skips duplicate USB reports. Screen ambience samples a small primary-display image at most twice/second, only when needed; it does not capture per-region colors or multiple monitors. Samples stay in memory.

## Profiles and preferences

In **Profiles**, enter a name and choose **Save profile**. Saving an existing name replaces that scene, ignoring case. **Apply** loads a scene; **Export/Import** use JSON, up to 1 MiB. **Delete** removes a profile immediately.

Profiles contain effect, palette, brightness, speed, and virtual layout. Header selections and real scene positions are separate preferences. Settings from 0.1.0 can be read; the obsolete backend preference is ignored. Output always starts disabled on launch.

Preferences live in `%LOCALAPPDATA%\PrismRGB\settings.json`. Use **Settings → Open profile folder**. UI errors may create `error.log` there. Quit before backing up/removing settings to reset them.

Default Close exits and restores header colors/modes. With **Keep running in the system tray** enabled, closing/minimizing hides the window while active lighting continues. Use the tray's **Stop lighting and restore** or **Quit** to finish. There is no automatic startup or updater.

## Discovery and research catalog

**Refresh USB devices** reads HID descriptors without changing lighting. A USB listing does not imply RGB support. **Compatibility** contains 462 descriptive research entries; only the MSI adapter above controls hardware in this release. Original SignalRGB plugin source is not included or executed.

## Troubleshooting

| Problem | What to do |
| --- | --- |
| Cannot connect | Exit competing RGB controllers and verify the exact board model. The app also checks the report descriptor. |
| Connected but no light change | Select the connected header, raise brightness, and press Start lighting. Check physical LED appearance yourself; readback confirms controller state. |
| Output stops | Check the connection/competing apps, then disconnect and reconnect. Prism does not resume automatically. |
| Restore fails | Close other controllers and reconnect, or reopen your normal lighting tool. Disconnection can prevent recovery. |
| Another keyboard/mouse is listed but cannot be controlled | It needs a future native driver; the catalog is research metadata. |
| Virtual dragging has no physical effect | Use the header position sliders under Devices. |
| Preview feels less smooth | The 8 Hz cap reduces resource use. Hardware output shares this limit. |
| Screen ambience is black | Protected content or an unavailable desktop may prevent capture; choose another effect. |
| Close leaves lighting active | Check tray mode and use Quit. |

## Diagnostics and developer commands

Run from the repository root after `./scripts/build.ps1`:

```powershell
./scripts/verify.ps1
./scripts/measure.ps1
```

The executable supports `--data-dir <folder>`, `--render-preview <png-path>`, and `--ui-smoke <folder>`. Use isolated data folders for diagnostic runs since UI tests modify preferences. `--self-test` uses fake transport and read-only HID enumeration, with results beside the executable.

Read-only motherboard inspection:

```powershell
./dist/app/PrismRGB.exe --inspect-msi msi-inspection.json
```

The following explicitly performs a brief dim-color hardware test and restores header colors/modes in a `finally` block. Exit all competing lighting tools first:

```powershell
./dist/app/PrismRGB.exe --test-msi msi-test.json
```

Only use it on the supported board. Do not commit local logs or raw installation data. See [performance](PERFORMANCE.md) and [architecture](ARCHITECTURE.md).
