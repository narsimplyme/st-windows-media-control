# HTTPS transport experiment

Current implementation: the PC-specific test certificate module has been removed.
Standalone setup now creates configuration/certificate, and the generic driver
discovers the public PEM, displays a locally computed verification value and waits
for user approval. See [protocol.md](protocol.md#security). The sections below
record the earlier transport experiment and its results.

The phone uses SmartThings; the Edge driver on the hub is the LAN client.
The hub should retain the PC's trusted certificate, analogous to SSH known_hosts.
The PC continues to authenticate the paired client separately. An IP address is
an allowlist constraint, not cryptographic identity.

The official Edge SSL API documents `verify = "peer"` and a PEM string in
`cafile`: https://developer.smartthings.com/docs/edge-device-drivers/ssl.html
This permits a dedicated per-PC trust anchor without a public CA or shared
private key embedded in the driver. Exact runtime behavior still needs testing
on the real hub. Do not assume all LuaSec certificate inspection APIs exist.

## Verified locally

Run `dotnet run --project tests/TlsProbe/TlsProbe.csproj` on Windows.
This temporary loopback-only Kestrel server uses an ephemeral test identity.
Explicit trust succeeds, default trust rejects the certificate, and plain HTTP
cannot reach the endpoint. No production configuration or credentials are used.
The Windows TLS provider required importing the generated PFX before serving it.

## Next integration checks

`tests/edge_tls_probe.lua` is a hub-side handshake experiment, not a deployed
driver change. Test it against a dedicated PC TLS listener using an independently
verified PEM; repeat with a different certificate and require handshake failure.
The local .NET test does not establish that the Edge implementation works.

Before switching the production endpoints, implement protected persistent PC
certificate storage and authenticated initial enrollment. Trust-on-first-use
alone leaves the first connection vulnerable to interception. Merely receiving
the certificate alongside the pairing response does not authenticate it.
Consider a short authentication string shown on both the hub's SmartThings
device and PC for explicit comparison, using a reviewed binding protocol.
Do not send a pairing secret until server identity has been authenticated.

Persist trust only after successful confirmation. Refuse changed certificates
until explicit re-pairing; do not silently accept replacement or fall back to
HTTP. Keep TLS 1.2 or later, existing request limits, source filtering, and bearer
authorization. Test persistence across restarts and identity reset/revocation.

## Live validation on 2026-09-23

The actual SmartThings hub successfully fetched the HTTPS probe with the trusted
PC certificate. A different CA produced `certificate verify failed`. This was
repeated successfully. The development companion and existing device driver
were then upgraded using a separate private test channel. The public enrollment
channel was not assigned this experimental version.

The installed companion now has `tlsEnabled: true` and serves HTTPS only on its
existing port. `--enable-https` provisions `server-tls.pfx` with a protected ACL
(current user and SYSTEM) and exports only its public certificate to
`server-tls.pem`. Provisioning reuses the existing identity; a missing established
identity is an error, not silent regeneration. The certificate expires after two
years; renewal and distribution still need a user-facing workflow.

For this controlled test, the public PEM was read locally and inserted into
`tls_trust.lua` in an ignored build directory before uploading to the private
channel. The certificate did not come from an unauthenticated network connection.
The source `tls_trust.lua` intentionally has no certificate and fails closed.
This is PC-specific test provisioning, **not general release enrollment**. Do not
publish this build as a generic driver or overwrite the public channel with it.

Existing pairing credentials remained valid. The user confirmed playback,
track change and volume controls worked; hub logs also confirmed command handling,
state updates, mute/unmute and volume restoration over HTTPS. The installed
Windows build is `artifacts/https-win-x64`; the staged driver is
`artifacts/edge-https`. Neither contains a private key in the driver package.

Local regression results: 148 state/configuration/pairing/TLS checks, existing
Edge Lua checks, and actual companion HTTPS endpoint smoke tests passed. Tests
cover unknown-CA rejection, no plaintext fallback, identity reuse, protected key
ACL, missing-key failure and preservation of TLS during pairing reset.

The generic authenticated enrollment flow and automated certificate renewal
remain unfinished. Legacy configurations still default to HTTP until explicitly
provisioned. No GitHub Release was published.

Cleanup completed: the temporary probe server was stopped, its port 8766 firewall
rule was removed, and the separate diagnostic driver was uninstalled from the
hub. The actual HTTPS media driver and companion remain installed and running.

## Standalone onboarding follow-up

- A compressed self-contained Windows x64 EXE installs to the per-user folder.
- Setup preselects a detected PC address, offers optional sign-in startup, creates
  protected configuration and TLS key files, and requests firewall elevation.
- The hub address is learned only after successful short-code exchange; initial
  enrollment accepts local-subnet peers only while a pairing window is active.
- Certificate discovery sends no pairing code or bearer token. Both screens show
  the same 128-bit SHA-256 certificate fingerprint; the certificate approval preference explicitly approves it. Playback commands cannot approve trust.
- Saved trust survives restarts. Certificate verification failure never initiates
  automatic re-enrollment; a settings toggle explicitly requests comparison again.
- The real hub initially rejected the SHA library's dynamic `load`. Its unchanged
  INT64 implementation is now statically compiled and checked against SHA vectors
  with dynamic loading disabled; the real hub successfully displays the digest.
- Separate package lock files cover the ordinary and standalone build graphs.
- Actions are pinned to verified Node.js 24 releases; the deprecation override
  is not used to hide warnings.

The first-run and pairing dialogs are rendered and exercised in isolated tests;
the published EXE is also tested after copying it alone to an empty directory.
No GitHub Release has been published. Certificate renewal remains an explicit
maintenance operation rather than an automatic trust replacement.
