# FLV recording timestamp normalization

## Contract

For an output path ending in `.flv`, the recording source passes through a
bounded FLV framing and timestamp transform before the generic file writer.
Other output formats remain byte-for-byte pass-through streams.

The timestamp on the first actual Audio or Video media tag establishes one
shared baseline. Later actual audio and video media-tag timestamps are rebased
by subtracting that same value. AAC and AVC sequence headers, AVC end-of-stream
tags, ScriptData tags, the FLV header, and every non-timestamp tag byte are
preserved. Audio and video do not receive independent baselines. Backwards
media timestamps, resets, wraps, malformed framing, and truncation fail the
recording transform; the recorder does not clamp timestamps, stitch a restart,
or create segments.

The transform buffers at most the bounded FLV header and one complete FLV tag
(whose DataSize is limited by its 24-bit field), and supports arbitrary source
read chunking. It emits a tag only after its full payload and `PreviousTagSize`
have been read and checked. Once emission of a complete header or tag starts,
stop cancellation drains that buffered record before the next source read, so
finalization cannot end in the middle of a tag. Source read failures remain
classified as `SourceRead`; malformed FLV input is classified as
`StreamTransform`.

## Metadata and scope

ScriptData / AMF metadata timestamps and fields are not rewritten. The accepted
recording copy contains `onMetaData` with `duration=0` and `fileSize=0`, and its
ScriptData tag is at timestamp zero. Offline validation of a copy of run
`PR242-LIVE-REC-REVALIDATE-20260928T054114Z-0C5AAA97B639BB420CCEECE7F733F5CC`
found a first media-tag timestamp of 4,127,494 ms and final media timestamp of
4,162,602 ms. After rebasing, ffprobe reported start time 0.015 seconds and
duration 35.108 seconds while the ScriptData bytes remained unchanged. This
supports leaving metadata untouched for this format slice; it does not establish
a general metadata-rewrite rule for other FLV producers.

This work changes only FLV tag timestamp fields for actual media tags. It does
not alter payloads, codec configuration, AVC composition-time offsets, sizes,
StreamID, or `PreviousTagSize`, and adds no remuxing or transcoding dependency.
The existing explicit PCP discontinuity and source-sequence rejection behavior
remains in force. The previously observed 3.417-second delay before the first
decodable video frame is a separate recording-start keyframe-quality issue and
is out of scope.

PeCaRecorder was examined only as behavioral reference material. No source or
decompiled implementation was copied, and this design makes no claim about its
internal timestamp behavior.
