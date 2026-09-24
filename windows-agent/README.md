# Windows companion

## Standalone EXE

Run the EXE as your normal Windows user. It places itself in the per-user app
folder, offers the detected PC network addresses, and creates configuration and
a private TLS certificate. Choose whether to start at Windows sign-in and approve
the firewall elevation prompt. No manual JSON editing, SDK, or runtime is needed.
The firewall rule is limited to the selected interface, port, application, local
subnet and Private network profile. If elevation is canceled, retry **Configure
firewall** from the tray menu. Administrator credentials are needed for that step.

Open SmartThings settings, enter the PC address and 8-digit code, then compare
all four certificate groups on its music card with the PC pairing window. Toggle **Confirm certificate** in SmartThings settings only when they match. The hub address, certificate
trust and internal credentials are saved automatically. No certificate is built
into the generic driver. Reopening the EXE does not overwrite configuration or
regenerate an established certificate. To replace an already running EXE, exit
the existing app from the tray first. Reserve the PC address in your router.

Build this distribution from the repository root:

```powershell
dotnet publish windows-agent/STMediaBridge.Agent.csproj -p:PublishProfile=Standalone -o artifacts/standalone
```

The resulting `STMediaBridge.Agent.exe` is self-contained for Windows x64.
Only the EXE is needed to run. The procedures below remain available for developers
using a multi-file build or existing script-based installations.

## Build

From the repository root, with the SDK pinned in `global.json` (9.0.318):

```powershell
dotnet publish windows-agent/STMediaBridge.Agent.csproj -c Release -r win-x64 --self-contained true -o artifacts/windows-x64
Copy-Item LICENSE,THIRD_PARTY_NOTICES.md artifacts/windows-x64/
```

For ARM64, use `win-arm64` and a matching output folder. Self-contained publishing includes the runtimes. Framework-dependent builds need both the .NET 8 Windows Desktop Runtime and ASP.NET Core Runtime. No Visual Studio, Python, or Node runtime is required by the published agent.

## Install for the signed-in user

Reserve the PC and hub addresses with DHCP. In a normal PowerShell window, set `$pcIp` and `$hubIp` to their actual IPv4 addresses and run:

```powershell
.\windows-agent\scripts\Install.ps1 -PublishDirectory .\artifacts\windows-x64 -PcAddress $pcIp -HubAddress $hubIp
```

This copies the application to `%LOCALAPPDATA%\STMediaBridge\bin`, generates a random 128-bit token (32 hexadecimal characters) and stable device ID, restricts the configuration file ACL, and registers a per-user Task Scheduler task. It starts immediately and on that user's future sign-ins, with limited privileges and no console. It can run on battery. Rerunning the script preserves identity and valid 32-character tokens. Legacy 64-character tokens are replaced with fresh random secrets; pair again using the numeric code after migration.

The executable is a user-session background agent, not an SCM/LocalSystem service. It stops being useful after sign-out. Do not configure it to run when the user is logged out, or install one copy for each simultaneously logged-in user.

Allow just the hub through Windows Firewall. Run the following in **elevated PowerShell as the same Windows user**:

```powershell
.\windows-agent\scripts\Set-Firewall.ps1 `
  -ConfigPath "$env:LOCALAPPDATA\STMediaBridge\agent.json" `
  -AgentExe "$env:LOCALAPPDATA\STMediaBridge\bin\STMediaBridge.Agent.exe"
```

The rule allows the configured TCP port only on the Private network profile, only at the selected PC interface, and only from the configured hub's IP. Do not accept a broader automatic firewall prompt. If elevation uses a different administrator account, supply the original user's absolute paths and arrange access to the configuration yourself.

## Pair

Right-click the blue music-note icon in the Windows notification area and choose **Pairing information** (or double-click). The window shows PC address, port and a large **8-digit numeric pairing code**. Enter the address and code in SmartThings device settings; the default port is 8765. No UUID or token needs typing or clipboard sharing. **New code** replaces an expired code without disconnecting an already paired hub. Each code lasts ten minutes; once paired, the driver saves the internal credentials for future restarts. The interface uses Korean on Korean Windows and English otherwise.

Closing the information window keeps the companion running. **Exit ST Windows Media Control** stops it, including LAN control. To start it again before the next sign-in:

```powershell
$taskName = 'ST MediaBridge-' + [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
Start-ScheduledTask -TaskName $taskName
```

The tray reports local process/audio status, not confirmation that a hub is paired. Configuration is also available in `%LOCALAPPDATA%\STMediaBridge\agent.json`. Do not post it in issues or commit it. Keep the ID stable across upgrades/reboots.

For an uninstalled development instance:

```powershell
dotnet windows-agent/bin/Debug/net8.0-windows10.0.19041.0/STMediaBridge.Agent.dll --init --config .\agent.json
dotnet windows-agent/bin/Debug/net8.0-windows10.0.19041.0/STMediaBridge.Agent.dll --config .\agent.json
```

`--init` creates a loopback-only configuration and refuses to overwrite it. To enable LAN access, edit `bindAddress` to the PC's explicit IPv4 address and `hubAddress` to the hub's address. Wildcard binding is rejected. All configuration changes require an agent restart. The first command only creates configuration; the second runs the agent in the foreground for development.

For diagnostics without the tray UI, add `--no-tray` when launching the agent. Initialization and token rotation do not create a tray icon.

