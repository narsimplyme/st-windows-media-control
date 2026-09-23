# SmartThings Edge driver

One manually paired LAN device uses one `main` component with `audioVolume`, `audioMute`, `mediaPlayback`, `mediaTrackControl`, `audioTrackData`, and `refresh`. There is no switch/power capability or separate manager device. Standard presentation is generated from the profile; no custom capability namespace or presentation publication is required.

## Install on your hub

Install and authenticate the [official SmartThings CLI](https://github.com/SmartThingsCommunity/smartthings-cli). From the repository root:

```powershell
smartthings edge:channels:create
smartthings edge:channels:enroll
smartthings edge:drivers:package .\edge-driver --install
```

Follow the CLI prompts to select your channel and hub. Alternatively, supply their IDs explicitly:

```powershell
smartthings edge:drivers:package .\edge-driver --channel <channel-id> --hub <hub-id>
```

Use either `--install` for interactive hub selection or `--hub <hub-id>` for an explicit hub, not both. The installed CLI treats these as conflicting options and may report an internal `isAxiosError` TypeError instead of a useful argument error. `--hub` already requests installation.

See the [official channel workflow](https://developer.smartthings.com/docs/devices/hub-connected/driver-channels) for account/channel enrollment details. This repository does not include a prepublished driver or invitation URL. Hub installation and account-side profile validation have not been run in this workspace.

## Pair

1. Build/start the Windows companion and add its hub-restricted firewall rule.
2. In the SmartThings app choose **Add device → Scan nearby**. The driver's discovery handler creates **ST Windows Media Control**. This is a manual pairing slot, not an automatic LAN scan.
3. Open the device's three-dot menu → **Settings** and enter the PC IPv4 address and the **10-digit pairing code** shown in the PC tray → Pairing information. Leave the port at 8765 unless changed.
4. Rename the device, for example **ST Windows Media Control — Gaming PC**.
5. Wait for current volume/mute state, then test the slider and an external Windows volume change.

The user-facing code contains exactly **10 decimal digits** and expires after ten minutes.
The driver exchanges it for internal credentials and saves those in persistent device fields;
once connected, code expiry and ordinary restarts do not require re-pairing. The zero address
and zero code defaults mean unpaired. Opening the PC pairing window or clicking **New code**
creates a code; changing the code preference initiates a new exchange. Existing legacy
preferences are migrated privately when still available during an upgrade.

The first real backend attempts rejected missing defaults and an over-limit token preference. The fixed profile retains all six standard capabilities, SmartMonitor category, `main` component, and the `mediaPlayback.config.values`/`{{enumCommands}}` structure used by the [official Sonos profile](https://github.com/SmartThingsCommunity/SmartThingsEdgeDrivers/blob/main/drivers/SmartThings/sonos/profiles/sonos-player.yml). The CLI [packager](https://github.com/SmartThingsCommunity/smartthings-cli/blob/main/src/lib/command/util/edge-driver-package.ts) parses YAML and includes profile files; backend constraints still require a live upload to confirm. Local YAML checks alone do not certify API acceptance.

The pairing slot has a deterministic network ID. Repeated discovery does not create extra devices; restarting the hub restores saved preferences and starts synchronization. V1 deliberately supports **one PC per driver installation**. The companion UUID verifies the selected PC, independently of its IP. Discovery can later create per-UUID devices without changing the HTTP protocol.

Manual refresh restarts state synchronization. Changes to settings invalidate old responses and reconnect. Temporary failures retry with 1–30-second exponential backoff. An idle connection returns a full state snapshot every 20 seconds; native changes normally return sooner.

## Controls and presentation

Volume steps use the current Windows volume, not a potentially stale SmartThings value. Play/pause are explicit commands; the HTTP API also supports toggle, but no custom toggle button is added. `supportedPlaybackCommands` consistently advertises `play` and `pause`; Windows checks availability when a command executes. Some standard Speaker cards still display a square and send `stop` despite the profile's play/pause restriction. The driver handles that command as **pause**, preserving the playback position. Repeated stop/pause requests never toggle music back on. Stop is not advertised as a separate action. Previous/next are attempted only when supported by the selected Windows session. The standard track-control UI may still show unavailable buttons.

After updating the driver, reopen the device screen and test the center button while music is playing. It should pause, then Play should resume. If nothing happens, capture logcat: `playback stop -> pause` or `playback pause -> pause` confirms the command arrived; a following HTTP 409 means Windows/the app rejected it. The standard card's icon itself is controlled by SmartThings and has not been verified to change to a pause symbol.

`audioTrackData` carries title, artist, and the Windows app display name (app model ID when name resolution is unavailable). Empty fields are cleared when sessions disappear. Optional `album` and `albumArtUrl` fields carry artwork from the companion; see [setup](../docs/album-art.md). The phone, not the hub, must be able to fetch the URL. Windows statuses other than Playing/Paused map to SmartThings `stopped`; lack of a session also maps to stopped. An unavailable output endpoint makes the device offline and preserves its last known volume instead of inventing a zero reading.

Standard mobile-client layout and metadata visibility require device testing. You cannot assume the exact proposed Now Playing/button arrangement on every SmartThings app version.

## Debugging

```powershell
smartthings edge:drivers:logcat <driver-id> --hub-address <hub-ip>
```

Check `--help` if your CLI version prompts for the hub differently. Logs distinguish network errors, HTTP authentication failures, identity mismatch and rejected commands. Never attach device-preference dumps or bearer tokens to an issue. Driver logs suppress repeated identical reconnect errors.

No `fingerprints.yml` is needed: devices are created by the LAN discovery callback, without Zigbee/Z-Wave fingerprints.

### Select a device icon

In SmartThings, open the device → Settings → **Device icon / 기기 아이콘**.
Choose **Monitor / 모니터** or **Speaker / 스피커**, save, and reopen the device.
The driver switches between profiles with identical media controls and pairing
preferences. This selects a built-in category icon; it does not upload a custom
image. App caching or an existing app-level icon override can delay/hide a change.

Profile switching uses the official Edge `try_update_metadata({profile=...})` API:
https://developer.smartthings.com/docs/edge-device-drivers/device.html

The pairing-code preference uses integer input to request the app’s numeric keyboard. Codes have no leading zero. The driver normalizes integer values to ten-digit strings and accepts existing string preferences during upgrade; zero is the unpaired default.
