# Service Fix

A small Windows app that stops unneeded services and sets their startup types, for better performance, lower resource use and a smaller attack surface.

Service Fix started out as a PowerShell script (`Service Fix.ps1`). It is now a standalone **EXE with a GUI**, written in C# (WinForms). It behaves the same way as the script: it uses the same matching rules and the same two groups. The service lists now live in an editable INI file instead of in the code.

## Features
- **Live service list** of gaming, bloatware and telemetry services, with status, startup type and target
- **Color-coded rows:**
  - **Green ✔** = already stopped and set to the target startup type
  - **Red ✖** = will be changed when you apply
- **One-click Apply:** stops the services (dependent services first, like `Stop-Service -Force`) and sets their startup types
- **Two groups:**
  - Gaming/bloatware services → **Manual**
  - Telemetry/diagnostic services → **Disabled**
- **Editable INI:** add or remove services without recompiling
- **Service details:** click any row to see the service's Windows description, with a **Web Search** button to look it up
- **Light and dark mode** that follows your Windows setting, including the title bar, and switches live
- **High-DPI aware**

### Controls
| Key / Button | Action |
|---|---|
| **Enter** / **Space** / **Apply** | Stop services and set their startup types |
| **Refresh** | Reload the INI and re-read service states |
| **Edit INI** | Open `Service Fix.ini` in Notepad |
| **Esc** / **Exit** | Close the app |

## Requirements
- Windows 10 or 11
- .NET Framework 4.8 (included with Windows 10 1903+ and Windows 11)
- **Administrator rights**, which are needed to change services. When the app is built with the manifest, it asks for them at startup.

## Files
| File | Purpose |
|---|---|
| `Service Fix.exe` | The app |
| `Service Fix.ini` | Service lists (keep it next to the EXE, or on your Desktop) |

## Configuring Services (`Service Fix.ini`)
The app looks for `Service Fix.ini` next to the EXE first, then on your Desktop. Click **Edit INI**, make your changes, save, then click **Refresh**.

```ini
; Lines starting with ; or # are comments.
; Wildcards * ? [a-c] are supported (case-insensitive), same as Get-Service.

[ManualDisplayNames]    ; match by Display Name -> stopped + MANUAL
*razer*

[ManualNames]           ; match by service Name -> stopped + MANUAL
SamsungMagicianSVC

[DisabledDisplayNames]  ; match by Display Name -> stopped + DISABLED
Remote Desktop*

[DisabledNames]         ; match by service Name -> stopped + DISABLED
DiagTrack
```

- One entry per line. Quotes are optional, so entries pasted from the old script work.
- Matching uses PowerShell's own wildcard engine, so results are the same as `Get-Service`.
- As with `Get-Service -Name`, an exact entry in a `Names` section also matches a service's Display Name.

## Default Service Lists

### Manual Group (Stopped + Set to Manual)
Mostly gaming and bloatware services.

**By Display Name:**
- `*vmware*`, `*gigabyte*`, `*asus*`, `*MSI*`, `*aorus*`, `*aomei*`, `*razer*`
- `NVIDIA FrameView SDK service`
- `*oculus*`, `*origin*`, `*easytune*`, `*gbtec*`
- `*EABackgroundService*`, `*EAAnti*`, `*rockstar*`, `*Epic *`
- `CMigrationService`, `ccleaner*`, `*BattlEye*`, `*galaxy*`
- `*malwarebytes*`, `MSIV`, `OCButtonService`, `*Cheat*`
- `Windows Health and Optimized Experiences`
- `Inventory and Compatibility Appraisal service`

**By Name:**
- `SamsungMagicianSVC`, `Haptic Service`, `LGHUBUpdaterService`, `OpenRGB`
- `IpOverUsbSvc`, `MapsBroker`, `TrkWks`

### Disabled Group (Stopped + Set to Disabled)
Mostly telemetry, diagnostic and remote-access services.

**By Display Name:**
- `Remote Desktop*`

**By Name:**
- `DiagTrack`, `DPS`, `LanmanServer`, `SysMain`, `WerSvc`
- `WSearch`, `AUEPLauncher`, `wuqisvc`, `WinRM`
- `CDPSvc`, `ADPSvc`, `PeerDistSvc`, `CscService`
- `pla`, `wercplsupport`, `RetailDemo`, `wcncsvc`, `WSLService`

## Security Benefits
Several services in the Disabled group accept connections from other machines, or quietly share data. Disabling them closes those doors:

| Service | What it exposes | Why disable it |
|---|---|---|
| `LanmanServer` (Server) | SMB file/printer sharing on TCP 445 | SMB is a common way for attacks to spread between PCs. The WannaCry worm (EternalBlue) used it. If you don't share files, you don't need it listening. |
| `Remote Desktop*` | Remote Desktop (RDP) on TCP 3389 | A frequent target for password-guessing attacks and wormable bugs like BlueKeep (CVE-2019-0708). |
| `WinRM` | Remote PowerShell management | Lets this PC be managed over the network. Workstations rarely need it. |
| `PeerDistSvc` (BranchCache) | Peer-to-peer content sharing | Shares cached content with other PCs on the network. |
| `wcncsvc` (Windows Connect Now) | Wi-Fi Protected Setup (WPS) configuration | Not needed on a single-user PC. |
| `CscService` (Offline Files) | Local cache of network files | Not needed without network shares. |
| `CDPSvc` (Connected Devices Platform) | Discovery of nearby devices | Lowers device-discovery activity on the network. |
| `DiagTrack`, `WerSvc`, `wercplsupport`, `pla` | Telemetry, error reports and performance logging | Keeps usage and crash data from being collected and sent to Microsoft. |

## App Security
- **Offline:** the app makes no network connections and collects no data. The only exception is **Web Search**, which opens a Google search in your default browser.
- **The browser doesn't inherit admin rights:** Web Search launches the browser through Explorer, so the browser runs as a normal user even though Service Fix runs as administrator.
- **Open source:** all logic is in `Service Fix.cs`. Read it, then build it yourself with the command above.
- **No hidden changes:** the app only stops services and changes their startup type. It doesn't delete services, edit other registry settings or install anything.

## Important Warning
The default lists disable several core Windows services, including **WinRM**, **SysMain** (Superfetch), **LanmanServer** (Server), **WSearch** (Windows Search), **DiagTrack** and others. They're turned off to shrink the PC's network attack surface, limit telemetry and tune Windows as a single-user gaming machine (see **Security Benefits**). However:
- **WinRM** is used for remote PowerShell management. Disabling it means you can't manage this PC remotely.
- **SysMain** preloads apps to speed up loading. With modern SSDs it mostly fills RAM with apps you may not use, and they load quickly anyway.
- **LanmanServer** is required for file and printer sharing. Disabling it stops this PC from sharing files or printers with other PCs.
- **WSearch** powers indexed search. Start menu and File Explorer searches will be slower.
- **Remote Desktop\*** disables Remote Desktop connections to this PC.
- **WSLService** is required for Windows Subsystem for Linux. Remove it from the list if you use WSL.

Most home users won't notice any issues. If you run into problems, re-enable any service in `services.msc`, or remove it from the INI so the app leaves it alone.
