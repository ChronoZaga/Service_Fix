$ErrorActionPreference = 'silentlycontinue'
[Console]::ForegroundColor = "Green"
[Console]::BackgroundColor = "Black"
function Get-DemServices {
   $SM1 = get-service -displayname *vmware*,*gigabyte*,*aorus*,*aomei*,*razer*,"NVIDIA FrameView SDK service",*oculus*,*origin*,*easytune*,*gbtec*,*EABackgroundService*,*EAAnti*,*rockstar*,*"Epic "*,CMigrationService,ccleaner*,*BattlEye*,*galaxy*,*malwarebytes*,MSIV,OCButtonService,*Anti-Cheat*,"Windows Health and Optimized Experiences","Inventory and Compatibility Appraisal service"
   $SM2 = get-service -name "SamsungMagicianSVC","Haptic Service"
   $SM1 + $SM2
}
function Get-DisabledServices {
   $SD1 = get-service -displayname "Remote Desktop*"
   $SD2 = get-service -Name "DiagTrack","DPS","LanmanServer","SysMain","WerSvc","WSearch","AUEPLauncher","wuqisvc","WinRM","CDPSvc","CDPUserSvc_*","ADPSvc","PeerDistSvc","CscService","pla","wercplsupport","RetailDemo","wcncsvc"
   $SD1 + $SD2
}
function Get-DeList {
    $manualServices = Get-DemServices | Select-Object DisplayName, Name, Status, StartType
    $disabledServices = Get-DisabledServices | Select-Object DisplayName, Name, Status, StartType
    $allServices = $manualServices + $disabledServices | Sort-Object DisplayName
    Write-Host "DisplayName".PadRight(49) "Name".PadRight(29) "Status".PadRight(14) "StartType" -ForegroundColor Green
    Write-Host ("-" * 49) ("-" * 29) ("-" * 14) ("-" * 15) -ForegroundColor Green
    foreach ($service in $allServices) {
        $displayName = $service.DisplayName.PadRight(50)
        $name = $service.Name.PadRight(30)
        $status = $service.Status.ToString().PadRight(15)
        $startType = $service.StartType.ToString()
        # Determine desired state and color
        $isManualGroup = $manualServices.Name -contains $service.Name
        $desiredStateMet = if ($isManualGroup) {
            $service.Status -eq 'Stopped' -and $service.StartType -eq 'Manual'
        } else {
            $service.Status -eq 'Stopped' -and $service.StartType -eq 'Disabled'
        }
        $lineColor = if ($desiredStateMet) { 'Green' } else { 'Red' }
        Write-Host -ForegroundColor $lineColor "$displayName$name$status$startType"
    }
    Write-Host ""
}
function Wait-ForUserInput {
    Write-Host "Press ENTER or SPACE to set services, or ESC to exit..." -NoNewline
    while ($true) {
        $key = [System.Console]::ReadKey($true)
        switch ($key.Key) {
            'Enter' {
                Write-Host ""
                return
            }
            'Spacebar' {
                Write-Host ""
                return
            }
            'Escape' {
                Write-Host ""
                Stop-Process -Id $PID
            }
        }
    }
}
function Set-AllDemServices {
while ($true) {
Clear-Host
Get-DeList
Wait-ForUserInput
Clear-Host
Write-Host "All superfluous services are being STOPPED..."
Get-DemServices | Where-Object { $_.Status -ne 'Stopped' } | Stop-Service -force
Get-DisabledServices | Where-Object { $_.Status -ne 'Stopped' } | Stop-Service -force
Start-Sleep 1
Write-Host "Setting startup of all superfluous services to MANUAL..."
Get-DemServices | Where-Object { $_.StartType -ne 'Manual' } | Set-Service -StartupType Manual
Write-Host "Setting startup of critical services to DISABLED..."
Get-DisabledServices | Where-Object { $_.StartType -ne 'Disabled' } | Set-Service -StartupType Disabled
Start-Sleep 1
}
}
Set-AllDemServices