# RigForge

RigForge is a Windows 11 and Linux daemon for RGB lighting, fan and pump curves, and AIO LCD screens. It runs in user mode and replaces SignalRGB, NZXT CAM, and FanControl for the devices it can drive directly.

The daemon listens on `http://127.0.0.1:5757`. On Windows the dashboard is an Electron window with a tray icon. On Linux the same UI opens in the browser from a desktop entry.

## What it controls

- One spatial canvas. An effect is drawn once and each LED samples that canvas.
- Temperature-to-duty curves with hysteresis, ramp limits, and a failsafe that goes to 100% when a temperature reading goes stale.
- AIO screen widgets for NZXT Kraken displays.
- Direct frames for the device families in [ARCHITECTURE.md](ARCHITECTURE.md). A device that is detected but has no writer stays visible as unmanaged and is not sent a frame.
- Named color palettes and custom WASM effects next to the profile.

## Download

Nothing has to be compiled. Open the [v0.1.0 release](https://github.com/AndrewShchetkin/rigforge/releases/tag/v0.1.0) and download the file for your system.

| System | Download |
| --- | --- |
| Windows 11 | [RigForge-Setup-0.1.0-win-x64.exe](https://github.com/AndrewShchetkin/rigforge/releases/download/v0.1.0/RigForge-Setup-0.1.0-win-x64.exe) |
| Windows, portable zip | [rigforge-win-x64-0.1.0.zip](https://github.com/AndrewShchetkin/rigforge/releases/download/v0.1.0/rigforge-win-x64-0.1.0.zip) |
| Linux | [rigforge-linux-x64-0.1.0.tar.gz](https://github.com/AndrewShchetkin/rigforge/releases/download/v0.1.0/rigforge-linux-x64-0.1.0.tar.gz) |

On Windows, run the setup file and accept the UAC prompt. A separate .NET install is not required. Installer steps, autostart, update, and uninstall are in [docs/INSTALL.md](docs/INSTALL.md).

## Requirements

- Windows 11 x64, or 64-bit Linux (Debian, Ubuntu, Fedora, Arch, openSUSE).
- Optional on Windows: the signed [PawnIO](https://pawnio.eu) driver for CPU temperature, motherboard fan control, and SMBus DRAM lighting. USB RGB, Kraken screens, and NVIDIA telemetry do not need it.
- Close SignalRGB, NZXT CAM, and Armoury Crate Lighting Service before the first start. They hold the same USB devices.

## Build

People changing the program need the .NET SDK 10 and Node.js 22. From the repository root on Windows:

```powershell
.\deploy\Build-Release.ps1
```

On Linux:

```bash
SKIP_WIN=1 ./deploy/build-release.sh
```

Outputs are written to `artifacts/`, with `SHA256SUMS.txt`.

## Develop

```powershell
dotnet run --project src/Host.Daemon
```

```powershell
cd ui
npm install
npm run dev
```

The UI dev server is `http://localhost:5173`. It proxies `/api` and `/hubs` to the daemon.

```powershell
dotnet test --project tests/RigForge.UnitTests
cd ui; npm test
cd shell; npm test
```

`Hardware:DryRun` discovers devices and reads sensors without writing RGB, fans, or the LCD. `dotnet run --project src/Tools.HardwareScanner -- hid` is the same kind of read-only scan.

## Repository map

| Path | Role |
| --- | --- |
| `src/Core`, `src/Application` | Domain models and the effect, cooling, and LCD engines |
| `src/Infrastructure.CrossPlatform` | HID, device protocols, NVML, libusb |
| `src/Infrastructure.Windows`, `src/Infrastructure.Linux` | OS integrations |
| `src/Host.Daemon` | Kestrel, SignalR, REST, background workers |
| `src/Tools.HardwareScanner` | `rigforge-scan` |
| `ui/` | React dashboard |
| `shell/` | Electron window and the NSIS installer |
| `deploy/` | Release and install scripts |

Protocol notes, memory measurements, and the safety rules for fans and SMBus are in [ARCHITECTURE.md](ARCHITECTURE.md).
