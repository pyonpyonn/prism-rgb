# Measured resource use

## 0.5.2 GPU recovery

Normal output remains capped at 30 motherboard / 15 GPU updates per second, with changed-packet caching and sleeping Solid scenes. GPU faults use cancellable delays: at most two brief packet retries, then reconnect waits of 1/2/5/10/30 seconds, capped at 30 seconds. An unavailable GPU does not create a busy retry loop or block motherboard frames. Successful static scenes have no periodic recovery worker.

For a ten-minute run against both real supported controllers, quit the normal Prism instance and competing RGB apps, then run:

```powershell
./scripts/measure.ps1 -Scenarios hardware-soak
```

This explicit diagnostic uses isolated preferences, drives Aurora at 15% brightness, records CPU/frame/write counts plus GPU transfer failures, packet retries, recoveries and final status, then clears/restores the controllers. It takes ten minutes plus warmup/cleanup. Check GPU status/counters as well as process success; motherboard output can continue while GPU access is unavailable. A finite successful run cannot establish that driver errors will never recur. The earlier measurements below remain version-specific observations.

## 0.4.0 color studio

Measured on 2026-10-20 on the same PC described below, using four seconds of warmup followed by about 20 seconds per case. These preview cases do not enable hardware output.

| Scenario | Total CPU | One-core equivalent | Mean app GPU | Preview frames/second |
| --- | ---: | ---: | ---: | ---: |
| Original Aurora preview | 0.383% | 12.26% | 0.000% | 7.9 |
| Custom eight-color Rainbow preview | 0.378% | 12.11% | 0.000% | 7.9 |
| Custom Solid preview after warmup | 0.000% | 0.00% | 0.000% | 0.0 |

Raw values are in [color-studio-performance.json](color-studio-performance.json). Palette stops are parsed once after an edit and cached for sampling. Static custom colors still stop the periodic preview timer. The eyedropper uses one temporary display snapshot per monitor, redraws its magnifier on pointer events, and disposes the images/windows/thread when finished. No ongoing eyedropper polling or screenshot storage is added. These observations do not measure the short picking interaction itself, system composition, or energy use.

The native protocols and output cadences are unchanged in 0.4.0. The hardware results below are archived 0.3.0 observations, rather than new 0.4.0 hardware measurements.

## 0.3.0 hardware and preview archive

Measured on 2026-10-19: Ryzen 9 5900XT (32 logical processors), Gigabyte RTX 4070 Ti Gaming OC, MSI B550M PRO-VDH WIFI with two configured 60-pixel ARGB chains, Windows build 26200, and a 1340x875 DIP window. The owner separately confirmed visible motherboard and GPU color tests.

Each scenario warms up for four seconds, then measures about **20 seconds** of process CPU time. Brightness is 15% except the separate Rainbow hardware case at 70%. GPU samples are Windows `GPU Engine` counters for the app process, summed across its engines at roughly two-second intervals. The GPU observation window is approximately the same steady period as the CPU measurement.

| Scenario | Total CPU | One-core equivalent | Mean app GPU | Scene/preview frames per second | USB writes | GPU I2C writes |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Motherboard + GPU, Aurora, hidden | 0.041% | 1.33% | 0.000% | 30.0 | 601 | 274 |
| Motherboard + GPU, Rainbow 70%, hidden | 0.063% | 2.03% | 0.000% | 30.0 | 600 | 819 |
| Motherboard + GPU, Solid, hidden | 0.000% | 0.00% | 0.000% | 0.0 | 0 | 0 |
| Aurora preview visible | 0.085% | 2.73% | 0.000% | 7.7 | 0 | 0 |
| Solid preview visible | 0.000% | 0.00% | 0.000% | 0.0 | 0 | 0 |
| Hidden, output off | 0.000% | 0.00% | 0.000% | 0.0 | 0 | 0 |
| Screen ambience preview | 0.181% | 5.78% | 0.000% | 1.8 | 0 | 0 |

CPU percentages divide process CPU time by wall time and 32 logical processors. The one-core equivalent makes that normalization explicit. Zero CPU means no additional CPU time at the timer's measurement resolution during that observation. Zero GPU means no nonzero app-engine activity was observed, including when a software-rendered process has no GPU-engine instances. Windows composition and screen readback can use GPU resources outside this process. These observations do not measure total system energy use or guarantee identical results on other PCs.

Hardware rows use the real supported MSI and Gigabyte controllers. The animation rate counts scene updates; identical reports can be skipped. GPU writes are individual eight-byte register transactions, not complete GPU frames. Solid's initial output and GPU mode setup occur during warmup, so zero measured writes means no repetition afterward. Cleanup clears streamed colors, waits for GPU work, and verifies the original motherboard configuration. Earlier external pixel scenes/GPU colors cannot be recovered.

Raw values, exact durations, sample counts, and working sets are in [performance-results.json](performance-results.json). The desktop runtime still occupies memory; this work targets low ongoing CPU/GPU processing. Only this setup and these short observations are represented. The 0.2.0 readback-only results did not establish visible ARGB output and are not used as proof of functioning lighting.

## Reproduce

Use Windows, .NET 10 SDK, and PowerShell 7 in the repository root:

```powershell
./scripts/build.ps1
./scripts/measure.ps1
```

Default scenarios do not enable hardware. They open the actual app for visible-preview cases and close it afterward. The script saves results in a unique `dist/performance-*` folder. Missing/unreadable GPU counters are null.

Explicit hardware cases change both supported controllers and clear/restore afterward. Close competing RGB apps first:

```powershell
./scripts/measure.ps1 -Scenarios hardware-animated,hardware-solid,hardware-rainbow
```

## Scheduling and limits

- A scoped high-resolution Windows waitable timer targets 30 motherboard frames/second independently of WPF. Its thread sleeps in the kernel, coalesces queued UI callbacks, and stops on static/idle output. It does not request a global timer period or change system power settings.
- GPU I/O coalesces the latest colors on a separate worker, capped at 15 Hz. Only changed register payloads are sent, and mode setup runs once per output session.
- Preview drawing is capped at 8 Hz and stops while hidden/minimized or on another page.
- Static scenes send changed colors once and leave no periodic frame loop running. Screen ambience samples at most twice/second only while needed.
- Palette/style/grid/label resources are cached. Desktop capture downsamples directly into a 32x18 bitmap rather than allocating a full-desktop bitmap.

Implementation and scope are in [architecture](ARCHITECTURE.md). Relevant platform references: [Windows waitable timer creation](https://learn.microsoft.com/en-us/windows/win32/api/synchapi/nf-synchapi-createwaitabletimerexw), [WPF software rendering](https://learn.microsoft.com/en-us/dotnet/api/system.windows.media.renderoptions.processrendermode), and [NVIDIA driver API](https://github.com/NVIDIA/nvapi).
