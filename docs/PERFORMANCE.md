# CPU / GPU measurements — 0.2.0

Measured on 2026-10-19 using Windows build 26200, Ryzen 9 5900XT (32 logical processors), RTX 4070 Ti, a 1340x875 DIP window, and 15% effect brightness. This is one PC and short observations, not a universal resource guarantee.

Each scenario has a four-second warmup and approximately **20 seconds** of process CPU measurement. GPU samples are Windows `GPU Engine` utilization counters filtered to the app's process, summed across its engines. They cover roughly the same steady period, at about two-second intervals (the counter read also waits for a sample). CPU is process CPU time divided by elapsed wall time and logical processor count; the one-core column makes the normalization explicit.

| Scenario | Total CPU | One-core equivalent | Mean app GPU engine sum | USB writes / measured period | Working set MiB |
| --- | ---: | ---: | ---: | ---: | ---: |
| Animated preview visible | 0.185% | 5.93% | 0.000% | 0 | 253 |
| Solid preview visible | 0.000% | 0.00% | 0.000% | 0 | 239 |
| Hidden, output off | 0.000% | 0.00% | 0.000% | 0 | 233 |
| Screen ambience preview | 0.173% | 5.54% | 0.000% | 0 | 270 |
| MSI animation, window hidden | 0.049% | 1.56% | 0.000% | 141 | 238 |
| MSI Solid, window hidden | 0.000% | 0.00% | 0.000% | 0 | 239 |

Zero CPU means no additional CPU time at the process timer's resolution during that observation. Zero GPU means no nonzero app-engine activity was observed, including when a software-rendered process has no GPU-engine instances. Windows desktop composition and screen readback may still use GPU resources outside this process. These numbers do not prove zero system-wide GPU cost. The preview uses WPF SoftwareOnly; it does not request GPU-accelerated WPF rendering.

The hardware scenarios used the real supported MSI controller and all four header outputs, then restored their original colors/modes. Solid's initial update occurs during warmup: zero measured writes means no repeated traffic afterward. Animated output attempts eight frames/second but suppresses identical packets. Preview frames stop while hidden. Screen ambience made roughly 34 captures in 20 seconds in this run because of timer/worker scheduling; two/second is a ceiling.

The older 0.1.0 animated preview measured **0.561% CPU and 0.554% mean app GPU** in a separate approximately 21-second observation on the same PC. Its default renderer and roughly 30 Hz preview differed from the new software-rendered 8 Hz preview. These are illustrative single-run observations; other desktop activity and timing differed. They are not a controlled benchmark or a promise of a fixed improvement.

Raw values, counts, durations, and the baseline are in [performance-results.json](performance-results.json). Memory is reported for transparency; this WPF app is optimized for low ongoing CPU/GPU work, and its desktop runtime still occupies memory.

## Reproduce

From the repository root with Windows, .NET 10 SDK, and PowerShell 7:

```powershell
./scripts/build.ps1
./scripts/measure.ps1
```

Default scenarios do not enable hardware. The script opens the actual app window for visible-preview scenarios and closes it afterward. Results go to a unique `dist/performance-*` folder. Missing/unreadable GPU counters are reported as null rather than a measured zero.

For actual supported MSI output, exit competing RGB controllers first and explicitly run:

```powershell
./scripts/measure.ps1 -Scenarios hardware-animated,hardware-solid
```

These hardware scenarios change all four headers for about 24 seconds each, then restore them. The app keeps output disabled on ordinary launch. Close mode, screen protection, display setup, window size, and effect can alter measurements. A separate bounded `--test-msi <result.json>` diagnostic checks dim test colors and restored header readback; physical LED appearance requires user observation.

## How ongoing work is reduced

- A shared effect timer runs at most eight times/second while needed; idle/static scenes stop it entirely.
- Hidden/minimized/inactive preview pages stop animation. Background hardware effects continue independently.
- Repeated identical reports produce no USB write. There is one frame in flight and no backlog.
- Process-conflict scans are throttled to two seconds during animation and checked again on connect/start/restore.
- Palette parsing, static brushes, background grid, and labels are cached.
- Primary-screen color sampling downsamples directly to 32x18 pixels instead of allocating a full-resolution desktop bitmap.

Rendering reference: [Microsoft RenderOptions.ProcessRenderMode](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.renderoptions.processrendermode). Feature buffers follow [Microsoft HidD_GetFeature](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_getfeature) and [HidD_SetFeature](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_setfeature), with the individual report size checked using HID value capabilities.
