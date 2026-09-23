# ST Windows Media Control

**Expose Windows system audio and media controls as a native SmartThings device.**

ST Windows Media Control connects a SmartThings Edge hub directly to a small Windows companion. One device provides master volume, mute, play/pause, and previous/next track. Windows audio changes are sent back immediately using Core Audio notifications. Compatible media apps supply playback state and metadata through Windows GSMTC.

**Status: experimental; playback and volume tested with a real SmartThings hub and Android app.** It is not a certified SmartThings integration. See [validation status](docs/testing.md).

## Features

- Exact volume 0–100 and mute in both directions, for the default multimedia output.
- Play, pause, next and previous through Windows' selected media session.
- Standard SmartThings capabilities, including optional title/artist/source metadata.
- Event-driven state updates, automatic reconnect, and periodic reconciliation.
- Token-authenticated LAN API restricted to a configured hub address.
- Pairing: type one 10-digit numeric code from the PC tray. Internal credentials are exchanged and saved automatically.
- Console-free startup at sign-in, with a tray status icon and pairing information with copy buttons.

No PC power management, remote shell, broker, or third-party bridge is included. The local control path has no external service dependency. SmartThings account, driver installation, app UI and remote access still use the SmartThings platform.

## Get started

**[Enroll / SmartThings 드라이버 설치](https://bestow-regional.api.smartthings.com/invite/kVlnanYee249)**

Samsung 계정 로그인 → 허브 선택 → Enroll → Available Drivers에서 **ST Windows Media Control** 설치. 사용자는 SmartThings CLI를 설치할 필요가 없습니다.

Requirements: Windows 10 with GSMTC support (1809 API baseline) or Windows 11; .NET 8 SDK or newer to build; a supported SmartThings Edge hub on the LAN; a signed-in Windows user. Use a currently supported Windows/.NET environment.

1. [Build and install the Windows companion](windows-agent/README.md). Reserve PC and hub IPv4 addresses in your router.
2. [Enroll and install the Edge driver](edge-driver/README.md), then pair with the PC address and 10-digit pairing code.
3. Rename the single device to **ST Windows Media Control — Gaming PC**.
4. Complete the [volume-first acceptance test](docs/testing.md) before testing media controls.

The agent runs after sign-in, not before login. Running it as LocalSystem in Session 0 would not reliably address the user's media apps. Only one user/session and one PC per installed driver are supported in v1.

Existing internal credentials are retained during upgrade. If reconnection is needed, open Pairing information in the PC tray and enter its 10-digit code in SmartThings Settings. There is no manual UUID/token entry.

## Repository

| Path | Purpose |
| --- | --- |
| `windows-agent/` | C# Core Audio/GSMTC companion and install scripts |
| `edge-driver/` | Lua driver and standard-capability profile |
| `docs/architecture.md` | Research, decisions and platform limitations |
| `docs/protocol.md` | HTTP API and synchronization contract |
| `docs/testing.md` | Automated checks and hardware acceptance tests |
| `tests/` | State-store, Lua, HTTP and optional native-audio tests |

Album artwork is not supported.

## Development

```powershell
dotnet build windows-agent/STMediaBridge.Agent.csproj
dotnet run --project tests/StateTests/StateTests.csproj
python -m pip install lupa==2.6 PyYAML==6.0.2
python tests/check_edge.py
python tests/http_smoke.py windows-agent/bin/Debug/net8.0-windows10.0.19041.0/STMediaBridge.Agent.dll
```

The basic smoke test uses a temporary token and loopback listener, then exits. Native audio tests are opt-in because they briefly change volume/mute and restore the previous values. No test installs startup tasks or firewall rules.

## Limitations and security

GSMTC works only with apps that publish media sessions. Windows selects the current session; an unsupported skip returns a rejection. Source uses the Windows app display name when available, falling back to the app model ID. Standard SmartThings presentations vary between mobile clients; the requested conceptual layout cannot be guaranteed.

V1 uses HTTP bearer authentication, not TLS. The token and media metadata are visible to someone able to observe LAN traffic. Use a trusted private LAN, keep the firewall scoped to the hub, and never port-forward the agent. Read the [protocol security notes](docs/protocol.md#security). The Edge driver persists internal credentials in device fields; this is not a separate secret vault.

MIT licensed. See [LICENSE](LICENSE) and [third-party notices](THIRD_PARTY_NOTICES.md). Contributions should keep volume synchronization reliable and avoid adding PC power-management features.

## Project name and upgrades

ST Windows Media Control was previously called ST MediaBridge. The public app,
tray, driver and documentation use the new name. Existing device labels can be
renamed in SmartThings; user-customized labels are not overwritten.

For in-place upgrades, the executable/project name `STMediaBridge.Agent`, C#
namespace, `%LOCALAPPDATA%\STMediaBridge` configuration directory, scheduled task
`ST MediaBridge-<SID>`, firewall rule IDs, Edge package key and profile/network IDs
remain stable. Renaming these is not required to use the new product name and
would otherwise risk duplicate devices or loss of saved pairing. No re-pairing
is required solely for this name change.

Suggested public repository name: `st-windows-media-control`.

## Development

Maintained by [narsimplyme](https://github.com/narsimplyme).
Developed with assistance from OpenAI Codex. AI assistance is acknowledged here;
GitHub commit authorship remains with the human maintainer.
