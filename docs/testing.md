# Validation and acceptance tests

## Executed in this workspace

2026-09-21, Windows 11 host, .NET SDK 9.0.318 compiling the .NET 8 Windows target:

| Check | Result |
| --- | --- |
| Windows agent build | Passed, 0 warnings / 0 errors |
| Self-contained Windows x64 publish | Passed; executable available in `artifacts/windows-x64` |
| State/configuration test runner | 123 assertions passed, including 100 subscribe/update races, token bounds/sentinels and legacy-token migration |
| Lua source syntax, configuration/schema/revision tests | Passed using Lua 5.4 through Lupa; profile defaults/bounds checked against reported API constraints, each pairing sentinel rejected |
| Edge lifecycle harness | Passed with mocked SDK/transport; discovery deduplication, command mapping, duplicate/old-state suppression, restart epoch, preference change and removal |
| HTTP smoke test against actual Windows agent | Passed authentication, malformed values, unknown command rejection, missing route and restart cursor |
| Independent native Core Audio test | Passed HTTP → actual volume readback, external volume/mute → event stream, and HTTP mute → native readback; changes observed within 5 seconds |
| Audio test restoration | Original scalar volume and mute restored using independent Core Audio client |

No startup task or firewall rule was installed by these tests. Native tests use temporary loopback configuration and stop the test process. Test secrets are temporary and not logged.

**Not validated yet:** a real SmartThings hub, packaging/upload to the SmartThings account, Android/iOS presentation, slider interaction, installer/task/firewall execution, live GSMTC controls and metadata, USB output hotplug, actual hub/Windows reboot, or multi-application session switching. No active media session was present during local tests. CI is provided but has not been run on GitHub here.

Follow-up: the user tested packaging against the real API and supplied 422 responses for missing embedded preference defaults and the token's over-limit length. Both are addressed, including migration of the Windows/Lua token contract. The local checks enforce string bounds ≤36, length-valid explicit defaults, and no communication for sentinel configurations. The corrected package still needs a successful live upload; `smartthings` was not available on this agent session's PATH. The current official Sonos profile and CLI packaging source were inspected for the other profile fields.

These results prove the native audio/protocol portion locally; they do not claim the full SmartThings slider milestone has passed on hardware.


Playback follow-up: the user reported the standard card's Stop button did nothing. The driver previously had no stop handler. It now maps that command to pause, advertises a stable play/pause command set, and logs received playback commands. Lua regression checks verify repeated stop requests stay pause requests and explicit play resumes; real mobile-button behavior and the displayed icon still require retesting after driver update.


Tray follow-up (2026-09-22): nine checks pass for host-start gating, STA startup/shutdown, shutdown before startup, read-only pairing fields, token masking/reveal, copy-button presence and field width. The pairing window was rendered and visually inspected with dummy credentials. These checks do not exercise the real clipboard or certify Explorer interaction and all DPI settings on the user's desktop. HTTP smoke tests run with `--no-tray`.

## Automated checks

From the repository root:

```powershell
dotnet build windows-agent/STMediaBridge.Agent.csproj
dotnet run --project tests/StateTests/StateTests.csproj
dotnet run --project tests/TrayTests/TrayTests.csproj
python -m pip install lupa==2.6 PyYAML==6.0.2
python tests/check_edge.py
python tests/http_smoke.py windows-agent/bin/Debug/net8.0-windows10.0.19041.0/STMediaBridge.Agent.dll
```

The state-store suite checks signaling, cancellation, timeout snapshots, epoch mismatch, future cursors, identical-state suppression, concurrent field updates, and safe configuration defaults. The Lua harness loads the actual driver but substitutes platform/network interfaces; its scope is driver logic, not Edge runtime compatibility.

Optional native audio test, with a default multimedia output present:

```powershell
dotnet build tests/AudioProbe/AudioProbe.csproj
python tests/http_smoke.py windows-agent/bin/Debug/net8.0-windows10.0.19041.0/STMediaBridge.Agent.dll `
  --exercise-audio --audio-probe tests/AudioProbe/bin/Debug/net8.0-windows/AudioProbe.dll
