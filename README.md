# ST Windows Media Control

Control Windows media playback, system volume, and up to five apps from one SmartThings device.

A Windows companion runs in your notification area and connects directly to a SmartThings Edge hub over your local network. Compatible media apps provide playback controls and track information through Windows media sessions.

**[Download for Windows x64](https://github.com/narsimplyme/st-windows-media-control/releases/latest)** · **[Enroll your SmartThings hub](https://bestow-regional.api.smartthings.com/invite/kVlnanYee249)**

## Features

- System volume and mute, synchronized with the default Windows multimedia output.
- Play/pause and previous/next track for supported media apps.
- Volume and mute for up to five selected apps, inside the same SmartThings device.
- Tray controls, pairing information, and optional startup at Windows sign-in.
- Automatic configuration and certificate creation on first launch.
- An 8-digit pairing code and HTTPS with explicit certificate verification.

## Requirements

- Windows x64 with a signed-in user. The application targets Windows 10 version 1809 or later and Windows 11; use a supported Windows version.
- A SmartThings Edge hub and a Samsung account in the SmartThings app.
- PC and hub on the same IPv4 subnet, with the PC network set to **Private**.
- Administrator approval for the Windows firewall rule. Normal operation does not require running the companion as administrator.

The downloadable EXE includes its .NET runtime. Users do not need Visual Studio, .NET installation, or the SmartThings CLI. One PC per driver installation is currently supported.

## Setup

1. Download the EXE from [Releases](https://github.com/narsimplyme/st-windows-media-control/releases/latest) and run it as your normal Windows user.
2. On first launch, select the **PC IPv4 address on the hub's network**. If several addresses appear, select the relevant Ethernet or Wi-Fi connection rather than an unrelated VPN address. Choose whether to start at sign-in, then select **Set up and start** and approve the firewall request.
3. Open the [Enroll link](https://bestow-regional.api.smartthings.com/invite/kVlnanYee249), sign in, select your hub, and choose **Enroll**. Under **Available Drivers**, install **ST Windows Media Control**.
4. In SmartThings, choose **Add device → Scan nearby**. Open the new device → **⋮ → Settings**.
5. Open **Pairing information** from the PC tray icon. Enter its PC address and **8-digit pairing code** in SmartThings. Leave the port at **8765** unless you have changed it.
6. Compare all four certificate verification groups on the PC pairing window and the SmartThings music card. Only if they match, toggle **Confirm certificate** in SmartThings settings.
7. Check volume and playback. You can rename the device to identify your PC.

Enrollment, driver installation, and adding the device are separate steps. The initial PC setup window is skipped when an existing configuration is found. Closing a companion window keeps the tray app running; exiting the tray app stops the connection.

## App volume controls

Each SmartThings device has five fixed sections: **App 1** through **App 5**. You assign Windows apps to these slots from the PC; selecting an app does not create another SmartThings device.

1. Open the PC tray menu → **App Volume Controls** (앱별 볼륨 제어).
2. If an app is missing, play audio in it and click **Refresh apps** (앱 새로고침). If it has just started, allow a few seconds for detection and refresh again.
3. Check up to five apps. Read the **Slot** (슬롯) column to see which App 1–5 section controls each one.
4. In SmartThings, tap the pencil beside the matching section heading and enter the app name yourself. For example, if Spotify shows **App 2** on the PC, rename **App 2** to **Spotify**.
5. Use that section's volume and mute controls. The main media card and master-volume control still apply to the PC's current media session and default output.

**Use the Slot column, not the PC list's row order.** New selections fill the lowest available slot. Unchecking an app frees only its slot; the other apps do not move. Selecting a replacement app does not update the SmartThings heading—rename it manually.

Selections and slot assignments survive app exit and companion restart. An app needs an available audio session to accept commands; inactive or unassigned slots cannot change master volume. The five sections remain in SmartThings even when some are unused.

The PC list updates when opened or when **Refresh apps** is clicked. This is separate from background audio synchronization: Windows volume/mute changes continue to update SmartThings automatically.

See [app volume setup and migration](docs/app-components.md) for more detail.

## Updating and reconnecting

To update, exit the current companion from its tray menu, then run the new EXE. Existing configuration, certificate, and selected apps are retained. Normal updates do not require pairing again.

- **Expired pairing code:** open Pairing information and choose **New code**, then enter it in SmartThings. Codes expire after ten minutes; established connections do not.
- **Hub IP changed:** on companion v1.0.2 or later, choose **Reset pairing** in the PC tray, then enter the new code in SmartThings. This revokes the old credentials and lets the hub enroll from its new local address while retaining the PC certificate.
- **Firewall approval canceled:** retry **Configure firewall** from the tray menu.
- **PC IP changed:** automatic recovery is not yet implemented. Reserve the PC address in your router to avoid changes. Pairing reset does not change the PC's listening address.

## Scope and security

Media buttons work only with apps that expose a Windows media session, and individual apps may not support every command. Some protected processes cannot be identified for app-volume controls. SmartThings controls and layout may vary by mobile client.

The current installer is for Windows x64 and is not code-signed, so Windows may display a publisher warning. Each release includes SHA256 checksums. The companion runs in the signed-in user's session, not as a system service. It has no PC power-management or remote-shell features.

Pairing and control use HTTPS. Initial certificate discovery sends no secrets; compare the displayed verification groups before approving. A changed certificate requires verification again. After pairing, requests are restricted to the saved hub address and authenticated with an internal token. Private configuration and certificate files are accessible only to the owning Windows user and SYSTEM. See [protocol security notes](docs/protocol.md#security).

The local control connection does not require a third-party bridge. SmartThings account services, enrollment, app UI, and remote access still depend on the SmartThings platform. This is a community project, not a certified SmartThings integration.

## Development

Use the SDK pinned in `global.json`. See [Windows build instructions](windows-agent/README.md), [Edge driver documentation](edge-driver/README.md), [architecture](docs/architecture.md), and [validation status](docs/testing.md).

```powershell
dotnet build windows-agent/STMediaBridge.Agent.csproj
dotnet run --project tests/StateTests/StateTests.csproj
python -m pip install lupa==2.8 PyYAML==6.0.2
python tests/check_edge.py
python tests/http_smoke.py windows-agent/bin/Debug/net8.0-windows10.0.19041.0/STMediaBridge.Agent.dll
```

The smoke test uses temporary configuration and a loopback listener. Native audio tests are opt-in because they briefly change volume/mute and restore the previous values.

MIT licensed. See [LICENSE](LICENSE) and [third-party notices](THIRD_PARTY_NOTICES.md).

Developed using OpenAI Codex.

### Uninstall

Right-click the PC tray icon and choose **Uninstall…**. Confirm removal and approve
Windows administrator access to remove this installation's firewall rule. Canceling
administrator approval leaves application files and startup settings intact.

The uninstaller removes `%LOCALAPPDATA%\STMediaBridge` (installed binaries,
configuration, pairing credentials, certificates, app selections and logs), its
startup entries and legacy scheduled task, and the default .NET extraction cache
for `STMediaBridge.Agent`. Close any other copies of the companion first. If cleanup
fails, the dialog reports the error; rerun the uninstaller to remove remaining files.
The repository's `windows-agent/scripts/Uninstall.ps1` also performs this cleanup.

Delete the original downloaded EXE and the SmartThings device separately. Custom
`--config` installations and custom .NET extraction directories require manual
cleanup. Linked directories are refused to prevent deleting unrelated files.
