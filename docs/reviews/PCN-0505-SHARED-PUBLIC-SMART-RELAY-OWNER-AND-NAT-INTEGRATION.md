# PCN-0505: Shared public Smart Relay Owner and NAT integration

## Scope

Phase W starts from main `5bcd2321df5c586ed59c43f363f7b21a3188c58d`
(merged Phase C, PR #367).
[ADR-0495](../adr/ADR-0495-shared-public-smart-relay-owner-and-nat-integration.md)
records the shared listener, Owner/Admission and borrowed NAT lifetime contract.

## Managed validation

- Actual TCP public Any/fixed-port bind, two isolated ChannelIds, correct Owner
  OLEH SID, HEAD/DATA delivery, unknown-channel rejection and port conflict.
- WAN OFF/startup/preparation/save are inert; saved opt-in is next-start only.
  Port zero/invalid values and unsupported WAN/IPv6 combinations are refused.
  Classic/Modern settings, JSON backward compatibility, fonts and layout regressions
  use existing tests; the WAN checkbox is included in layout collision checks.
- Same atomic Admission for public and loopback, capacity 503/OLEH/HOST/1003,
  Owner-local HOST learning and direct-child exclusion/release, exact replacement
  generation and old release unable to remove the new route.
- Combined and byte-split HTTP/HELO over the same reader, malformed HTTP and
  ChannelId, missing/empty HELO SID, malformed HOST discard, oversized control,
  direct PCP ping, per-address pending limits, identification and accept-relative
  HTTP deadlines, disconnect/Stop cleanup, subsequent valid connections.
- Existing central manager receives only the bound port. Fake router verifies
  map/renew/unmap, rejection, manual mode, other Owner demand retention and Stop
  during a pending map followed by compensation of late success. Production
  application-network wiring borrows the initialized manager and stops the
  shared host before runtime disposal. Active/cleanup-failed legacy public
  ownership cannot be bypassed by shared WAN mode.
- Real TCP upstream EOF/replacement connection keeps the same GUI Owner,
  public port and mapping demand. Old public child receives QUIT 1003, old HEAD
  is discarded, gap requests are refused, resume uses accepted DATA end, and
  the replacement public child gets the fresh HEAD. Existing Phase C tests
  additionally cover alternative candidate selection, budgets and cleanup failure.
- Stop during a deliberately delayed provider joins startup without a late bind
  or disposed-token race. A held route drain faults once at the bounded deadline;
  repeated releases join and subsequent acquisition stays fail-closed.

The PR and final report record Release/full-suite/Format results and CI at the
final change HEAD. Existing A-1/B/C, playback/recording, OBS, YP and settings tests
are included in full-suite verification. No CI configuration change is required.

The first full run caught the existing announce-isolation check after an initial
friend-assembly integration. That access was removed; the existing listener now
exposes only Coordinator resolution, registration, lifecycle and scalar cleanup
status. The original isolation test is retained unchanged and passes. No announce
foundation type or internal connector is made public.

## Local results

- Windows .NET SDK 10.0.301: restore and Release solution build pass, zero
  warnings/errors.
- Final full Release suite: 8,256 passed, 15 existing platform/native acceptance
  skips, zero failures (8,271 total). App 3,060; Network 1,189; Playback 3,104
  passed/15 skipped; Core 459; Protocol 66; CLI 374; Foundation 4.
- Source formatting and final solution-wide Format verification pass.
  Exact-head GitHub Actions is recorded in the PR/final report.

## Acceptance limits

All new TCP traffic is managed, synthetic and local. NAT backends/network clocks
are controlled fixtures; production router discovery/mapping is not operated.
The shared public listener uses a test-only loopback allowance; production source
validation is unchanged. Recovery reconnects a second synthetic parent on its
explicit bootstrap port; existing C fake-connector tests establish alternate
candidate selection independently.

Upstream HELO does not advertise a public `port`/`ping`. Direct ping control SID
is separate from channel Owner SID; automatic Station port verification/discovery
cannot be claimed without resolving that existing identity boundary. The managed
recovery test explicitly checks that no such port announcement is emitted.

No actual PeerCastStation executable, independent Internet node, Windows GUI,
OBS source, router or human audio/video acceptance was operated. Bind/map success
is not reachability proof. **WAN実機受入未確認** applies. New global endpoint
discovery/advertising, IPv6 WAN, node SID migration, BCST propagation, seamless
switching, bandwidth automation and relay-tree/manual-child GUI are not implemented.
