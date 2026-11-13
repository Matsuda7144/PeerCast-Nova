# PCN-0502: Typed PCP QUIT Compatibility Without Identity Migration

## Scope

Smart Relay Phase 0 / A-1, based on main
`810bc0b6265abb3fe6104fbe3af0e91901d511f2`.
Decision and the compact ID/Owner contract map:
[ADR-0492](../adr/ADR-0492-typed-pcp-quit-compatibility-without-identity-migration.md).

The change replaces only the capacity-response QUIT 0 with standard QUIT 1003,
adds numeric QUIT classification/result propagation, validates its leaf shape,
and prevents explicit rejections from being masked by HTTP 503 or a prior
startup referral refusal. It does not migrate identities or change YP, OBS,
recording, listener, mapping or automatic recovery contracts.

## Managed checks

- Codec: code 0, 1000, 1003, 1005, 1007, unknown codes, little-endian bytes,
  malformed length, parent and wrong-atom rejection.
- Handshake: HTTP 200/503 accepted exchanges; 1003 and legacy OLEH/QUIT 0;
  missing/empty OLEH identity; explicit BadAgent/NotIdentified/other errors;
  HTTP 401/403; malformed QUIT; combined HTTP/PCP writes and one-byte writes.
- Channel session: HTTP/OLEH/OK/CHAN/QUIT written together and in chunks,
  exact retained payload/position/session, typed handshake/receiving QUIT and
  no new post-QUIT reconnect; explicit referral rejection preserves the actual
  error/status and does not try the next candidate.
- Downstream: full fanout emits exactly OLEH/QUIT 1003 with no HOST, normal
  relay still delivers media, remote QUIT is retained, and malformed HELO at
  both full/available capacity is protocol rejection with cleanup.
- Existing stop/cancel, bounded admission/quiescence, OBS/YP, recording and
  Owner-generation regression suites remain applicable and are included in
  the solution-wide test run.

The previous host-candidate regression classified HTTP 503/QUIT 1004 as
RelayFull. Its expected outcome intentionally changes to ProtocolRejected:
only explicit Unavailable or the defined legacy code-0 exchange denotes
capacity. HOST candidates remain parsed and bounded, not discarded.

## Local validation

- .NET SDK 10.0.301; Release solution build: PASS, zero warnings/errors.
  The inherited LIB path pointed to a nonexistent vcpkg directory; LIB was
  cleared only in the child build/test shell, without changing user settings.
- Full solution tests: 8,150 passed, 15 skipped, zero failed (8,165 total).
  This includes 151 tests across the changed PCP/QUIT-related test classes.
- Solution-wide format verification: PASS.
- Repository documentation/ADR/PCN synchronization checks: PASS.

An earlier solution run encountered existing failures in
`PcpDownstreamRelayServerTests.Quiescence_forced_stop_during_reservation_wait_never_succeeds`
and
`PlaybackFmp4PcpLoopbackAcceptanceTests.Caller_cancellation_after_loopback_media_does_not_synthesize_eos`.
Both also failed in a separate, clean checkout of the exact base main above.
The final full run passed both. Their underlying cause/stability is not claimed
resolved here; their source and tests are outside this patch. Retain this
limitation alongside the passing final run rather than treating it as proof
that the existing tests are consistently reliable.

Exact-head GitHub Actions is a separate PR check; no CI success is inferred
from local results.

## Limits

All new network fixtures use managed loopback/ephemeral listeners. Synthetic
global referral endpoints are mapped by the existing fake connector to local
listeners. No actual PeerCastStation binary, real OBS/player, external network,
public listener or router is operated. Passing managed tests/CI does not
establish physical interoperability or WAN readiness. No merge/auto-merge.
