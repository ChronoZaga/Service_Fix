# Service Fix

A simple PowerShell script to stop and optimize unnecessary services on Windows for better performance and lower resource usage.

## Features
- Shows real-time status of gaming, bloatware, and telemetry services
- Color-coded list:
  - **Green** = Already in desired state
  - **Red** = Will be changed when you apply
- One-press action: Press **Enter** or **Space** to stop services and set correct startup types
- Services are split into two groups:
  - Gaming/Bloatware services → set to **Manual**
  - Telemetry/Diagnostic services → set to **Disabled**
- **Easy customization**: You can add or remove services from either list by editing the script (by DisplayName or technical Name)

## Services Affected

### Manual Group (Stopped + Set to Manual)
These are mostly gaming and bloatware services.

**Current services (by DisplayName or Name):**
- `*vmware*`, `*gigabyte*`, `*aorus*`, `*aomei*`, `*razer*`
- `NVIDIA FrameView SDK service`
- `*oculus*`, `*origin*`, `*easytune*`, `*gbtec*`
- `*EABackgroundService*`, `*EAAnti*`, `*rockstar*`, `*Epic *`
- `CMigrationService`, `*ccleaner*`, `*BattlEye*`, `*galaxy*`
- `*malwarebytes*`, `MSIV`, `OCButtonService`, `*Anti-Cheat*`
- `Windows Health and Optimized Experiences`
- `Inventory and Compatibility Appraisal service`
- `SamsungMagicianSVC`, `Haptic Service`

### Disabled Group (Stopped + Set to Disabled)
These are mostly telemetry and diagnostic services.

**Current services (by DisplayName or Name):**
- `Remote Desktop*`
- `DiagTrack`, `DPS`, `LanmanServer`, `SysMain`, `WerSvc`
- `WSearch`, `AUEPLauncher`, `wuqisvc`, `WinRM`
- `CDPSvc`, `CDPUserSvc_*`, `ADPSvc`, `PeerDistSvc`
- `CscService`, `pla`, `wercplsupport`, `RetailDemo`, `wcncsvc`

## How to Customize the Lists
You can easily **add or remove** services from either group by editing the script:

1. Open `Service Fix.ps1` in Notepad or any text editor.
2. Go to the `Get-DemServices` function (for Manual group) or `Get-DisabledServices` function (for Disabled group).
3. Add or remove items in the `-DisplayName` or `-Name` parameters.
   - Use wildcards `*` for partial matching (e.g. `*razer*`)
   - Use exact names for precision (e.g. `SamsungMagicianSVC`)
4. Save the file and run it again.

Example:
```powershell
# To add a new service to Manual group:
$SM1 = get-service -displayname *vmware*,*gigabyte*,*YOURNEW SERVICE*

## Important Warning
This script disables several core Windows services including **WinRM**, **SysMain** (Superfetch), **LanmanServer** (Server service), **DiagTrack**, and others. 

These services are turned off to improve scurity, limit telemetry, and tune Windows more to be a single user gaming machine. However:
- **WinRM** is used for remote PowerShell management — disabling it means you won't be able to manage this PC remotely.
- **SysMain** helps with application loading speed — however with modern SSD speeds it only serves to fill up your RAM with apps you may not use, and can be loaded in no time when needed.
- **LanmanServer** is required for file/printer sharing — disabling it will prevent this PC from acting as a file server.

Most home gaming users won't notice any issues, but re-enable any service via `services.msc` if you experience problems.
