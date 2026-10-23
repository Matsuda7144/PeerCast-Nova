# Headless OBS publish channel foundation

## Read-only audit and prerequisite

Predecessor: 01bac910a03654cdadefd7b1ff6a6d416ee6ca78.
Main/merge-base: 1193569f79274a2797e544f8796c85183227eda2.
The inspected Core/PCP/Playback primitives were identical on both trees.

The audit initially stopped because HTTP bootstrap was AAC-before-AVC while the
existing managed FLV parser requires AVC-before-AAC. An external audit probe
confirmed the same 75-byte bootstrap creates a stream description directly,
but HTTP's reordered bootstrap fails after 32 bytes. The user explicitly asked
to resume, then to resume safely, on 2026-10-03.

The prerequisite changes the existing generic HTTP bootstrap writer to
header / metadata / AVC / AAC. Existing ingest HEAD construction and parser
policy stay unchanged. Both classic FLV sequence-header orders are valid on the
wire; this host uses the order already supported by Nova's managed parser.
No OBS-specific HTTP or relay branch is added.

| Existing primitive | Audit / reuse decision |
| --- | --- |
| ChannelId / ChannelSessionId | Reuse; channel identity is independent of route key and generation. |
| ChannelStatusTracker | Reuse status/fault snapshots, not a resource owner/registry. |
| PcpRelayCoordinator / PcpRelayRuntimeComposition | Existing owner for a network PCP upstream session. No local-source injection point; do not create a fake upstream PCP connection or duplicate its relay logic. |
| PcpRelayFanout | Reuse bounded payload copying, retained HEAD replay, ordered child queues, slow/failing child isolation and drain observation. |
| PcpDownstreamRelayServer | Reuse existing PCP TCP serving; bind IPv4 loopback only. |
| PcpHttpFlvPlaybackHost / Session | Reuse channel-ID map, duplicate-source prevention, session checks, bounded retention and atomic bootstrap/client admission. Add a bounded internal completion path. |
| RecordingStreamSourceLeaseRegistry | Recording-only map and identity-bound removal token. Add TryRegister for atomic non-replacing reservation; existing Register semantics remain. |
| PcpRelayFanoutRecordingStreamSourceLeaseFactory | Reuse recording reader leases from the same fanout; no recording network connection or automatic recording is added. |

No generic channel-resource registry or existing local publisher owner was found.
The minimal new ObsPublishChannelHost joins these existing primitives rather
than introducing another relay, HTTP server or broad owner abstraction.

## Production composition

```text
OBS-shaped publisher
  -> ObsRtmpIngestHost
  -> ObsFlvPublishingSession (unchanged HEAD / DATA rules)
  -> ObsPublishChannelHost.PublishConsumer
  -> PcpRelayFanout (one instance per generation; retained HEAD replay)
       -> PcpDownstreamRelayServer -> PCP localhost peer
       -> mandatory HttpRelayChild
            -> PcpHttpFlvPlaybackSession
            -> existing bounded HTTP client fanout
            -> PcpHttpFlvClientSink -> HTTP localhost client
       -> optional existing recording reader leases
HTTP body -> PlaybackBoundedFlvParser (managed tests only)
```

The host owns ingest, relay fanout/server, HTTP source, recording lease factory,
registration token and status tracker. It owns an HTTP listener by default;
a supplied, already-started HTTP host is borrowed. A supplied recording registry
is borrowed. It never disposes the borrowed host or registry.

ChannelId defaults to a newly generated existing PeerCast identifier; callers may
provide a stable ChannelId. Each host always generates a fresh ChannelSessionId.
The RTMP key nova is only a route name. HTTP's existing source map prevents
duplicate active IDs on one shared HTTP host; TryRegister also prevents duplicate
owners sharing a recording registry, even with different HTTP hosts. Scope is the
supplied runtime maps, not a process-global registry.

## Lifetime and acceptance

Existing Core states are Created -> Connecting -> Negotiating -> Receiving ->
Stopping -> Stopped. Connecting/Negotiating describe library setup and waiting
for a valid HEAD; no per-command RTMP state telemetry is added. Receiving starts
after HEAD is accepted by the mandatory HTTP relay child. Fault categories are
fixed Core snapshots and remain available in the terminal Stopped snapshot.

Start binds listeners/registers one generation. HEAD is retained by existing
relay and HTTP primitives; duplicate/replacement config policy remains ingest's
policy. Recording acquisitions open only after HEAD acceptance. Late join
replays current HEAD and subsequent DATA, not historical DATA. This does not
provide late-join IDR caching or decoder acceptance for arbitrary join times.

