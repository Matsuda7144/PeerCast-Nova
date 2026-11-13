# PCN-0503: Owner Scoped PCP HOST Directory and Referrals

## Scope

Smart Relay Phase B, base main
`1143340e36ac26ddd55553e3e4db439552f4a6ce`.
[ADR-0493](../adr/ADR-0493-owner-scoped-pcp-host-directory-and-referrals.md)
records ownership, wire compatibility, budgets and deferred paths.

## Verification boundary

- Directory registration/update, SID/endpoint merge, Unknown fields, TTL boundary,
  256-record eviction, rate limit, bounded local failure history/cooldown,
  old-owner retirement, direct-peer knowledge overflow and exact exclusion lease.
- Global IPv4/address/port/channel validation, own/requesting/direct peer/known
  upstream-loop exclusions, no invented SID, explicit firewall exclusion.
- Selection with 0/1/8/>8 peers, deterministic priority and unique endpoint cap.
- Standard HOST SID/cid, non-palindromic IPv4 wire bytes, little-endian port/count,
  flags without inversion, omitted Unknown values and no private LAN leakage.
- Coalesced/one-byte-written upstream startup and receiving HOST, malformed and
  oversized HOST rejection without undoing valid CHAN payload, position or ID.
- Real Coordinator learns both upstream phases and introduces exactly those
  candidate HOSTs on its actual downstream capacity-refusal path.
- Real server sends OLEH/HOST/QUIT 1003 with bounded order/count; learns downstream
  HOST via its existing monitor; excludes that direct peer while still sending
  normal CHAN data. Zero candidates retains A-1 behavior.
- Current-Owner loss/admission closure before HELO suppresses HOST; cancellation
  closes the directory and rejects late observations. GUI Owner current/capacity
  observations follow exact old/new registration and reservation state.
- Existing Phase A-1 PCP/QUIT, relay, OBS/YP, Playback/Recording and cleanup
  regressions are included in the final solution tests.

## Local results

- .NET SDK 10.0.301, Windows; Release solution build succeeded with zero warnings
  and zero errors.
- Focused Phase B plus existing HOST/redirect tests: 93 passed. Broader PCP,
  OBS publishing and YP network regression: 769 passed.
- Full Release solution tests: 8,188 passed, 15 existing platform/native
  acceptance skips, zero failures (8,203 total). Per project: Network 1,150;
  Playback 3,104 passed/15 skipped; App 3,031; Core 459; Protocol 66; CLI 374;
  Foundation 4.
- Solution-wide `dotnet format --verify-no-changes --no-restore` succeeded.
  A later test-only change waits for actual server completion before checking
  active transport counts; its Release test class passed all 31 cases.
  Format verification for that changed test file also succeeded.
- GitHub Actions evidence for the committed PR head is recorded in the PR;
  these local results do not substitute for that exact-head CI check.

The initial CI head `67be9b8b356d14a6217c1946573bf22d6e768ba2` built successfully
and passed the Network tests, but the existing cross-Owner capacity test read
two active transports where it expected one. Receiving a refusal QUIT does not
await RunSessionAsync's finally block that updates completed/active counts.
The test now waits for those expected terminal completions before asserting
the same capacity and active-count invariants. This is a test ordering fix,
not a claim that any prior runtime intermittent failure was resolved.

## Limitations

All new connections are managed loopback/ephemeral fixtures. Global addresses
are synthetic candidate data (or the existing fake connector's logical targets),
not hosts probed for referral. No actual PeerCastStation binary, GUI/player,
OBS stream, router or public listener is operated. No physical compatibility
or WAN readiness is claimed.

Known pre-existing intermittent tests are not claimed fixed. Main's successful
merge CI is used as the base evidence; earlier successful A-1 validation is not
repeated as a separate audit. This PR does not enable Phase C/W/D/E/RT behavior.
