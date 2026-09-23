# ST Windows Media Control LAN protocol v1

HTTP/1.1 JSON over TLS 1.2 or newer. Default configurable port: **8765**. UTF-8 JSON, camelCase property names. All control/state endpoints require `Authorization: Bearer <32-hex-character-token>`. Token comparison is case-sensitive. The `POST /v1/pair` code exchange is described below. Every response disables caching. No redirects, CORS, browser UI, or cloud callbacks.

The token encodes 16 cryptographically random bytes (128 bits); the all-zero value is reserved for unpaired profile defaults and cannot authenticate a configured agent. The all-zero UUID is also invalid. Legacy 64-character tokens must be rotated using the rebuilt companion's `--rotate-token` option or migrated by the updated installer, then copied to SmartThings. There is no UI truncation or legacy-length fallback. The HTTP paths and snapshot format are unchanged.

## Endpoints

| Method/path | Purpose |
| --- | --- |
| `GET /v1/identity` | Public certificate only; no secret or control data |
| `GET /v1/state` | Current complete snapshot, immediately |
| `GET /v1/events?epoch=<epoch>&after=<revision>` | Return immediately if cursor differs, otherwise hold up to 20 seconds for a native state change |
| `POST /v1/command` | Execute one allowlisted command; return acknowledgement, not speculative state |

Snapshot shape (illustrative placeholder identities):

```json
{
  "deviceId": "12345678-1234-1234-1234-123456789abc",
  "epoch": "0123456789abcdef0123456789abcdef",
  "revision": 42,
  "audio": {"available": true, "volume": 67, "muted": false},
  "media": {
    "available": true,
    "playback": "playing",
    "title": "Track title",
    "artist": "Artist",
    "source": "Spotify",
    "canPlay": true,
    "canPause": true,
    "canNext": false,
    "canPrevious": false,
    "canToggle": true
  }
}
```

`deviceId` is stable across reboots; `epoch` is 32 hex characters freshly generated on process start. `revision` starts at 1 and increases for changed state only. A snapshot includes both audio and media, never a delta. Availability false means associated defaults are unavailable, not actual volume zero. Metadata is an empty string when absent. `source` is the Windows app display name when resolvable, otherwise its original app model ID; it is a display label, not a stable session identifier. Playback is playing, paused or stopped; no session is stopped/available=false.

`after` is an integer revision. Missing cursor, mismatched epoch, old revision, or future revision returns the current full snapshot immediately. Equal cursor waits for a signal. Timeout returns HTTP 200 with a full snapshot, possibly unchanged. Disconnected requests cancel their wait. Notifications between checking the revision and waiting cannot be lost because both happen under one lock.

Examples of command bodies:

```json
{"command":"setVolume","value":67}
{"command":"adjustVolume","value":-5}
{"command":"setMute","value":true}
{"command":"play"}
{"command":"pause"}
{"command":"toggle"}
{"command":"next"}
{"command":"previous"}
```

Each line is a separate request. Volume is an integer 0–100. Delta is an integer -100–100 and is applied to current native volume, clamped to range. Mute must be a JSON boolean. Audio values are read back after writes. Media calls check the current session and its supported controls. No command launches a player.

Responses:

- `200 {"accepted":true}`: native audio operation completed or media request accepted. Watch state to learn the observed result.
- `400 {"error":"..."}`: malformed JSON/command/value.
- `401`: absent or incorrect token, including read-only endpoints.
- `403`: remote address is neither the configured hub nor loopback.
- `404`/`405`: unknown path/method.
- `409 {"error":"unavailable_or_unsupported"}`: endpoint/session unavailable, control unsupported/refused or operation timed out.
- `413`: request exceeds the 1 KB server body limit. Other malformed HTTP is rejected by Kestrel.

## Ordering, recovery and retries

The Edge driver holds at most one active event request per generation. Commands run independently in ordered device callbacks. It applies only valid snapshots for the configured UUID and a newer revision (or new epoch). Command acknowledgements never update attributes, so delayed command replies cannot overwrite newer notifications.

