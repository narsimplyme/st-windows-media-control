# Fixed app volume components

Both monitor and speaker profiles contain main plus app1–app5. Main is unchanged.
Each app component has only `oceangarden54575.appName` v1, audioVolume v1 and
audioMute v1. Headers remain App 1–5; the custom string attribute `appName`
has no commands and a detail-view `state` presentation (`{{appName.value}}`).
There are no media capabilities on app components. Layout is generated through
the public profile/presentation APIs, not a mobile UI workaround.

Definition: `edge-driver/capabilities/appName.json`.
Presentation: `edge-driver/capabilities/appName.presentation.json`.
The capability and presentation are already registered for this project's namespace.

## Update commands (PowerShell)

For a new fork only, create the capability in your own namespace and replace the
returned capability ID in both parent profiles and `src/app_components.lua`:

```powershell
smartthings capabilities:namespaces
smartthings capabilities:create --namespace YOUR_NAMESPACE --input .\edge-driver\capabilities\appName.json
```

For this repository, upload the presentation if changed (do not create duplicates):

```powershell
smartthings capabilities:presentation:create oceangarden54575.appName --capability-version 1 --input .\edge-driver\capabilities\appName.presentation.json
smartthings edge:drivers:package .\edge-driver --channel 312aff0b-0bd7-40a8-9f4a-9bec56f56c1f --hub e08ef4f8-d11e-481c-8699-85ad307bfdb4
smartthings devices:commands 064ec46d-7ad5-44fc-b5b0-114685c5fc6a 'refresh:refresh()'
```

Packaging registers the embedded profiles and installs this private test-channel
version. Do not combine `--install` with `--hub` in this CLI version. Reopen the
SmartThings device after updating. No delete/re-pair of the main device is required.
Publish/build the Companion separately, preserving its private configuration:

```powershell
dotnet publish .\windows-agent -p:PublishProfile=Standalone -o .\artifacts\standalone
```

Update the Companion and Edge together. Old selected apps are assigned once in
saved order. Existing app child devices are retired after the new snapshot arrives;
old child Routine references need reconfiguration. The retired child profile is
kept only for migration and no code creates children.

## Identity and synchronization

See [protocol](protocol.md#selected-app-volume-channels) for slot persistence,
stale-command checks, stable AUMID/path identity, multi-session grouping and the
last-volume-event-wins display policy. Companion discovery history remains
scrollable; only opening the window or Refresh apps redraws the list. Checking
updates the assigned slot immediately without reordering the list.

## Verification

Automated tests cover maximum five, first free slot, no compaction, persistence,
legacy migration, stale key/slot rejection, empty/inactive slots, component-only
routing and main command preservation. AppAudioTests uses two real Windows audio
sessions for grouped volume/mute and callback/exit/restart tests; it skips only on
machines without an audio output. Phone rendering must additionally be checked in
the SmartThings mobile app; API status alone does not verify its layout.

Official references:
- [Capability presentations](https://developer.smartthings.com/docs/devices/capabilities/capability-presentations)
- [Embedded profile configuration](https://developer.smartthings.com/docs/devices/configurations-and-presentations/embedded-device-configurations)
- [Component events](https://developer.smartthings.com/docs/edge-device-drivers/device.html)

Validated locally on 2026-09-27: 168 state/pairing/catalog assertions; WinForms
render/manual-refresh tests; Lua protocol/driver/component/TLS tests; HTTPS and
single-EXE smoke tests. Real grouped audio callbacks arrived in approximately
50–112 ms in this run. The test hub reported all five app names/empty slots.
LOST ARK app1 accepted volume 25%, Windows-side 31% propagated back, and mute
worked in both directions. Original 37%/unmuted was restored. The user also
confirmed app1 volume/mute in the phone UI. Spotify-specific testing was not run;
exit/restart and multi-session grouping used dedicated silent test processes.

## Android named-slider experiment

On 2026-09-27, a separate appVolume capability combined appName with a volume
slider whose presentation label was `{{appName.value}}`. The API accepted the
presentation, but the user's Android app displayed a generic App Volume card
and a separate native mute card. It did not produce the requested named native
volume card. The experiment was reverted; both profiles again use audioVolume,
audioMute and the separate read-only appName card. No Companion or protocol
changes were needed. Do not treat API presentation acceptance as mobile UI validation.
