# Optional album artwork

The companion reads the thumbnail supplied by the active Windows GSMTC session and emits it through SmartThings' standard `audioTrackData.albumArtUrl`. There is no player-specific API or external hosting service. Only JPEG/PNG thumbnails up to 1 MB are accepted. Album title is included too.

**Not displaying in the tested SmartThings Android music card:** live testing on 2026-09-22 confirmed Windows thumbnail extraction, Edge delivery of `albumArtUrl`, and phone-browser image access. The music card displayed neither the LAN image nor a verified public HTTPS PNG after the HTTPS URL was confirmed in device status. The companion received no app image requests during LAN tests. Therefore changing HTTP to HTTPS alone is not an established fix. No supported image-rendering setting for this card has been identified; capability data support does not guarantee card rendering. The thumbnail endpoint remains usable by clients that render the URL. Do not port-forward the companion to make it work.

## Update and enable

1. Connect the phone to the same LAN/subnet as the PC. **No phone IP registration is needed.** Artwork is enabled by default; an old `artworkClients` list is ignored.
2. In normal PowerShell from the repository root, install the rebuilt companion using the existing configuration:

```powershell
$configPath = "$env:LOCALAPPDATA\STMediaBridge\agent.json"
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json

.\windows-agent\scripts\Install.ps1 `
  -PublishDirectory .\artifacts\windows-x64 `
  -PcAddress $config.bindAddress -HubAddress $config.hubAddress -Port $config.port
```

3. Reapply the firewall rule in elevated PowerShell, using the same Windows account:

```powershell
.\windows-agent\scripts\Set-Firewall.ps1 `
  -ConfigPath "$env:LOCALAPPDATA\STMediaBridge\agent.json" `
  -AgentExe "$env:LOCALAPPDATA\STMediaBridge\bin\STMediaBridge.Agent.exe"
```

4. Update the Edge driver using the same channel and hub:

```powershell
smartthings edge:drivers:package .\edge-driver --install
```

Keep the existing device and pairing. No new capability or profile is required. Play a track that shows artwork in Windows, then reopen the SmartThings device. If the app does not update immediately, advance a track.

## Access model

Artwork is enabled by default. Set `artworkEnabled` to `false` to disable it. The fixed URL is `/v1/artwork/cover.jpg`; it requires no image key or control token. Any client on the bound interface's IPv4 subnet (or loopback) can read the current cover. Control/state access remains restricted to the hub/loopback plus bearer authentication.

PNG/JPEG thumbnails are decoded and encoded as actual JPEG, with a maximum dimension of 1024 pixels and a 1 MB output limit. The response uses `Content-Type: image/jpeg`, `Cache-Control: no-store` and `nosniff`. The same URL serves replacement artwork and returns 404 when cleared or disabled. Old random URLs return 404. An app that does not reload an unchanged URL may still show stale artwork; fixed naming does not establish SmartThings rendering support. The supplied Private-profile firewall rule includes LocalSubnet for artwork.

HTTP is unencrypted. Someone able to observe LAN traffic may see the thumbnail and its temporary URL. SmartThings also receives the URL as capability metadata. This does not grant control access. Use a trusted LAN and set `artworkEnabled` to `false` if image sharing is unwanted.

## Verification and troubleshooting

- The unsuccessful HTTPS diagnostic option has been removed. Old saved values are ignored; the driver always forwards the real companion artwork URL.

- The companion logs `Artwork response: HTTP ...` when an image request completes, without its URL/key or song details. To test the mobile app, close the browser image tab, reopen the SmartThings device and inspect new log entries. A 200 response establishes delivery, not successful rendering; no entry means no completed image request reached the companion during that test and does not by itself identify the app's reason.

- First confirm normal volume/playback still works.
- Confirm a track actually provides a thumbnail in Windows; some apps provide none or only a logo.
- If needed, inspect `media.albumArtUrl` in an authenticated `/v1/state` response from the hub/loopback. An empty string means disabled, missing, unsupported, oversized or unreadable artwork.
- Open that URL in a browser on a phone on the same LAN. If it works there but not in SmartThings, the remaining issue is mobile presentation/image-fetch behavior. Do not weaken control authentication or open the port publicly.
- Change tracks and close the media app: the URL should change or clear as appropriate.
- To disable, set `artworkEnabled` to `false`, restart the companion and reapply the firewall rule.

Local validation covers the image cache, size/type rejection, JPEG conversion, fixed naming and clearing, Edge artwork-only events/clearing, and HTTP authentication regression checks. Live Windows extraction and fixed-URL JPEG delivery have been verified; SmartThings mobile rendering remains unresolved.

References: [Windows GSMTC Thumbnail](https://learn.microsoft.com/en-us/uwp/api/windows.media.control.globalsystemmediatransportcontrolssessionmediaproperties.thumbnail), [official Sonos albumArtUrl mapping](https://github.com/SmartThingsCommunity/SmartThingsEdgeDrivers/blob/main/drivers/SmartThings/sonos/src/api/event_handlers.lua).
