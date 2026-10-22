# Phase 5F-19: GUI Next public channel owner composition

Independent integration base: bd78103218e14c4e09a6b2c42b82af9703ae85b6 (main after #309).
Source: #293 at e08fb4756241ba9b0de531e5f9a06a4c3b64ecd9; original stacked base: dbd55fb368870262433a1f0adcf23713445e8f91 (#292). Only the source's nine-file delta is integrated, preserving current main's GUI, settings, font and transport fixes. Main exact-SHA CI 39830120117 succeeded at preflight; original #291/#292 are closed without merge.

## Result and reachability

**GUI NEXT PUBLIC CHANNEL OWNER STARTUP INTEGRATION READY — PUBLIC ACTIVATION PENDING**

Gate 1 is B. The normal startup path constructs the composition and exposes it
through a typed application boundary before creating the interaction bridge:

1. `Program.cs` -> `NovaApplicationEntrypoint.RunAsync`.
2. `NovaWindowsStartupComposition.RunGenesisAsync` constructs the interactive surface and system state loader.
3. `SystemGenesisInteractiveMainWindowSurfaceTarget.OpenAsync` loads `GenesisGuiNextApplicationState`.
4. The state creates `NetworkPolicies` from `Connection.Startup`, then the existing `DownstreamRelays` registry, then `PublicChannels` with that same registry.
5. The surface assigns `GenesisMainViewModel.PublicChannels` before bridge creation. Normal close and bridge rejection clear the non-owning reference.
6. The state owns composition shutdown, then retires the registry and Startup policy.

The managed startup test exercises the existing RunGenesisAsync host/projection/
interactive-surface path with native host/surface and stores replaced. Default
public startup's construction of this same surface/loader is source-traced; no
native GUI execution is claimed.

No current GUI command prepares or activates a public owner. Startup, settings
save, channel selection, Play and Recording do not call the new RunAsync. This is
a usable owner composition reachable from normal startup, not active public
enforcement. No public listener was started in this phase.

## Reuse and responsibilities

Daily's `NovaDailyPcpRelayController.StartAsync` uses a new channel generation,
`PcpRelayCoordinator`, and IPv4 Any:0 on an explicit Daily Relay command. Its
wrapper also binds UI callbacks, legacy settings and NAT. The GUI Next composition
reuses the concrete coordinator below that wrapper; it does not copy the Daily
controller or load its settings/NAT runtime.

| Entry | Authoritative role | Runtime / fanout owner | Admission path |
|---|---|---|---|
| PrepareViewingAsync(explicit upstream PCP source, endpoint) | Viewing: an explicit receive/relay operation | Existing PcpRelayCoordinator owns upstream, fanout, downstream server | Standalone server -> PR #292 child session injection |
| PrepareBroadcastAsync(producer ChannelId, ChannelSessionId, exact fanout, endpoint) | Broadcast: local producer supplies identity and fanout | Composition owns existing downstream server; producer still owns fanout | Standalone server -> PR #292 child session injection |

Broadcast is a producer handoff contract. This branch has no connected local
producer/publishing lifecycle; none is invented or inferred from ChannelId/IP.
The same ChannelId can acquire a new authoritative role/generation, but existing
leases count until actual cleanup. Local viewing does not determine role.

No shared routed listener is added or selected. Its PR #292 injection/lifetime
contract is unchanged. One TCP connection gets one reservation, never both paths.

## Preparation versus activation

Preparation constructs existing inert runtime objects and registers the exact
fanout plus authoritative generation with the existing registry. There is no
socket bind, upstream connect or router operation during construction/preparation.
A viewing generation is created for the explicit receive operation; producer
generation comes from the producer. Replacing a prepared owner retires previous
admission but does not kill its live connections.

Only the explicit prepared-owner `RunAsync` calls existing coordinator/server
`RunAsync`. A retired owner cannot activate. Normal GUI has no caller for this
operation yet. In this slice, the endpoint must be an explicitly supplied IPv4
ephemeral plan (port 0); it is copied so caller mutation cannot change it.
Fixed ports including 7144 and IPv6 are rejected before registration. The saved
ListenPort/UseIpv6 values are not applied or silently reinterpreted.

Managed tests activate loopback port-0 plans only. Any:0 plans are prepared and
disposed unstarted to test composition, never bound.

The six existing non-loopback temporary-bind tests are excluded from local solution testing for this integration. The earlier #308 exception is not inherited. Filtered local results and the unchanged hosted GitHub Actions full-suite result are separate evidence. No physical GUI, production Nova, real router/NAT operation or WAN probe is performed. Existing Evidence and #306 Physical NOT_ACCEPTED are preserved.

## Admission and cleanup

The application owner forwards the existing registry registration; it creates no
policy or parallel count. The coordinator's optional admission argument forwards
to its existing standalone server. Its non-owning DownstreamFanout identity lets
the application bind exactly the coordinator-owned fanout; the composition never
disposes that fanout separately. Defaults with no admission preserve Daily and
media-owner behavior.

PR #292 ordering remains: validate HTTP/version/ChannelId, provisional policy
reservation, independent fanout reservation, existing HTTP/HELO/OLEH/OKAY or
503/RelayFull response, session lifetime, real TCP CleanupCompletion and callback
completion, lease release. HELO validation still follows the HTTP capacity reply.
No packet/rejection format changes or second RelayFull response are introduced.

Owner retire closes admission only. It does not dispose a live reservation.
Run completion/failure and explicit disposal retire the owner and delegate
cleanup once to the existing runtime; concurrent callers await the same disposal
task. Composition shutdown synchronously retires every prepared owner before
scheduling cleanup, preventing late activation after its shutdown request.
Disposal continuations force asynchronous execution without capturing the GUI
synchronization context; publishing the shared task precedes cleanup re-entry.
Existing finite runtime shutdown bounds remain in control. Registry/policy
retirement follows owner shutdown and cannot fabricate capacity for late cleanup.

Construction/registration failures dispose the unstarted runtime. Removal is
identity-checked so late old-generation cleanup cannot erase a replacement.
Shutdown continues across owners and reports failures after attempting all.
Application-state cleanup still retires registry/policy and performs existing
settings cleanup if public-owner shutdown fails.

## Managed proof and limits

New tests cover the actual owner composition, normal host/surface handoff,
null/zero/positive limits, authoritative roles, generation replacement,
same-generation/fanout rejection, simultaneous admission/shutdown, stale activation,
actual upstream PCP exclusion, actual local playback/recording lease exclusion,
fanout resource bounds, negotiated rejection/timeout/cancel/disconnect, real TCP
release/re-admission and Startup versus Saved behavior.

Retained PR #292 tests cover borrowed routed transport and non-cooperative child
callbacks. Production owner integration here is standalone; no routed public
activation is claimed. Physical public relay, native GUI and router acceptance
remain unperformed. Bandwidth policy/enforcement, settings schema/fonts/media
pipelines, ListenPort/IPv6/NAT and Daily activation behavior are unchanged.

## Next minimal slice

Add an explicit GUI Next public Relay start/stop command and activation plan:
user intent, selected authoritative channel, endpoint ownership/port conflict
feedback, readiness/error presentation and finite stop. Feed it the existing
view model PublicChannels boundary and prepared-owner RunAsync. Review fixed
ListenPort/IPv6/NAT separately; do not auto-bind at startup or replace
playback/recording loopback owners. Connect a real producer only through its
authoritative lifecycle. This phase does not automatically start that work.