```

This briefly changes master volume and mute. The independent utility bypasses the agent for external-change tests, then restores exact original scalar volume/mute in `finally`. Avoid other volume adjustments during the test. Without `--audio-probe`, the optional test restores the original rounded percentage.

The tray runner requires an interactive Windows desktop and writes `artifacts/tray-pairing-preview.png` using dummy credentials. It briefly creates and removes a test notification icon without changing the installed configuration or clipboard.

## Tray acceptance

- After upgrading the companion, find its blue music-note icon (including the notification area's overflow menu). Right-click to inspect local running/audio status.
- Open Pairing information from the menu and by double-clicking. Check the PC address, port and 10-digit code against SmartThings settings.
- Confirm the large code is numeric and New code replaces it. Close the window; confirm SmartThings control continues while it is closed.
- Choose Exit ST Windows Media Control. Confirm the icon disappears and the companion stops; restart its scheduled task and confirm control recovers with unchanged pairing.
- Check the window at the desktop's normal DPI scaling and after moving it between monitors.

## Gate 1: real volume milestone

Install the agent and driver per their READMEs. Keep logs visible. Do not move to live media acceptance until these pass:

1. Scan nearby twice. Exactly one ST Windows Media Control device exists. Configure its IP, port and 10-digit code; its initial slider reflects Windows.
2. Set the SmartThings slider to safe values such as 12, 25 and 40. Windows' default output master volume matches each value, without launching a player or changing mute.
3. Use Windows Settings, a keyboard media key and (if available) a headset wheel/USB DAC knob. The SmartThings slider follows each change without pressing Refresh. Target under 2 seconds while connected; record measurements rather than assuming this SLA.
4. Move the slider quickly and then change Windows volume. The final values converge; old responses do not jump the slider backwards.
5. Temporarily block LAN traffic, change Windows volume, then restore traffic. The device recovers to the current value within 60 seconds. A pending command must not replay after reconnect.
6. Restart the agent, then separately restart the Edge driver/hub. Pairing survives and values synchronize without creating another device.

## Gate 2: mute and endpoint recovery

- Mute/unmute in SmartThings and Windows. Each side follows the actual state, including false/unmuted.
- Keep muted while changing volume. Volume control must not unmute.
- Switch default output, unplug/replug a USB DAC, and temporarily disable every output. No stale callback crash; unavailable output marks offline without an invented zero. Recovery should occur via notification or within the 20-second observer fallback.

## Gate 3: media and presentation

- With no media session: volume/mute still work, playback is stopped, metadata empty, and media commands are rejected gracefully.
- In at least two unrelated GSMTC-compatible apps: play, pause, next, previous and externally initiated playback changes work where supported.
- Test an app/session that cannot skip. It should return 409 without media-key fallback or another application being controlled.
- Open two sessions, switch Windows' current session, then close it. Verify the selected session and its source/metadata change without stale metadata persisting.
- Check missing/non-ASCII/long metadata. It must not break audio control or JSON decoding.
- Check Android/iOS standard controls, including unsupported buttons and volume range. Capture the actual presentation; do not assume an exact button arrangement.

## Gate 4: install and security

- Sign out/in: the named user task starts without a console. Confirm it runs as the intended user at limited privilege.
- Reboot/sign in: token and UUID are unchanged; reconnection works.
- An unauthenticated allowed-source request gets 401; another LAN host gets 403 even with a token, unless it is the configured hub. Wrong token cannot read metadata or issue commands.
- Oversized/malformed JSON is rejected. Power/shell/unrecognized commands are rejected.
- Public-profile network access is blocked by the supplied firewall rule. There are no broader allow rules for the same program.
- Update and uninstall only affect this user's named task and explicitly scoped firewall rule; configuration survives updates.

Report OS, SDK/hub firmware/app versions, steps, timing and sanitized logs when filing an issue. Never attach `agent.json`, pairing preferences or authorization headers.

Short-code follow-up (2026-09-22): 156 state/configuration/pairing checks passed,
including code expiry, regeneration, attempt throttling and unchanged internal
credential security. Tray tests cover the three visible fields, hidden internal
credentials, active displayed code and New code renewal. Lua tests cover exchange,
persistence, restarts, stale responses, invalid responses and address/code binding.
Published-agent HTTP smoke checks reject unauthorized/expired code exchange,
rate-limit attempts, and prevent using a short code as a control bearer token.
The updated pairing dialog was rendered and visually checked with dummy data.

Album artwork removal: 139 state/configuration/pairing checks, Lua behavior checks and published HTTP smoke checks pass. The former image route returns 404 with valid authentication and has no unauthenticated exemption. Thumbnail extraction and local-subnet firewall access were removed.
