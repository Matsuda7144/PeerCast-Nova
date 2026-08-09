# ADR-0422: Bounded ISO BMFF Fragment Sample Timing Projection

## Status

Accepted as a post-0.0.10e non-physical managed fragmented-MP4 foundation
slice.

## Decision

Project one resolved fragment run from one parsed `tfdt` base media decode time.
Accumulate each unsigned sample duration into exact arbitrary-precision decode
start and exclusive-end values, apply the preserved signed or unsigned
composition offset to obtain presentation time, and retain sample size and flags
in an immutable ordered projection. Preserve zero-duration samples without
advancing the decode cursor and expose the exact next decode time.

## Non-goals

Timescale or clock conversion, cross-fragment continuity, sequence ordering,
absolute data-offset calculation, byte-range or media-data lookup, fragment
traversal, streaming, decoder submission, native execution, rendering, or
physical execution are not included.
