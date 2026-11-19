# Install, run, and remove RigForge

This guide covers release installs of **RigForge** on Windows 11 and Linux, autostart, updates, and removal.

---

## 1. Windows 11 (`.exe` installer)

Windows is installed and removed with executables. `RigForge-Setup-<version>-win-x64.exe` registers the program, the sign-in task, and an entry in Installed apps. Uninstall runs `Uninstall RigForge.exe` from the install folder. PowerShell scripts are not part of the release: execution policy blocks them until they are signed.

### Step 1. Download the release

Download **`RigForge-Setup-<version>-win-x64.exe`** from the GitHub Releases page, or build it from source (section 3).

A separate .NET runtime install is not required. The runtime is packed into the installer.

> **Motherboard and CPU sensors:** to read CPU temperature, control motherboard fans (Super I/O), and drive DRAM lighting over SMBus, install the signed **PawnIO** driver ([pawnio.eu](https://pawnio.eu)). USB RGB controllers, Kraken screens, and GPUs via NVAPI work without extra drivers. Before the first start, close conflicting programs (`SignalRGB`, `NZXT CAM`, `Armoury Crate LightingService`).

### Step 2. Install

Run `RigForge-Setup-<version>-win-x64.exe` and accept the UAC prompt:

1. **Install folder:** default `C:\Program Files\RigForge`.
2. **Startup and shortcuts:**
   - **Start RigForge automatically when I sign in to Windows (Recommended)** starts the background daemon in the notification area at sign-in, without another UAC prompt, and applies cooling curves, AIO widgets, and lighting immediately.
   - **Create a Desktop shortcut** creates a **RigForge** shortcut on the desktop. A Start menu shortcut is always created.
3. **Finish:** leave **Launch RigForge** checked to open the dashboard.

### Step 3. Daily use and autostart

- **Closing the window (`×`)** closes only the window and frees its memory. The daemon stays in the system tray and keeps controlling cooling, the AIO screen, and lighting.
- **Desktop or Start menu shortcut:**
  - if the daemon is already in the tray, the window opens immediately;
  - if autostart was off, or the daemon was stopped with tray **Exit**, the window shows a startup screen, starts the daemon without an extra UAC prompt, and then opens the dashboard.
- **Change autostart at any time:**
  1. Right-click the **RigForge** tray icon → **Start with Windows**.
  2. Or in the app: **Devices** → **System & Startup** → **Start RigForge automatically when you sign in**.

### Step 4. Update and uninstall

- **Update:** run a newer `RigForge-Setup-<version>-win-x64.exe` over the existing install. The wizard stops the daemon (fans return to firmware control for the update), replaces files, and keeps profiles and the autostart setting.
- **Uninstall:** **Windows Settings → Apps → Installed apps → RigForge → Uninstall**.
  - By default, profiles, fan curves, canvas layouts, and custom effects in `%LOCALAPPDATA%\RigForge` are **kept**.
  - To remove those as well, check **Also delete my saved profiles, fan curves, layouts, and custom effects (`%LOCALAPPDATA%\RigForge`)**.
  - Before deleting files, the uninstaller stops the daemon so fan and pump speeds and the AIO screen return to firmware or BIOS control, then removes the scheduled task, shortcuts, and program files.

### Installer command-line switches (silent / automation)

```powershell
# Silent install with autostart and a desktop shortcut
.\RigForge-Setup-0.1.0-win-x64.exe /S

# Silent install without autostart or a desktop shortcut, into a custom folder
.\RigForge-Setup-0.1.0-win-x64.exe /S /AUTOSTART=0 /NODESKTOP=1 /D=C:\Apps\RigForge

# Silent uninstall, keep profiles in %LOCALAPPDATA%\RigForge
"C:\Program Files\RigForge\Uninstall RigForge.exe" /S

# Silent uninstall and delete profiles
"C:\Program Files\RigForge\Uninstall RigForge.exe" /S /PURGE=1
```

---

## 2. Linux (Debian/Ubuntu, Fedora, Arch/CachyOS, openSUSE)

### Install from the release archive

```bash
tar -xzf rigforge-linux-x64-0.1.0.tar.gz
cd rigforge-linux-x64
./deploy/linux/install-linux.sh
```

The script installs binaries to `~/.local/share/rigforge`, the `60-rigforge.rules` access rules, and the `i2c-dev` modules. It registers the user service `rigforge.service` and adds a **RigForge** desktop entry (`~/.local/share/applications/rigforge.desktop`).

- Install **without** autostart at graphical login:

  ```bash
  ./deploy/linux/install-linux.sh --no-autostart
  ```

  Clicking **RigForge** in the application menu runs `rigforge-open`, which starts `rigforge.service` and opens `http://127.0.0.1:5757/#/dashboard`.

### Uninstall on Linux

```bash
# Remove the service, udev rules, shortcut, and program files (profiles in ~/.config/rigforge are kept)
./deploy/linux/uninstall-linux.sh

# Also delete profiles in ~/.config/rigforge
./deploy/linux/uninstall-linux.sh --purge
```

---

## 3. Build a release from source

```powershell
# Windows: UI, self-contained win-x64 and linux-x64, NSIS Setup.exe, portable .zip, .tar.gz, and SHA256SUMS.txt
.\deploy\Build-Release.ps1
# then install the same exe that ships in the release
.\artifacts\RigForge-Setup-<version>-win-x64.exe
```

```bash
# Linux: UI and the self-contained rigforge-linux-x64-<version>.tar.gz archive, plus SHA256SUMS.txt
SKIP_WIN=1 ./deploy/build-release.sh
```
