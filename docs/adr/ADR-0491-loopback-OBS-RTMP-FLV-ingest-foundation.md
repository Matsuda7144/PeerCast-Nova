# ADR-0491: Loopback OBS RTMP FLV Ingest Foundation

## Status

Proposed and implemented on the independent OBS ingest foundation branch.
Managed verification is recorded in PCN-0501.

## Context

Normal broadcasting should use OBS for capture, scene composition, mixing and
H.264/AAC encoding. Nova should provide a lightweight ingest, FLV, PCP publish,
relay and YP node. Existing native encoding remains a separate optional boundary.
The current main already has packet consumers, bounded relay fan-out, HTTP-FLV
bootstrap retention, recording bridges and a managed FLV parser, but no RTMP
receiver. Network depends on Core and Protocol.Pcp; it must not acquire a
production dependency on Encoding, Playback, Windows or the GUI.

## Decision

Add a one-generation IPv4 loopback listener in Nova.Network. The fixed bind
address is 127.0.0.1; options configure the port, application and one stream name.
OBS configuration is Custom Streaming Server, server
`rtmp://127.0.0.1:7145/live`, stream key `nova`, H.264 and AAC-LC.
The key selects a stream; it is not authentication.

The first TCP client owns the generation. Additional clients are closed while
the first is active. Any terminal outcome closes the listener; a new explicit
host instance is required for another generation.

Implement the simple RTMP3 handshake, bounded chunk formats 0..3 with extended
timestamps and basic headers, bounded AMF0 commands, connect, releaseStream,
FCPublish, createStream, publish/live, bandwidth check, explicit unpublish/close,
chunk-size change, abort, acknowledgements and ping response. Each incoming
message, interleaved aggregate, chunk-stream context, command structure and
bootstrap has a finite budget. Reads and responses have finite I/O deadlines.

Support classic AVC video and AAC-LC audio with indexed sample rates, mono or
stereo, and the ordinary two-byte AudioSpecificConfig. Validate avcC parameter
set framing and length-prefixed sample framing without decoding or re-encoding.
Reject codec replacement, AMF3, enhanced RTMP codecs, aggregate messages and
unsupported commands. Preserve encoded bytes, unsigned 32-bit RTMP DTS,
signed 24-bit AVC composition time and frame classification in the FLV tags.

Retain only metadata and both codec configurations. Publish one complete PCP
HEAD containing FLV header, optional metadata, AVC configuration and AAC
configuration after both configurations arrive. Drop initial audio and video
without buffering until an AVC keyframe containing an IDR arrives after HEAD.
The configuration order matches the existing managed FLV parser, which creates
its session when AAC follows AVC, irrespective of OBS arrival order.
Then publish each FLV tag as bounded sequential DATA slices, including its
PreviousTagSize. Do not rewrite timestamps to remove the wait for the first IDR.

Reuse the existing PcpChannelPacket and awaited IPcpChannelPacketConsumer
contract. The host borrows its consumer and does not dispose it. Each callback
must finish using packet memory before returning and must promptly observe
cancellation. Awaiting capacity stops subsequent RTMP reads. A partially
accepted tag followed by cancellation or consumer failure terminates the
generation; it is never replayed or retried. Delivery accounting advances only
after accepted callbacks. As with the current fMP4 publisher, stop before a PCP
wire position would exceed uint.MaxValue; long-duration wrap handling is deferred.

## Consequences

The ingress can compose with existing HTTP-FLV, relay fan-out and recording
consumers without a new PCP wire format or another encoder. RTMP session,
FLV bootstrap, packet ordering and lifetime belong to one publisher generation.
EOF is Disconnected, not successful media EOS. PublisherStopped means an
explicit RTMP stop command; it does not assert that any media was decoded,
rendered or heard.

This slice exposes a library host. Application channel registration, relay
listener hosting, YP announcement, CLI/GUI controls and OBS physical acceptance
remain subsequent work. Full RTMP service compatibility, TLS, authentication,
play, reconnect, multiple streams, codec replacement, WAN ingest, device capture
and native encoding are outside this decision.

## References

- [OBS RTMP client implementation](https://github.com/obsproject/obs-studio/blob/master/plugins/obs-outputs/librtmp/rtmp.c)
- [Adobe RTMP specification](https://rtmp.veriskope.com/pdf/rtmp_specification_1.0.pdf)
- [Implementation and managed review](../reviews/PCN-0501-LOOPBACK-OBS-RTMP-FLV-INGEST-FOUNDATION.md)
- [Host contract and usage](../obs-flv-ingest-foundation.md)
