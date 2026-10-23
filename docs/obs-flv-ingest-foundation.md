# OBS FLV ingest foundation

## Read-only audit

The independent branch starts from current main
`1193569f79274a2797e544f8796c85183227eda2`, verified against the GitHub branch
API and Git remote on 2026-10-02. The baseline checkout was clean.

| Existing boundary | Reuse in this slice |
| --- | --- |
| H.264/AAC encoding in Nova.Encoding | No ingest dependency; OBS supplies encoded packets |
| PcpChannelPacket / IPcpChannelPacketConsumer | Identity, HEAD/DATA, position, borrowed memory and awaited capacity |
| PcpFmp4PublishingBoundary | Existing opaque-segment publishing precedent; FLV keeps its own codec/bootstrap gate |
| PcpFlvBootstrapRetention / PcpHttpFlvPlaybackHost | Existing streaming FLV bootstrap observation and localhost HTTP output |
| PcpRelayFanout | Existing bounded independent relay-child queues and owned payload copying |
| PcpRecordingPacketStreamBridge / RecordingRuntime | Existing stream/file boundaries for later host composition |
| PlaybackBoundedFlvParser | Existing managed parser; test-side validation only |
| PcpRelayCoordinator / YP / application channel owners | Subsequent broadcast hosting slice |

Production dependencies stay Network -> Core / Foundation / Protocol.Pcp.
No Encoding, Playback, Windows or GUI dependency is added.

## Host composition

The library host requires caller-supplied channel/session IDs and a packet
consumer. The application owner is responsible for registering the channel,
owning any relay/HTTP/recording consumer, checking Completion, and completing or
disposing downstream resources after ingest stops.

```csharp
await using var host = new ObsRtmpIngestHost(
    channelId,
    sessionId,
    packetConsumer,
    new ObsRtmpIngestOptions
    {
        Port = 7145,
        Application = "live",
        StreamName = "nova",
    });
host.Start(stopToken);
var result = await host.Completion;
```

With such a host running, configure OBS Custom Streaming Server:

- Server: `rtmp://127.0.0.1:7145/live`
- Stream key: `nova`
- Video: classic H.264/AVC
- Audio: AAC-LC, mono/stereo; indexed or supported explicit sample rate and bounded
  SBR-absent ASC forms described in [AAC compatibility](obs-aac-audiospecificconfig-compatibility.md)
- Disable automatic reconnect for this single-generation foundation

There is no broadcast start command or GUI binding in the current application
yet. This library example describes the composition required by the next host
slice. The stream key is a name, not a secret or authentication credential.

## Bounds and delivery

Defaults are 1 MiB per incoming RTMP message, 4 MiB for incomplete interleaved
bodies, 16 chunk contexts, 64 KiB chunk-size ceiling, 64 KiB bootstrap/PCP
packet ceiling and 15-second input/output deadlines. Hard limits are enforced
when constructing the public host. A completed message and its FLV copy are
transient additional allocations; the incomplete-body budget is not a claim
about total process memory. No full stream or unbounded media queue is retained.

The initial HEAD contains FLV header and both sequence headers. Optional
`@setDataFrame` metadata loses only its RTMP wrapper string. Media begins with
the first post-bootstrap IDR. Samples arriving before that gate are discarded
without queueing. Timestamps are preserved, so the first media DTS may be nonzero.
Late-join IDR admission is a subsequent relay/playback concern.

The consumer's awaited callback is the backpressure boundary. Consumer
completion means accepted bytes at that boundary; fan-out admission does not
prove delivery or decoding by every child. A consumer must copy retained
payloads and observe cancellation. The host does not own or finish its consumer.
At most one publication callback is active.

Terminal results distinguish explicit stop, EOF/disconnection, cancellation,
timeout, protocol rejection, consumer rejection and transport failure. No
automatic reconnect or retry follows any of them. Successful stop is not
decoder EOS, and partial DATA delivery after a failure is not resumed.

## Managed evidence and remaining work

RTMP tests use an independent test-side wire encoder and real localhost TCP.
They exercise OBS's connect/releaseStream/FCPublish/createStream/publish
sequence, controls, chunk-size changes, rejection, concurrent publisher
exclusion, I/O deadline, backpressure, cancellation and cleanup.

FLV tests compare complete expected tags, extended DTS, negative composition
time, exact AVC/AAC bytes, bounded DATA slices and the existing bootstrap
retention. Integration forwards the ingest output through the existing
localhost HTTP-FLV consumer and compares all delivered bytes. An independent
Playback test passes generated HEAD/DATA through the existing managed FLV
parser and verifies stream description, payloads and timestamps.
Ingest preserves the full unsigned timestamp on the wire; the existing managed
parser's separate default seven-day timestamp-magnitude policy still applies.

These establish managed protocol and container boundaries. Actual OBS
interoperability, external-player video/audio, native decode, physical
playback, a PCP TCP broadcast to another peer, YP, WAN, prolonged operation and
late join remain UNVERIFIED.

Next recommended slice: add an explicit headless broadcast owner that registers
one channel and composes this host with existing relay and HTTP-FLV consumers.
Keep its channel lifetime, stop reasons and downstream completion observable.
Then perform a separately authorized, bounded real-OBS localhost acceptance.

Design: [ADR-0491](adr/ADR-0491-loopback-OBS-RTMP-FLV-ingest-foundation.md).
Managed review: [PCN-0501](reviews/PCN-0501-LOOPBACK-OBS-RTMP-FLV-INGEST-FOUNDATION.md).

OBS source audit and compatibility hardening:
[OBS classic RTMP compatibility](obs-rtmp-compatibility.md).

The subsequent channel owner and existing relay/HTTP-FLV composition are now
documented in [Headless publish channel foundation](headless-obs-publish-channel.md).
