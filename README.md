# ST Windows Media Control

**Expose Windows system audio and media controls as a native SmartThings device.**

ST Windows Media Control connects a SmartThings Edge hub directly to a small Windows companion. One device provides master volume, mute, play/pause, and previous/next track. Windows audio changes are sent back immediately using Core Audio notifications. Compatible media apps supply playback state and metadata through Windows GSMTC.

**Status: experimental; playback and volume tested with a real SmartThings hub and Android app.** It is not a certified SmartThings integration. See [validation status](docs/testing.md).

## Features

- Exact volume 0–100 and mute in both directions, for the default multimedia output.
- Play, pause, next and previous through Windows' selected media session.
- Standard SmartThings capabilities, including optional title/artist/source metadata.
- Event-driven state updates, automatic reconnect, and periodic reconciliation.
- HTTPS LAN API with a saved PC certificate and token authentication.
- First-run setup creates configuration and a private certificate automatically; no runtime installation is needed for the standalone EXE.
- Pairing: enter the PC address and a 8-digit code, compare the certificate verification values once, and approve. Later connections reuse saved trust.
- Console-free startup at sign-in, with a tray status icon and pairing information with copy buttons.

No PC power management, remote shell, broker, or third-party bridge is included. The local control path has no external service dependency. SmartThings account, driver installation, app UI and remote access still use the SmartThings platform.

## Get started

**[Enroll / SmartThings 드라이버 설치](https://bestow-regional.api.smartthings.com/invite/kVlnanYee249)**

Samsung 계정 로그인 → 허브 선택 → Enroll → Available Drivers에서 **ST Windows Media Control** 설치. 사용자는 SmartThings CLI를 설치할 필요가 없습니다.

Requirements: Windows 10 with GSMTC support (1809 API baseline) or Windows 11; a supported SmartThings Edge hub on the LAN; a signed-in Windows user. Building from source requires the SDK pinned in `global.json`; the standalone EXE includes its runtime. Use a currently supported Windows/.NET environment.

1. Run the standalone Windows EXE. Select the detected PC address, choose whether to start at sign-in, and approve the firewall prompt. Developers can [build the EXE](windows-agent/README.md).
2. [Enroll and install the matching Edge driver](edge-driver/README.md), then enter the PC address and 8-digit pairing code.
3. Compare all four certificate groups on the PC and SmartThings music card. Only when they match, toggle **Confirm certificate** in device settings.
4. Rename the single device to **ST Windows Media Control — Gaming PC**.
5. Complete the [volume-first acceptance test](docs/testing.md) before testing media controls.

The agent runs after sign-in, not before login. Running it as LocalSystem in Session 0 would not reliably address the user's media apps. Only one user/session and one PC per installed driver are supported in v1.

Existing internal credentials are retained during upgrade. If reconnection is needed, open Pairing information in the PC tray and enter its 8-digit code in SmartThings Settings. There is no manual UUID/token entry.

## Repository

| Path | Purpose |
| --- | --- |
| `windows-agent/` | C# Core Audio/GSMTC companion and install scripts |
| `edge-driver/` | Lua driver and standard-capability profile |
| `docs/architecture.md` | Research, decisions and platform limitations |
| `docs/protocol.md` | HTTP API and synchronization contract |
| `docs/testing.md` | Automated checks and hardware acceptance tests |
| `tests/` | State-store, Lua, HTTP and optional native-audio tests |

## Development

```powershell
dotnet build windows-agent/STMediaBridge.Agent.csproj
dotnet run --project tests/StateTests/StateTests.csproj
python -m pip install lupa==2.8 PyYAML==6.0.2
python tests/check_edge.py
python tests/http_smoke.py windows-agent/bin/Debug/net8.0-windows10.0.19041.0/STMediaBridge.Agent.dll
```

The basic smoke test uses a temporary token and loopback listener, then exits. Native audio tests are opt-in because they briefly change volume/mute and restore the previous values. No test installs startup tasks or firewall rules.

## Limitations and security

GSMTC works only with apps that publish media sessions. Windows selects the current session; an unsupported skip returns a rejection. Source uses the Windows app display name when available, falling back to the app model ID. Standard SmartThings presentations vary between mobile clients; the requested conceptual layout cannot be guaranteed.

Control and pairing use verified TLS 1.2 or newer. Initial public-certificate discovery sends no secrets; the user must compare all four displayed certificate groups before approving. A changed certificate requires explicit verification again. The PC learns the hub address after successful code exchange and restricts subsequent traffic to that hub. Read the [protocol security notes](docs/protocol.md#security). The Edge driver persists trust and credentials in device fields; this is not a separate secret vault.

MIT licensed. See [LICENSE](LICENSE) and [third-party notices](THIRD_PARTY_NOTICES.md). Contributions should keep volume synchronization reliable and avoid adding PC power-management features.

Developed using OpenAI Codex.

## App volume controls

Open the Windows tray menu → **App Volume Controls** and check the apps to expose.
Each selection creates **PC <App name>** in SmartThings with volume and mute only.
Play audio once to discover an app. Previously discovered apps remain selectable
when closed. Closing an app retains its child; unchecking it deletes the child.
Windows Volume Mixer changes sync back to SmartThings. The existing parent keeps
master volume, mute, playback controls, and track information.
