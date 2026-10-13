<#
.SYNOPSIS
    IP Switch integration tests that change the real network (administrator required).

.DESCRIPTION
    Loads IPSwitch.exe itself into Windows PowerShell 5.1 (.NET Framework) and runs its real code
    against ONE test adapter, verifying the result with the Windows networking cmdlets:

      - Applying a static profile (IP + secondary IP + DNS) and DHCP through netsh.
      - Capturing the current configuration (basis of "save as profile" and of the rollback).
      - Connectivity check (unreachable / reachable host).
      - Automatic rollback when there is no connectivity (ProfileApplier).
      - Disabling, listing as disabled, enabling and restarting the adapter.
      - Startup scheduled task (only if the user does not already have one).
      - Command line: --apply with exit codes 0 / 2.
      - Global hotkey registration and WLAN query.

    WARNING: it temporarily changes the configuration of the given adapter (if it is the active
    connection, the computer is offline for 1-2 minutes). At the start it captures its configuration
    (IP and DNS, static or automatic) and at the end it ALWAYS leaves it enabled with that same
    configuration. It also writes a readable copy to "<Log>.original-config.txt" in case it has to be
    restored by hand.

    Application data (profiles and settings) goes to an isolated "<Log folder>\it-data" folder through
    IPSWITCH_DATA_DIR, so the user's real profiles in %AppData%\IPSwitch are never touched.

.PARAMETER Exe
    Path of IPSwitch.exe (preferably a copy in a test folder).

.PARAMETER Adapter
    Name of the test adapter.

.PARAMETER Log
    File the results are written to (PASS/FAIL per test).

.EXAMPLE
    Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList '-NoProfile -ExecutionPolicy Bypass -File .\tests\Integration\Invoke-AdminIntegrationTests.ps1 -Exe C:\Temp\ipswitch-test\IPSwitch.exe -Adapter "VMware Network Adapter VMnet1" -Log C:\Temp\ipswitch-test\it.log'
#>
#Requires -RunAsAdministrator
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Exe,
    [Parameter(Mandatory)][string]$Adapter,
    [Parameter(Mandatory)][string]$Log
)

$ErrorActionPreference = 'Stop'
$results = New-Object System.Collections.Generic.List[string]
$failures = 0

function Write-Result([bool]$ok, [string]$name, [string]$detail = '') {
    $line = '{0}  {1}{2}' -f ($(if ($ok) { 'PASS' } else { 'FAIL' })), $name, $(if ($detail) { "  ($detail)" } else { '' })
    $results.Add($line)
    if (-not $ok) { $script:failures++ }
}

function Wait-Until([scriptblock]$condition, [int]$seconds = 20) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) { if (& $condition) { return $true }; Start-Sleep -Milliseconds 500 }
    return $false
}

function Get-IPv4([string]$name) { @(Get-NetIPAddress -InterfaceAlias $name -AddressFamily IPv4 -ErrorAction SilentlyContinue | ForEach-Object { $_.IPAddress }) }
function Get-Dhcp([string]$name) { (Get-NetIPInterface -InterfaceAlias $name -AddressFamily IPv4).Dhcp -eq 'Enabled' }
function Get-Dns([string]$name) { @((Get-DnsClientServerAddress -InterfaceAlias $name -AddressFamily IPv4).ServerAddresses) }

function New-Profile([string]$name, [string]$ip = 'DHCP', [string]$mask = 'DHCP', [string]$gateway = 'DHCP',
                     [string]$dns1 = 'DHCP', [string]$dns2 = 'DHCP', [string[]]$secondary = @()) {
    $p = New-Object IPSwitch.Models.IPProfile
    $p.Name = $name; $p.Interface = $Adapter
    $p.IPAddress = $ip; $p.SubnetMask = $mask; $p.Gateway = $gateway
    $p.PrimaryDNS = $dns1; $p.SecondaryDNS = $dns2
    $list = New-Object 'System.Collections.Generic.List[string]'
    foreach ($s in $secondary) { $list.Add($s) }
    $p.SecondaryAddresses = $list
    return $p
}

Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase, System.Web.Extensions
# Compiled "do not keep changes" delegate (script blocks cannot run on thread-pool threads)
Add-Type -TypeDefinition @'
using System.Threading.Tasks;
public static class ItHelper { public static Task<bool> NeverKeep(int seconds) { return Task.FromResult(false); } }
'@

$dhcpProfile = $null
$original = $null
# Isolated data folder: inherited by the CLI child processes and read by AppPaths when the assembly loads
$dataDir = Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($Log))) 'it-data'
New-Item -ItemType Directory -Force $dataDir | Out-Null
$env:IPSWITCH_DATA_DIR = $dataDir

