# FLV recording decodeable start

## Purpose

A live recording can join an AVC stream in the middle of a GOP. The first
received video tags may be predictive frames, so an otherwise valid recording
can play audio before the next independently decodable video frame. The
approximately 3.417-second wait observed in the earlier recording acceptance
run is historical motivation for this boundary, not a universal GOP delay.
This is a saved-file start rule; it does not capture media from before Record
was pressed.

## Supported profile and IDR rule

The gate applies to FLV inputs whose header advertises video and whose observed
video tags identify AVC/H.264. It reads the `AVCDecoderConfigurationRecord`
from the AVC sequence header and uses its declared NAL length size. Supported
length-prefix sizes are 1, 2, and 4 bytes; the reserved 3-byte form and
malformed configuration or NAL framing fail the transform safely.

An AVC media tag establishes the start only when its `AVCPacketType` is `1`,
its complete AVCC payload parses within the tag bounds, and at least one parsed
NAL unit has type `5` (IDR). The FLV FrameType keyframe bit alone is not
sufficient. The parser does not search arbitrary payload bytes for an IDR
marker and does not change codec data.

## Transform and retained state

The `.flv` sink path is:

```text
recording source
  -> bounded FLV decodeable-start gate
  -> existing shared-timestamp normalizer
  -> generic file writer
```

Both transforms live in `Nova.Core`; the recording layer does not depend on
`Nova.Playback`. The start gate owns retention and IDR detection. The existing
normalizer continues to own the shared Audio/Video timestamp baseline,
backwards-timestamp rejection, and byte-preservation rules.

The gate retains at most a 64 KiB FLV header, a 1 MiB unknown-codec probe
prefix, a 1 MiB AVC/AAC/ScriptData bootstrap set, and the current complete FLV
tag (bounded by the 24-bit FLV DataSize field). Once AVC is observed, actual
Audio and Video media before the first valid IDR are discarded. ScriptData is
retained byte-for-byte within the bootstrap bound. The latest AVC and AAC
sequence header replaces an earlier header of the same kind, in its original
bootstrap position. Exceeding the AVC bootstrap bound fails without a final
file; it never grows silently while waiting for an IDR.

Before a video codec is observed, the gate buffers at most 1 MiB so that an
early non-AVC stream can fall back to the existing recording behavior with its
captured prefix. If that probe bound is reached first, it deterministically
falls back to timestamp normalization and streams the remainder. This bounds
compatibility probing without treating non-AVC video as AVC.

When an IDR is accepted, the gate emits the original FLV header, retained
bootstrap tags, and the complete IDR tag. The normalizer then uses the IDR's
original tag timestamp as the single shared media baseline. The IDR becomes
timestamp 0; later Audio and Video subtract the same value. Pre-IDR audio is
not pulled forward. AVC CompositionTime, sequence-header timestamps and
payloads, ScriptData, StreamID, tag framing, and PreviousTagSize remain
unchanged.

## Audio-only and non-AVC inputs

When the FLV header does not advertise video, the gate immediately passes the
stream to the existing normalizer. Audio-only recordings do not wait for an
IDR. If the first observed video codec is not AVC, the gate emits the bounded
probe prefix and falls back to the prior normalization behavior. No IDR
semantics are assigned to other codecs.

## Stop or EOF before IDR

If AVC has been observed but no IDR start boundary is established before Stop,
EOF, or disconnect, the file writer returns an internal `NoDecodableVideoStart`
outcome, removes its partial file, and does not move a final `.flv` into place.
The sink exposes the distinct `NoDecodableVideoStart` status and stop result;
this is not represented as a fault and carries no raw exception text. Generic
non-FLV zero-byte writer behavior is unchanged.

The gate emits only complete, validated FLV tags. Once the normalizer has
started emitting a tag, its existing cancellation behavior drains that
complete tag before the next source read, so Stop cannot leave a partial tag.

## Exclusions

This boundary waits for the next IDR after Record. It does not keep a rolling
GOP buffer, include pre-Record media, request keyframes from a publisher,
transcode, remux, synthesize SPS/PPS, segment recordings, change the GUI, or
perform physical real-channel validation.