An ingest callback waits for fanout admission and the mandatory HTTP child's
ConsumeAsync before accepted counters/position advance. Browser/TCP writes are
separate downstream delivery. Queues remain finite; optional slow/failing relay
or browser children are removed independently. The mandatory child's failure
terminates the owner as ConsumerFaulted. At least two pending packet slots and
two MaximumPacketBytes budgets per relay child are required (the acknowledged
callback can overlap the next admission before its worker releases accounting);
no extra media queue or unrestricted packet acknowledgment map is introduced.

### Explicit-start waiting contract on current main

Construction allocates an unstarted generation; it binds no listener and adds
no recording registration. Only explicit Start opens the required IPv4 loopback
RTMP, PCP relay and owned HTTP listeners. No Nova.App, Daily or GUI autostart is
introduced by this foundation integration.

After Start, no publisher arrival deadline is imposed. Waiting is represented
by the owner's Negotiating state, with no HEAD/Receiving evidence. Listener,
source, registration and child capacities stay bounded during that wait.
IoTimeout applies to connected protocol I/O, not the initial TCP accept.
Cancellation or Dispose cancels accept and releases the generation's listeners,
HTTP source and recording registration. A borrowed HTTP host remains owned by
its caller; this generation removes only its own source.

An unconnected cancellation/disposal reports HostCancelled/Cancelled, no
accepted media and no successful HTTP/relay drain. ShutdownCompleted describes
resource cleanup, not clean EOS. A future optional publisher arrival deadline
would be a separate policy; none is added here. Managed test wait limits bound
the tests only and do not define production timeouts. Source #259's historical
physical evidence does not establish acceptance of this independent integration.

On explicit PublisherStopped, with upstream finished, observe bounded relay
drain, freeze HTTP source admission, drain existing HTTP queues, then complete
recording readers, close relay children/source and remove registrations.
Timeout reports TimedOut drain and HttpDrained=false, followed by cleanup.
Caller/disposal cancellation during drain reports HostCancelled/Canceled, not
clean EOS. Disconnected, protocol/consumer faults and cancellation skip graceful
drain and complete recording readers with a terminal fault. Closing a raw HTTP
connection cannot carry a structured terminal reason; use owner Completion for
that distinction. A drain observation is not proof every child received bytes.

Cleanup is attempted for every owned resource even after a cleanup failure;
ShutdownCompleted is reported separately. Registration tokens and HTTP removal
are identity-bound, so old disposal cannot remove a newer generation. Restart
on the same host is rejected; a new host may reuse the channel/key with a fresh
session. This is not reconnect automation.

## Headless use

```csharp
await using var publish = new ObsPublishChannelHost(
    new ObsPublishChannelOptions
    {
        Ingest = new ObsRtmpIngestOptions { Port = 7145 },
        RelayPort = 0,
        Http = new PcpHttpFlvPlaybackOptions { Port = 0 },
    });
publish.Start(stopToken);
// Endpoints and HttpStreamUrl are available after successful setup.
var result = await publish.Completion;
```

This is a reusable production library boundary; no executable, GUI, YP or
public listener is wired. Start failures are reported through Completion and
the fixed fault snapshot; endpoint access requires successful setup.

## Managed evidence / remaining boundary

Owner tests cover registration, HEAD replay, drain, EOF/cancellation/fault,
duplicate sources/reservations, second publisher, slow relay isolation, startup
failure, cleanup, drain timeout/cancellation and channel/key reuse with a new
generation. Core tests cover bounded non-replacing registration and stale tokens.

Two localhost E2E cases connect the managed RTMP client to the owner, actual
existing PCP TCP server, actual existing HTTP listener and existing FLV parser:
an early viewer and a late viewer after previous DATA. Both assert exact HEAD,
AVC/AAC extradata, video/audio bytes, extended DTS, signed CTS/keyframe, two parsed
packets, HTTP EOF after normal stop and HTTP 404/unregistration afterwards.

HEADLESS OBS INGEST -> NOVA PUBLISH CHANNEL -> RELAY / HTTP-FLV COMPOSITION PROVEN
(managed localhost tests).

REAL OBS INTEROPERABILITY NOT YET PROVEN.
No OBS launch, native decoding or human video/audio acceptance is performed.
GUI, YP, WAN/public publishing, reconnect, NAT/UPnP, auth/TLS, other codecs,
multitrack/plugin/capture/normal encoder work and soak tests remain excluded.
Next: separately authorized bounded real OBS interoperability acceptance via
localhost RTMP -> owner -> HTTP-FLV -> MPC-BE / PCRPlayer, with one human moving
video / audible audio confirmation.