On reconnect, the old cursor returns an immediate snapshot if anything changed. A hub restart begins with `/v1/state`. A companion restart changes epoch. Reconciliation uses the same full snapshot structure, with no replay backlog or subscription database. Intermediate wheel ticks may be coalesced; the goal is current-state consistency, not an audit log.

No command is retried automatically. A timed-out next/previous/toggle may have executed; retry could advance twice. Absolute volume/mute are idempotent but are likewise left for a deliberate new user action. There is no command ID or deduplication cache in v1.

The driver limits response bodies to 16 KB, validates identity/schema, does not follow redirects, and uses a 25-second socket timeout. Failed reads retry with exponential backoff from 1 to 30 seconds. New pairing settings invalidate in-flight responses from old workers.

## Security


The generated token has 128 bits of randomness. First-run setup generates a per-PC RSA certificate and selects an explicit IPv4 interface. Its key and configuration files are protected by user/SYSTEM ACLs. Before a hub is paired, only same-subnet certificate discovery and pairing requests are accepted during an active pairing window. Successful code exchange saves the source hub address; later traffic is restricted to that hub or loopback. Kestrel limits concurrent connections to 16 and request bodies to 1 KB. Every state/control endpoint authenticates before reading state or invoking an action.

Only public-certificate discovery uses unverified TLS: a fixed GET path, no Authorization header, no body and no redirects. It yields an untrusted candidate. The driver computes SHA-256 over the whitespace-free base64 certificate body, displays the first 128 bits as four uppercase eight-hex-character groups, and waits for an explicit change of the certificate approval preference after comparison with the PC. All code exchanges, state and control traffic then use `verify=peer` with that certificate as the sole trust anchor. The candidate is persisted only after explicit user comparison approval, independently of whether the short-lived pairing code succeeds. Updating an expired code reuses that approved certificate; an enabled approval switch alone never approves a new candidate. A previously saved certificate is never automatically replaced. This is human-verified initial trust; skipping the comparison defeats protection against an active first-pair intermediary. No public CA or shared private key is embedded. Source-IP filtering alone is not authentication. A compromised authorized hub can still use its credentials.

The certificate lasts two years. Expiry or key loss fails closed; explicit certificate replacement and re-verification are required. Automatic renewal is not yet implemented. Legacy headless developer configurations may still use HTTP; normal GUI startup migrates them to TLS and the generic Edge client never falls back to HTTP.

The installed configuration file is restricted to the installing Windows user and LocalSystem. The Edge driver stores the internal device ID/token in persistent device fields, not user-editable preferences. This is not a dedicated secret-management API. Neither component deliberately logs tokens. SmartThings SDK diagnostic logging may expose preferences; redact before sharing.

The API exposes only the listed audio/media operations. It cannot accept shell commands, process names, paths, keystrokes, power operations or arbitrary URLs.

## Short-code pairing

The PC tray generates a random 8-digit decimal code (no leading zero), valid
for ten minutes and kept in memory only. `POST /v1/pair` with JSON
`{"code":"1234567890"}` exchanges a valid code for `{deviceId, token}`. The example
is illustrative, not an actual code. This route accepts the configured hub,
loopback, or a local-subnet peer during initial TLS enrollment; it does not require an existing bearer token. All responses
are no-store. Five exchange attempts per minute are allowed globally; exhausted
budgets return 429. Wrong, missing or expired codes return 401. Reissuing a code
does not reset the attempt budget. Response retries within the validity window
are allowed. A host restart invalidates the code.

The Edge driver binds saved internal credentials to the PC address, port and
entered code. Restart/icon changes reuse credentials without exchanging again;
changing the address, port or code prevents reusing old credentials. Stale
responses from superseded workers cannot replace saved credentials. Legacy
UUID/token preferences, when present, are migrated into private persisted fields.
The visible profile contains PC address, port, pairing code, icon choice and an explicit certificate re-verification toggle.

Album artwork serving has been removed. The media snapshot retains the album title but no image URL. The Edge driver clears any previously published image URL on its first metadata update.
