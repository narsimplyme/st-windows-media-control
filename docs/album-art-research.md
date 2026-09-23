# SmartThings music-card artwork investigation

Checked 2026-09-22. No confirmed Android-card fix yet.

User-reported test environment: SmartThings 1.8.47.24, One UI 9.0, Android 17. Public searches for this exact version did not identify a verified music-artwork fix. Android renderer/source or mobile diagnostic logs have not been inspected; the app version alone cannot establish its internal behavior.

At the user's request the profile category was changed from Speaker to the documented SmartMonitor category for its default icon. The six capabilities and command handlers are unchanged. This is not an album-art fix; actual icon shape/card layout is mobile-client controlled. No verified combined PC-and-monitor icon identifier was found.

## Verified locally

- The Windows companion extracts current artwork and serves a PNG successfully.
- The real Edge device reports the current `audioTrackData.value.albumArtUrl`.
- The user's phone browser renders that LAN image.
- Opening the Android device card, changing tracks, and reopening the app produced no additional completed artwork requests in companion logs.
- A public HTTPS PNG was substituted and confirmed in device status. The Android card still showed its blue music-note background. This does not prove that all HTTPS URLs or all SmartThings cards behave identically.
- The unsuccessful diagnostic preference was removed and the restored driver installed.

## New evidence and limits

1. A [July/August 2025 first-hand report](https://community.smartthings.com/t/text-to-speech-notifications-in-sonos-integration-stopped-working-august-2025/304120) explicitly describes album art working inside SmartThings with Sonos on iOS 26 beta. This is evidence of one working platform/integration combination, not Android support or a reproducible configuration for this device.
2. The [official Sonos profile](https://github.com/SmartThingsCommunity/SmartThingsEdgeDrivers/blob/main/drivers/SmartThings/sonos/profiles/sonos-player.yml) uses Speaker and audioTrackData, with no explicit artwork switch, vid, or dpInfo in that profile. Its [event handler](https://github.com/SmartThingsCommunity/SmartThingsEdgeDrivers/blob/main/drivers/SmartThings/sonos/src/api/event_handlers.lua) uses the same albumArtUrl property as this driver. Other Sonos capabilities include groups, presets, audio notifications and software generation; there is no verified reason to add unsupported controls merely to mimic the profile.
3. The platform can select special device-detail plugins through dpInfo/dpUri. [SmartThings developer support gives an explicit camera example](https://community.smartthings.com/t/struggling-with-getting-devices-live/294609/11), and the [official SDK describes the mechanism](https://github.com/SmartThingsCommunity/smartthings-core-sdk/blob/main/src/endpoint/presentation.ts). No verified music-specific plugin URI or album-art configuration was found. Camera values are not an appropriate music-card fix.
4. A capability presentation declaring `state` is NOT sufficient evidence that its standard native card cannot render images. Standard capabilities can receive special UI treatment. The earlier inference from `state` alone was too strong.

## Next discriminating evidence

- Inspect the actual Android music-card renderer or obtain a working Android Sonos device presentation plus app version and screenshot. Compare platform-specific plugin/metadata and required fields.
- If renderer access is available, determine whether it reads albumArtUrl, what conditions enable it, and whether image fetches are rejected before reaching the network.
- Do not repeat LAN/browser tests, add guessed capability fields, or claim that a public HTTPS test alone establishes a global lack of support.

No support inquiry has been sent and no phone application files have been accessed. The current implementation exposes artwork data; rendering in the user's SmartThings card remains unresolved.

## Separate image box above the music card

The [documented detail-view display types](https://developer.smartthings.com/docs/devices/capabilities/display-types) include controls and textual state, but no generic image or HTML/WebView element. The [official capability SDK interface](https://github.com/SmartThingsCommunity/smartthings-core-sdk/blob/main/src/endpoint/capabilities.ts) likewise exposes no image-widget definition. Its displayType is a string, so this is a statement about documented/supported functionality, not proof that every undocumented renderer is impossible.

[Device configuration](https://developer.smartthings.com/docs/devices/configurations-and-presentations/device-configurations) influences ordering but the app groups main, status and command capabilities itself; arbitrary placement above its native music card is not guaranteed. Adding a custom capability by itself cannot supply an image renderer. imageCapture's URL attribute is not evidence of a general-purpose image box; its specialized camera UI is not a verified drop-in music solution.

A separately implemented companion web page could display current artwork above media controls, but that would be a separate screen, not an embedded box in SmartThings. No such replacement UI has been built or substituted for the requested native card.
