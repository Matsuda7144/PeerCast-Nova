# OBS classic RTMP compatibility audit

Audit predecessor: `c9c58ec9df2d1fc6fb565e806adeffd81abffffa`.
Main/base: `1193569f79274a2797e544f8796c85183227eda2`.
Branch: `feature/obs-flv-ingest-foundation`.
OBS master reference checked on 2026-10-02:
`97ca37291c4e5f5c5c8a0719b8c7b897d2fbc6b5`.
This is a source/wire behavior audit with Nova-owned managed tests.
No OBS implementation or upstream test vectors were copied into Nova.

## Audit decision

One concrete production gap was identified before edits: the bounded AMF0 decoder
accepted unknown event/body shapes, but the FLV publisher then terminated the
whole session. Four new compatibility cases reproduced ProtocolRejected before
hardening. The only production change validates the complete metadata payload and
its string event envelope, then ignores valid unknown events/unsupported body
shapes without retaining bytes or calling the consumer.

Recognized onMetaData object/ECMA-array metadata retains its original AMF encoding,
stripping only the optional @setDataFrame wrapper. The existing limits remain:
64 KiB AMF payload, 4096-byte strings, depth 4, 256 values, 128 array elements,
strict UTF-8 and the separate message/bootstrap budgets. Malformed or oversized
input fails closed even for an unknown event. AMF3 and AMF0 value kinds outside
the existing bounded decoder subset remain intentionally unsupported; this
change tolerates event/body shapes within that subset, not arbitrary binary data.

## Classification (source and managed evidence)

| Boundary | Classification | Evidence / policy |
| --- | --- | --- |
| Handshake | compatible | RTMP3; zero-version S1 selects OBS simple fallback; S2 echoes C1; changed C2 time2 is permitted. |
| connect | compatible | OBS sends chunk size 4096 before connect, chunk stream 3 / message stream 0, AMF0 app/type/flashVer/swfUrl/tcUrl. Unknown connect properties within decoder bounds are accepted. _result echoes transaction and carries Connect.Success/objectEncoding 0. |
| releaseStream / FCPublish | compatible | OBS sends both without waiting and queues neither response. Nova's additional _result responses are ignored by OBS's unmatched-call path. |
| createStream | compatible | Pipelined on chunk stream 3 / message stream 0; echoed transaction, null command object, numeric stream 1. Second creation fails closed. |
| publish | compatible | OBS sends nonzero transaction on chunk stream 4 / message stream 1. StreamBegin then onStatus(transaction 0, Publish.Start); OBS starts on the status code. One live stream only. |
| _error / auth negotiation | unsupported intentionally | Valid startup does not require _error. Invalid commands terminate with ProtocolRejected; no authentication challenge or rejection negotiation. |
| ACK / windows / ping | compatible | Existing incremental input ACKs, window updates, peer bandwidth receipt, ping response and chunk controls. No general congestion controller. |
| Metadata | hardened | Exact OBS-style 20-field ECMA array (numbers, booleans, encoder string) accepted before capture; bounded valid unknown shapes ignored before/after HEAD. Full parsing still rejects malformed/oversized input. |
| AVC header | compatible | Classic track 0 avcC and AVCC framing; duplicate allowed, replacement rejects. Header required before video; both codec configs required before HEAD. |
| AAC header | compatible | OBS audio-first startup accepted, HEAD canonicalized AVC then AAC for existing parser. AAC-LC with indexed sample rate and mono/stereo; duplicate allowed, replacement rejects. |
| Media packets | compatible | Shared chunk stream 4 / message stream 1; fmt0/1/2/3 and extended timestamps/continuations. Preserve payload, frame classification and signed CTS. Drop initial audio/interframes until a true post-bootstrap IDR. |
| Timestamp baseline / discontinuity | compatible for preservation | OBS subtracts its initial DTS; Nova does not normalize again. Nonzero first media DTS and backward DTS are preserved; no inferred discontinuity/decoder reset. Downstream timestamp/late-join policy remains separate. |
| Stream key / URL | compatible | Server rtmp://127.0.0.1:port/live plus separate key nova maps to app live and publish name nova. Exact matching, IPv4 loopback bind. No credentials/query aliases. |
| Disconnect / explicit stop | compatible | OBS classic H.264 does not require an enhanced footer. FCUnpublish then deleteStream: finish on the first valid terminal command, distinguish EOF as Disconnected. Incomplete chunk contexts cannot fabricate DATA/EOS; accepted socket/listener close and rejection task is joined. |
| Downstream completion / ownership | unverified composition | Completion reports accepted callbacks and session end only. Consumer is borrowed; future Publish Channel Owner must finish/dispose it. Session arrays have bounded lifetimes; no deterministic GC/physical-release claim. Cooperative cancellation remains required. |
| Real OBS interoperability / physical A/V | unverified | REAL OBS INTEROPERABILITY NOT YET PROVEN. No real OBS launch or physical run. |

## Managed coverage

`ObsRtmpCompatibilityTests` adds the OBS-shaped command pipeline with full
response assertions, metadata before capture, audio-first headers, interleaved
media on one chunk stream, signed CTS/frame classification, compressed and
extended timestamps with 4096-byte continuations, four safe-ignore shapes,
malformed/oversized metadata, explicit stop with an incomplete other chunk
context, complete/partial EOF, and baseline/backward timestamp preservation.

The predecessor tests continue to cover unsupported audio/video codecs, media
before configuration, duplicate/replaced configurations, second publisher and
stream, ACK/ping/abort, bootstrap/message/context limits, backpressure, callback
failure, cancellation and disposal. Existing relay/HTTP-FLV regression tests are
retained; no new relay or HTTP-FLV composition is introduced by this slice.

## Pinned upstream source anchors

- [rtmp-stream.c](https://github.com/obsproject/obs-studio/blob/97ca37291c4e5f5c5c8a0719b8c7b897d2fbc6b5/plugins/obs-outputs/rtmp-stream.c): try_connect / init_send / send_meta_data / send_headers / send_footers / send_packet.
- [flv-mux.c](https://github.com/obsproject/obs-studio/blob/97ca37291c4e5f5c5c8a0719b8c7b897d2fbc6b5/plugins/obs-outputs/flv-mux.c): build_flv_meta_data / flv_video / flv_audio.
- [librtmp/rtmp.c](https://github.com/obsproject/obs-studio/blob/97ca37291c4e5f5c5c8a0719b8c7b897d2fbc6b5/plugins/obs-outputs/librtmp/rtmp.c): SendConnectPacket / SendCreateStream / SendPublish / HandleInvoke / HandleServerBW / HandleClientBW / RTMP_Write / RTMP_Close.
- [librtmp/handshake.h](https://github.com/obsproject/obs-studio/blob/97ca37291c4e5f5c5c8a0719b8c7b897d2fbc6b5/plugins/obs-outputs/librtmp/handshake.h): HandShake, RTMP3 zero-server-version fallback.

## Next slice

Headless Publish Channel Owner → existing relay fanout → existing HTTP-FLV →
localhost end-to-end. Make channel registration, session lifetime, accepted
delivery and downstream completion observable. Perform a separately authorized
bounded real-OBS interoperability/physical A/V run after that composition exists.
GUI, YP, WAN, public bind, authentication/TLS, reconnect, enhanced/multitrack,
other codecs, OBS plugin and Nova capture/normal encoder work remain excluded.