## Logs and troubleshooting

Logs are alongside configuration in `logs\agent.log`, with one rotated backup; each is approximately 1 MB maximum. Normal logs contain availability transitions and failed command types, not tokens or song metadata.

```powershell
Get-Content "$env:LOCALAPPDATA\STMediaBridge\logs\agent.log" -Tail 50 -Wait
Get-ScheduledTask | Where-Object TaskName -Like 'ST MediaBridge-*'
```

- No connection: check task state, configured IP, Private network profile, hub address, VLAN routing and the firewall rule. A changed DHCP address requires configuration and firewall updates.
- HTTP 401: token mismatch. HTTP 403: source is not the configured hub or loopback. Control/state endpoints require the internal token; the short-code exchange is also restricted to the configured hub or loopback.
- Audio unavailable: enable an output device and set the Windows default multimedia output. Endpoint switches and USB unplug/replug are observed; a 20-second refresh also recovers missed notifications.
- Playback unavailable: open a GSMTC-compatible app and start a track locally. Merely producing sound does not guarantee GSMTC support. Windows chooses the current session.
- HTTP 409: no endpoint/session, unsupported action, application refusal, or media-operation timeout. It is not a successful command. A timed-out native media request can complete late; do not automatically retry next/previous/toggle.

## Token migration and rotation

The token is exactly **32 hexadecimal characters / 128 random bits**, generated with the system cryptographic RNG. All-zero tokens and UUIDs are rejected. HTTP still uses constant-time comparison of the complete, case-sensitive bearer credential.

Earlier builds generated 64-character tokens. They are deliberately rejected by the new agent and driver; **do not truncate them**. Rebuild the companion before upgrading. Running the updated installer generates a fresh token for legacy configurations and keeps the existing device ID/network configuration (unless you supply different addresses). Existing valid 32-character tokens remain unchanged.

For a development configuration, or explicit rotation without reinstalling, stop the agent, then run the **newly built** executable:

```powershell
$configPath = "$env:LOCALAPPDATA\STMediaBridge\agent.json"
$taskName = 'ST MediaBridge-' + [System.Security.Principal.WindowsIdentity]::GetCurrent().User.Value
Stop-ScheduledTask -TaskName $taskName
$rotation = Start-Process -FilePath .\artifacts\windows-x64\STMediaBridge.Agent.exe `
  -ArgumentList ('--rotate-token --config "' + $configPath + '"') -WindowStyle Hidden -Wait -PassThru
if ($rotation.ExitCode -ne 0) { throw 'Rotation failed; inspect logs next to agent.json.' }
```

This updates only the token in `agent.json`, preserves the UUID/network settings and file ACL, and never prints the secret. For a development file, set `$configPath` to its absolute path and omit the scheduled-task stop if no task exists. Restart using the rebuilt companion (or rerun the installer to update installed binaries), then pair again using the tray's 8-digit code. The old running agent otherwise retains its old credential until restarted. Do not change `deviceId` unless intentionally replacing the pairing identity.

## Uninstall

```powershell
.\windows-agent\scripts\Uninstall.ps1
```

This stops/removes only the current user's startup task and retains files. In elevated PowerShell remove the firewall rule with `Remove-NetFirewallRule -Name "STMediaBridge-<deviceId>"`, substituting your ID. You can then delete the retained application folder and remove the SmartThings device/driver.

### Reset pairing

Right-click the tray icon → **Reset pairing…** and confirm. This revokes the old
internal identity and secret, restarts the companion host, and shows a new
8-digit code. Enter that code in the existing SmartThings device's settings.
PC address/port and firewall configuration are retained. Cancel changes nothing.
Use **New code** inside Pairing information when only the temporary code expired;
use **Reset pairing** when the existing connection should be revoked.

## Automatic startup

Right-click the tray icon and toggle **Windows 로그인 시 자동 실행 / Start automatically at Windows sign-in**.
The checkmark reflects the current setting. Enabling registers this user's app
under Windows Startup apps (HKCU Run); disabling removes that registration.
Changing the option disables the installer's existing login task to avoid two
startup entries. The app keeps running when this option is disabled; it only
changes behavior at the next sign-in. Keep the executable in its installed location.
Reinstalling preserves a disabled startup setting. Uninstall removes the Run entry
and the existing scheduled task. No release has been published yet.

### Smaller download with separately installed runtimes

The optional Compact profile produces a framework-dependent single EXE (about
24.8 MiB with the current SDK, versus 85.3 MiB for Standalone). It requires x64
.NET 8 Windows Desktop Runtime and ASP.NET Core Runtime on the user's PC.
These runtimes are not included with Windows by default. This reduces the app
download, not the total runtime footprint. Standalone remains the default for
users who want to run the EXE without installing prerequisites.

```powershell
dotnet publish windows-agent/STMediaBridge.Agent.csproj -p:PublishProfile=Compact -p:RestoreLockedMode=true -o artifacts/compact
```

### Per-app volume selection

Tray → App Volume Controls lists current and previously discovered audio apps,
with icons when available. Checkboxes are the sole exposure control; no app is
selected automatically. Selections persist in the private `audio-apps.json`
next to `agent.json`. Apps without a current session remain selectable.
The matching Edge driver creates/deletes volume-only children on reconnect.
Changes use Core Audio session events, never GSMTC. See `docs/protocol.md` for
identity, multi-session aggregation, and lifecycle semantics.
