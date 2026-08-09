# ADR-0481: Bounded ISO BMFF Regular Movie Native Decode Progress

## Status

Accepted as the native receive and managed-frame ownership boundary following
ADR-0480.

## Decision

Consume one exact ADR-0480 packet-submission session and advance native decode
by at most one packet-send, flush-send, or frame-receive action per public call.
When packet send reports backpressure, receive and copy exactly one frame into
managed ownership before retrying the retained packet. Keep the copied frame in
one exclusive disposable completion and prohibit further progress until that
completion is released.

After an exhausted merge traversal, flush selected roles in fixed video-then-
audio order. Retain flush backpressure across one-frame receive progress, retry
the exact role flush, then drain copied frames until that decoder reports end
of stream. Do not flush a caller-bounded truncated traversal. Apply a fixed
maximum copied-frame count and fail closed on receive without progress, role or
frame-kind mismatch, native rejection, or ownership inconsistency. Keep flush
and receive operations injectable; the production adapter composes the existing
reviewed null-packet flush and managed-frame copy boundaries.

## Consequences

The regular MP4 chain now has finite packet/backpressure/flush/drain progress
with exact selected-role order and managed decoded-frame ownership. Decoder
contexts and pending packet state remain under one aggregate lifetime.

## Non-goals

Canonical pixel/audio conversion, scheduler ingress, delivery, rendering,
application integration, discontinuity continuation, truncated-window
continuation, fragmented MP4, runtime discovery or loading, and physical
execution are not included.