try {
    $assembly = [System.Reflection.Assembly]::LoadFile($Exe)
    # Dependencies are embedded with Costura: hook its loader before using any type
    $attach = $assembly.GetType('Costura.AssemblyLoader').GetMethods([Reflection.BindingFlags]'Static,Public,NonPublic') |
        Where-Object Name -eq 'Attach' | Select-Object -First 1
    if ($attach.GetParameters().Count -eq 1) { [void]$attach.Invoke($null, @($true)) } else { [void]$attach.Invoke($null, $null) }
    $dhcpProfile = New-Profile 'IT-DHCP'

    # 0. Initial state (saved to restore it exactly at the end)
    $nic = Get-NetAdapter -Name $Adapter
    Write-Result ($nic.Status -eq 'Up') 'Adapter present and up' $nic.InterfaceDescription
    $original = [IPSwitch.Services.NetworkService]::CaptureCurrentConfig($Adapter)
    Write-Result ($null -ne $original) 'CaptureCurrentConfig (initial)' $(if ($original) {
        "ip={0} mask={1} gw={2} dns={3},{4}" -f $original.IPAddress, $original.SubnetMask, $original.Gateway, $original.PrimaryDNS, $original.SecondaryDNS } else { 'null' })
    # Readable copy in case it has to be restored by hand
    $backup = "$Log.original-config.txt"
    @("Adapter: $Adapter", "IP: $($original.IPAddress)", "Mask: $($original.SubnetMask)", "Gateway: $($original.Gateway)",
      "DNS1: $($original.PrimaryDNS)", "DNS2: $($original.SecondaryDNS)") | Set-Content $backup -Encoding UTF8

    # 1. Static profile with secondary IP and DNS
    $static = New-Profile 'IT-Static' '192.168.250.10' '255.255.255.0' 'DHCP' '1.1.1.1' '9.9.9.9' @('192.168.251.10/24')
    Write-Result $static.IsValid 'Static profile is valid'
    $r = [IPSwitch.Services.NetworkService]::ApplyProfile($static).GetAwaiter().GetResult()
    Write-Result $r.Success 'ApplyProfile static' $r.Message
    $ok = Wait-Until { (Get-IPv4 $Adapter) -contains '192.168.250.10' -and (Get-IPv4 $Adapter) -contains '192.168.251.10' }
    Write-Result $ok 'Static: primary and secondary IPs set' ((Get-IPv4 $Adapter) -join ', ')
    Write-Result (-not (Get-Dhcp $Adapter)) 'Static: DHCP disabled'
    $dns = Get-Dns $Adapter
    Write-Result ($dns[0] -eq '1.1.1.1' -and $dns[1] -eq '9.9.9.9') 'Static: DNS servers in order' ($dns -join ', ')

    # 2. Capture of the static configuration
    $captured = [IPSwitch.Services.NetworkService]::CaptureCurrentConfig($Adapter)
    $okCapture = $captured.IPAddress -eq '192.168.250.10' -and $captured.SubnetMask -eq '255.255.255.0' -and
                 ($captured.SecondaryAddresses -contains '192.168.251.10/24') -and $captured.PrimaryDNS -eq '1.1.1.1' -and $captured.SecondaryDNS -eq '9.9.9.9'
    Write-Result $okCapture 'CaptureCurrentConfig (static)' ("{0} {1} sec={2} dns={3},{4}" -f $captured.IPAddress, $captured.SubnetMask, ($captured.SecondaryAddresses -join ';'), $captured.PrimaryDNS, $captured.SecondaryDNS)

    # 3. Back to DHCP
    $r = [IPSwitch.Services.NetworkService]::ApplyProfile($dhcpProfile).GetAwaiter().GetResult()
    Write-Result $r.Success 'ApplyProfile DHCP' $r.Message
    Write-Result (Wait-Until { Get-Dhcp $Adapter }) 'DHCP: enabled again'
    Write-Result (Wait-Until { (Get-IPv4 $Adapter) -notcontains '192.168.251.10' }) 'DHCP: secondary IP removed'
    Write-Result ((Get-Dns $Adapter).Count -eq 0 -or -not ((Get-Dns $Adapter) -contains '9.9.9.9')) 'DHCP: static DNS removed'

    # 4. Connectivity
    $unreachable = New-Profile 'IT-NoGw' '192.168.250.10' '255.255.255.0' '192.168.250.254'
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $reach = [IPSwitch.Services.NetworkService]::WaitForConnectivityAsync($unreachable, '', [TimeSpan]::FromSeconds(4)).GetAwaiter().GetResult()
    Write-Result (-not $reach) 'WaitForConnectivity: unreachable gateway -> false' ("{0:N1}s" -f $sw.Elapsed.TotalSeconds)
    $reach = [IPSwitch.Services.NetworkService]::WaitForConnectivityAsync($unreachable, '127.0.0.1', [TimeSpan]::FromSeconds(4)).GetAwaiter().GetResult()
    Write-Result $reach 'WaitForConnectivity: custom host 127.0.0.1 -> true'

    # 5. Automatic rollback: profile without connectivity and the user does not confirm
    $settings = New-Object IPSwitch.Models.AppSettings
    $settings.VerifyConnectivity = $true
    $settings.VerifyHost = ''
    $settings.RevertTimeoutSeconds = 5
    $neverKeep = [Delegate]::CreateDelegate([Func[int, System.Threading.Tasks.Task[bool]]], [ItHelper].GetMethod('NeverKeep'))
    $outcome = [IPSwitch.Services.ProfileApplier]::ApplyAsync($unreachable, $settings, $neverKeep).GetAwaiter().GetResult()
    Write-Result ($outcome.Item1.ToString() -eq 'RolledBack') 'ProfileApplier: rolls back without connectivity' ("{0} {1}" -f $outcome.Item1, $outcome.Item2)
    Write-Result (Wait-Until { Get-Dhcp $Adapter }) 'Rollback restored DHCP'

    # 6. Disable / enable / restart
    $r = [IPSwitch.Services.AdapterService]::SetEnabledAsync($Adapter, $false).GetAwaiter().GetResult()
    Write-Result $r.Success 'Disable adapter' $r.Message
    Write-Result (Wait-Until { (Get-NetAdapter -Name $Adapter).Status -eq 'Disabled' }) 'Adapter reported Disabled by Windows'
    $disabled = [IPSwitch.Services.AdapterService]::GetDisabledAdapters() | ForEach-Object { $_.Name }
    Write-Result ($disabled -contains $Adapter) 'GetDisabledAdapters lists it (WMI)' ($disabled -join ', ')
    $r = [IPSwitch.Services.AdapterService]::SetEnabledAsync($Adapter, $true).GetAwaiter().GetResult()
    Write-Result $r.Success 'Enable adapter' $r.Message
    Write-Result (Wait-Until { (Get-NetAdapter -Name $Adapter).Status -eq 'Up' } 30) 'Adapter back Up'
    $r = [IPSwitch.Services.NetworkService]::RestartAdapterAsync($Adapter).GetAwaiter().GetResult()
    Write-Result $r.Success 'Restart adapter' $r.Message
    Write-Result (Wait-Until { (Get-NetAdapter -Name $Adapter).Status -eq 'Up' } 30) 'Adapter Up after restart'

    # 7. Startup task (left untouched if the user already has one)
    if ([IPSwitch.Services.StartupService]::IsEnabled()) {
        Write-Result $true 'Startup task' 'SKIPPED: an IPSwitch task already exists'
    } else {
        Write-Result ([IPSwitch.Services.StartupService]::SetEnabled($true)) 'Startup task created'
        $xml = [xml](schtasks /Query /TN IPSwitch /XML | Out-String)
        $runLevel = $xml.Task.Principals.Principal.RunLevel
        $arguments = $xml.Task.Actions.Exec.Arguments
        Write-Result ($runLevel -eq 'HighestAvailable' -and $arguments -match '--minimized') 'Startup task: elevated, --minimized' "$runLevel $arguments"
        Write-Result ([IPSwitch.Services.StartupService]::SetEnabled($false) -and -not [IPSwitch.Services.StartupService]::IsEnabled()) 'Startup task removed'
    }

    # 8. Command line
    $dataFile = Join-Path $dataDir 'profiles.json'
    $serializer = New-Object System.Web.Script.Serialization.JavaScriptSerializer
    $existing = if (Test-Path $dataFile) { [System.Collections.ArrayList]$serializer.DeserializeObject((Get-Content $dataFile -Raw)) } else { New-Object System.Collections.ArrayList }
    $existing = [System.Collections.ArrayList]@($existing | Where-Object { $_['Name'] -notlike 'IT-CLI-*' })
    foreach ($p in @(
        @{ Name = 'IT-CLI-Static'; IPAddress = '192.168.250.20'; SubnetMask = '255.255.255.0'; Gateway = 'DHCP'; PrimaryDNS = 'DHCP'; SecondaryDNS = 'DHCP'; Interface = $Adapter },
        @{ Name = 'IT-CLI-DHCP'; IPAddress = 'DHCP'; SubnetMask = 'DHCP'; Gateway = 'DHCP'; PrimaryDNS = 'DHCP'; SecondaryDNS = 'DHCP'; Interface = $Adapter })) { [void]$existing.Add($p) }
    New-Item -ItemType Directory -Force (Split-Path $dataFile) | Out-Null
    Set-Content -Path $dataFile -Value $serializer.Serialize($existing) -Encoding UTF8

    $cli = Start-Process $Exe -ArgumentList '--apply "IT-CLI-Static"' -Wait -PassThru
    Write-Result ($cli.ExitCode -eq 0) 'CLI --apply static: exit 0' "exit $($cli.ExitCode)"
    Write-Result (Wait-Until { (Get-IPv4 $Adapter) -contains '192.168.250.20' }) 'CLI static IP applied'
    $cli = Start-Process $Exe -ArgumentList '--apply "it-cli-dhcp"' -Wait -PassThru
    Write-Result ($cli.ExitCode -eq 0) 'CLI --apply (case-insensitive) DHCP: exit 0' "exit $($cli.ExitCode)"
    Write-Result (Wait-Until { Get-Dhcp $Adapter }) 'CLI DHCP applied'
    $cli = Start-Process $Exe -ArgumentList '--apply "IT-Does-Not-Exist"' -Wait -PassThru
    Write-Result ($cli.ExitCode -eq 2) 'CLI unknown profile: exit 2' "exit $($cli.ExitCode)"

    # 9. Global hotkeys and WLAN (no network changes)
    $window = New-Object System.Windows.Window
    $hotkeys = New-Object IPSwitch.Services.HotkeyService($window, [Action[IPSwitch.Models.IPProfile]] { param($p) })
    $hk = New-Profile 'IT-Hotkey'; $hk.Hotkey = 'Ctrl+Alt+Shift+F11'
    $failed = $hotkeys.RegisterAll([IPSwitch.Models.IPProfile[]]@($hk))
    Write-Result ($failed.Count -eq 0) 'HotkeyService registers Ctrl+Alt+Shift+F11'
    $hotkeys.Dispose()
    $ssids = [IPSwitch.Services.WlanApi]::GetConnectedSsids()
    Write-Result $true 'WlanApi.GetConnectedSsids runs' ("{0} connected WiFi adapter(s)" -f $ssids.Count)
}
catch {
    Write-Result $false 'Unexpected exception' $_.Exception.ToString()
}
finally {
    # ALWAYS restore: adapter enabled and with the configuration captured at the start
    netsh interface set interface name="$Adapter" admin=enabled | Out-Null
    [void](Wait-Until { (Get-NetAdapter -Name $Adapter).Status -eq 'Up' } 30)
    $restored = $false
    if ($original) {
        try {
            $r = [IPSwitch.Services.NetworkService]::ApplyProfile($original).GetAwaiter().GetResult()
            $restored = $r.Success
        } catch { $restored = $false }
    }
    if (-not $restored) {
        netsh interface ip set address name="$Adapter" source=dhcp | Out-Null
        netsh interface ip set dns name="$Adapter" source=dhcp | Out-Null
    }
    if ($original) {
        $okRestore = Wait-Until {
            $isDhcp = Get-Dhcp $Adapter
            $dnsNow = Get-Dns $Adapter
            $ipOk = if ($original.IPAddress -eq 'DHCP') { $isDhcp } else { (Get-IPv4 $Adapter) -contains $original.IPAddress }
            $dnsOk = if ($original.PrimaryDNS -eq 'DHCP') { $true } else { $dnsNow.Count -gt 0 -and $dnsNow[0] -eq $original.PrimaryDNS }
            $ipOk -and $dnsOk -and (Get-NetAdapter -Name $Adapter).Status -eq 'Up'
        } 30
        Write-Result $okRestore 'Final state restored to the original configuration' ("dhcp={0} ip={1} dns={2}" -f (Get-Dhcp $Adapter), ((Get-IPv4 $Adapter) -join ','), ((Get-Dns $Adapter) -join ','))
    } else {
        Write-Result ((Get-Dhcp $Adapter) -and (Get-NetAdapter -Name $Adapter).Status -eq 'Up') 'Final state restored (enabled + DHCP)'
    }
    $results.Add(("SUMMARY: {0} passed, {1} failed" -f ($results.Count - $failures), $failures))
    $results | Set-Content -Path $Log -Encoding UTF8
}
exit $failures
