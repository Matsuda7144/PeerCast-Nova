# Phase 5F-18 ownership investigation

Independent integration base: 172153c99d016d0a8226affcd7bc9e109610be5b (main after PR #308).

Source: PR #292 at dbd55fb368870262433a1f0adcf23713445e8f91, whose original stacked base was bda79041984df58f6ed5980a886153df8bd2dc12 (PR #291). Only that source commit's 12-file delta is integrated; parent history is not imported.

## Gate 1: C

Program.cs selects NovaWindowsStartupComposition.RunGenesisAsync. NovaDailyPlaybackStartupComposition has a public IPv4 ephemeral downstream server, but no shipped Program entry instantiates that composition. GUI Next's GenesisSharedPcpPlaybackRuntime owns loopback ephemeral playback/recording routes. Those routes are not a public relay owner.

Connecting GUI Next Startup settings to a reachable public owner requires an explicit startup/composition slice. This phase implements the permitted registry foundation and optional PCP admission seam only. No normal startup path enforces relay limits yet.

## Ownership and ordering

- Standalone PcpDownstreamChildSession owns transport and reader. Routed sessions borrow them; PcpSharedDownstreamRelayListener releases route/reader/transport after the handler returns.
- TcpTransportConnection.ConnectionId is the process-wide identity. IsDisposed marks disposal start, not completed cleanup.
- HTTP request format, PCP version and ChannelId are validated before provisional admission. There is no separate authentication layer in this path. HELO identity validation follows the HTTP capacity reply in the existing protocol; preserve that order. A provisional reservation is not a fully negotiated relay.
- New policy capacity and existing fanout resource capacity are independent. ActiveChildCount contains local consumers and is not the relay count.
- Authoritative owner supplies Broadcast or Viewing plus ChannelSessionId. ChannelId and remote loopback IP never determine classification.
- Exact fanout binding protects stale routed generations. Unknown/unbound routes retain legacy behavior only when every relay limit is null; finite settings fail closed when this resolver is explicitly injected.
- Registry owns identity/admission leases, never TCP, reader, writer or fanout. Retired/disposed owners deny new requests but existing leases remain.
- Reservation release observes actual transport cleanup and child callback task completion. Finite session shutdown remains bounded; late callbacks retain slots until they return. If actual TCP cleanup is unproven, retain the slot conservatively.
- Registry uses the same NetworkPolicies.Relays instance built from Connection.Startup. Saved settings cannot mutate current limits.

## Scope

Bandwidth, listener bind/IPv6/NAT, GUI/fonts/settings, media processing and public startup architecture remain outside this phase. Managed loopback tests are permitted; no physical run is performed.

## Live authority

At independent-integration preflight, main matched the base above and exact-main GHA 39824142530 succeeded. Source #292 remained OPEN/Draft at the source HEAD above, based on #291, with exact-head GHA 38487528234 successful. PR #308's policy foundation is already in main and is reused without creating a second policy. Older stacked-PR status statements are historical and are not current acceptance evidence. Source and downstream PRs/branches, existing Evidence, and #306 Physical NOT_ACCEPTED are preserved.

## Reachability

| Path | Existing owner | This phase |
|---|---|---|
| Program -> RunGenesisAsync | GUI Next application state | Creates registry from Startup policy; no public owner registered |
| GenesisSharedPcpPlaybackRuntime.CreateOwner | Playback/recording shared loopback owner | No admission injection; retains existing media consumer behavior |
| NovaDailyPlaybackStartupComposition -> NovaDailyPcpRelayController | Alternate Daily public IPv4 owner | No injection; Program does not construct this alternate composition |
| PcpDownstreamRelayServer with explicit admission | Existing standalone PCP server class | Optional injection; exercised only in managed loopback tests here |
| Shared listener + routed handler with explicit resolver | Existing listener owns borrowed transport and reader | Exact fanout resolver injection; exercised only in managed loopback tests here |

No production startup enforces the new policy. Do not interpret the optional seam or the creation of an empty registry as public runtime integration.

## Managed verification boundaries

- Registry tests cover three limits, zero/null, multiple channel roles, duplicate identities, atomic concurrent reservation, retired/replaced generations, weak fanout bindings, unknown role and lifetime disposal.
- Application tests prove the registry uses the existing Startup policy instance and that Saved changes affect only the next application state.
- Integration tests use the real standalone server, shared listener and routed handler on managed loopback ephemeral endpoints. They cover HTTP 503/OLEH/QUIT RelayFull, simultaneous requests, total/per-role limits, remote disconnect, normal QUIT, cancellation, invalid HTTP/channel/HELO, timeout, owner shutdown, registry retirement and generation replacement.
- A borrowed handler returning does not prove transport cleanup. Its reservation stays occupied until the listener owner disposes the real transport.
- An uncooperative child callback keeps capacity after finite session shutdown, even when TCP is already closed; both QUIT and cancellation are covered.
- Actual recording source leases and local fanout sinks do not increment registry usage; the independent fanout bound still rejects new children. Merely connecting TCP/upstream-style transports does not register a downstream identity. Existing upstream PCP regressions remain relevant separately.
- TCP tests distinguish disposal-start, duplicate no-op disposal, completed callback and real socket closure after callback failure.

Local execution in this independent integration permits managed loopback ephemeral fixtures only. The six existing non-loopback temporary-bind tests previously permitted for #308 are excluded locally here; that exception is not inherited. Filtered local results are not an unfiltered full-suite PASS. The unchanged automatic GitHub Actions workflow remains the source of exact-head full CI evidence. No production Nova, public listener, real router/NAT operation, or physical GUI run is authorized by these tests.

PcpDownstreamChildResult.CleanupCompleted describes the existing bounded child-session cleanup. It is not the new admission-release signal for a borrowed transport, and can be false while a late worker still holds a reservation. A failed transport cleanup signal retains the slot; this conservative failure path is documented but OS socket-disposal failure itself is not artificially injected.

## Next slice

Define and connect a reachable GUI Next public channel owner/startup composition, authoritative Broadcast/Viewing lifecycle and registry registration/retirement. Review listener ownership separately before enabling a public bind. Reuse this registry and PCP seams. Do not attach admission to playback/recording loopback owners as a substitute. Physical relay acceptance and bandwidth enforcement remain separate work.
