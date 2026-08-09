# ADR-0479: Bounded ISO BMFF Regular Movie Native Decoder Session

## Status

Accepted as the decoder-context ownership boundary following ADR-0478.

## Decision

Consume one exact regular-movie verified-runtime capability binding and open
the selected video and audio decoder contexts in fixed video-then-audio role
order. For each selected role, transfer ownership of its verified ABI gate to
a newly allocated codec context, transfer the exact managed codec parameters,
configure the packet time base as `1 / mdhd.timescale`, and open the decoder.
Require distinct gates for simultaneously selected roles and reject an invalid
or source-inconsistent media timescale before context allocation.

Treat context creation, parameter transfer, packet-time-base configuration,
and codec open as one fail-closed transaction. On the first rejection or
exception, dispose every partially created context, the source preparation,
and any gate not already consumed by a context. On success, the returned
session solely owns the opened contexts and their consumed gates. Keep the
native operations injectable; the production implementation delegates to the
existing reviewed FFmpeg context, transfer, time-base, and open boundaries.

## Consequences

The selected regular MP4 chain now reaches source-bound opened H.264 and AAC
decoder contexts with exact track time bases and deterministic cleanup. The
managed verification tests can exercise transaction ordering and ownership
without loading or invoking a native runtime.

## Non-goals

Runtime discovery, runtime-path selection, library loading, new symbol lookup,
packet construction or submission, decoder receive, frame transfer,
scheduling, delivery, rendering, application integration, fragmented MP4, and
physical execution are not included.
