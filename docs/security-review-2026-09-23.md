# Release security review — 2026-09-23

Scope: all tracked Windows/Edge source, installation/startup/firewall scripts,
profiles, tests, CI and documentation at baseline commit `0701f04`, plus fixes
in this review. No release was created. This is a source review and targeted
local testing, not a certification or a full penetration test of SmartThings,
Windows, the native audio libraries, or the mobile app.

## Findings and disposition

| Severity / scope | Finding | Disposition |
| --- | --- | --- |
| High under LAN interception | Plain HTTP carries the reusable bearer token, pairing code and exchange response. A network observer/active intermediary can obtain credentials; IP allowlisting does not provide transport authenticity or confidentiality. | Still present. Broad/untrusted-network deployment should wait for authenticated encryption or TLS with verified peer identity. Current scope is trusted private LAN only. |
| Medium, local shared-directory exposure | `--init` created token files with inherited permissions; installer wrote secrets before applying the file ACL. Reproduced `AreAccessRulesProtected=false` on a fresh temporary config. | Fixed: create files with a private DACL from the first byte, restrict installation directory before writes, and enforce private ACLs on rotation/replacement. |
| Medium, admin helper misuse | Firewall rule identifiers from a user-writable config reached wildcard-aware `Get-NetFirewallRule -Name`; `*` could select all project rules. Invalid ports/addresses also lacked consistent validation. | Fixed: nonzero UUID, integer port bounds and unicast IPv4 checks before firewall lookup or mutation. Five negative tests use mocked firewall cmdlets. |
| Development dependency | Lupa 2.6 matches CVE-2026-34444 / GHSA-69v7-xpr6-6gjm (PYSEC-2026-2613 is the same issue). Advisory concerns Python attribute filtering used as a sandbox. This repository executes its own test Lua and does not claim sandboxing, so this is not a Windows-agent remote-code-execution finding. | Updated to 2.8; disabled Lua access to Python builtins/eval. No reported advisories for 2.8 in the OSV re-query. Lupa is not shipped with the Windows app. |
| Hardening, not demonstrated exploit | CI used mutable action tags and default token permissions; WinForms output enabled BinaryFormatter although application code does not use it. | Pinned official action commit IDs, read-only contents permission, disabled persisted checkout credentials, and disabled unsafe BinaryFormatter serialization. NuGet advisory warnings now fail restore. |

The suspected malformed Edge response crash did **not** reproduce: existing
validation rejects missing/wrong-type fields without throwing. Added a regression
test; this is not counted as a vulnerability. No remote shell, arbitrary process
execution, arbitrary file-serving route, or unauthenticated media-control path
was found in the reviewed application code.

## Evidence

- 141 state/configuration/pairing assertions passed, including protected token
  ACLs, random credentials, code expiry, attempts per minute and rotation.
- 17 tray/startup checks passed using an isolated registry key, including quoted
  executable/config paths and idempotent enable/disable. Real sign-in/reboot and
  all failure/rollback combinations were not exercised.
- Lua behavior/protocol tests passed with Lupa 2.8; invalid snapshots, stale pairing
  responses, credential/address binding and media command mapping were checked.
- Published self-contained audit build passed HTTP authentication, command
  allowlist, input bounds, oversized-body rejection, pairing throttling and removal
  of the old image route. No playback commands were sent by this smoke run.
- NuGet package audit including transitive packages reported no known affected
  package. OSV reported none for Lupa 2.8 and PyYAML 6.0.2 at review time.
- Included .NET/ASP.NET/Windows Desktop runtime is 8.0.31, matching Microsoft's
  September 2026 patch release. Rebuild before release if a newer patch appears.
- 75 historical Git blobs checked for the current installed token/device ID,
  GitHub credential patterns and private-key headers: no matches. This is a
  targeted scan, not proof that every possible secret format is absent.
- Actual firewall changes and a fresh installer execution under an elevated
  account were not performed as part of this review; script syntax and mocked
  validation tests were used. Existing machine ACL/rule state is not certified.

## Remaining release work

Transport protection is the primary unresolved security issue. Keep release
publication paused pending a decision on the trusted-LAN threat model or a
protocol change. The currently installed app was not replaced with this audit
build. A standalone first-run installer flow and signed release artifacts also
remain separate distribution work; successful tests do not imply a signed or
fully tested installer. Do not describe this audit as “no vulnerabilities.”

## Sources

- [Lupa maintainer advisory](https://github.com/scoder/lupa/security/advisories/GHSA-69v7-xpr6-6gjm)
- [Microsoft .NET 8.0.31 release notes](https://github.com/dotnet/core/blob/main/release-notes/8.0/8.0.31/8.0.31.md)
- [File creation with an explicit ACL](https://learn.microsoft.com/en-us/dotnet/api/system.io.filesystemaclextensions.create)
