# ADR-0066 Playback Policy and Clock Boundary

## Status

Accepted for 0.0.6q.

## Decision

Playback timing decisions are separated from decoding, 0.0.6p delivery, and the
sink. `IPlaybackMonotonicClock` supplies process-monotonic arrival timestamps;
the implementation uses `Stopwatch` and never reads wall-clock time.

Each pure managed policy input identifies audio/video, explicit known-or-unknown
timestamp and duration, sequence, discontinuity, and monotonic arrival time. The
fixed decisions are `DeliverNow`, finite `Wait`, `Drop`, `ResetClock`, and
`Reject`, accompanied only by a fixed reason and bounded wait value.

Audio or video master is selected through policy options rather than hard-coded.
The first timestamped master frame anchors presentation time to arrival time.
`DecoderReset` clears prior sequence/timestamp anchors before regression checks
and returns `ResetClock`. Unknown timestamp delivers immediately without clock
scheduling; unknown duration is accepted. Known duration must be positive and no
more than 10 seconds.

One lock protects all state. Rejected input does not advance state. No queue,
background task, timer, thread, delay, or sink call is introduced.

## Bounds

- default maximum wait: 250 ms; configurable hard limit: 2 s
- default maximum lateness: 500 ms; configurable hard limit: 5 s
- default maximum lead: 2 s; configurable hard limit: 10 s

Wait is capped at the configured maximum. Lateness beyond its bound drops. Lead
beyond its bound, monotonic/timestamp/sequence regression, invalid duration, and
arithmetic overflow fail closed as fixed rejection results.

## Non-goals

This milestone starts no playback scheduler and adds no queue, `Task.Delay`,
timer, sink integration, renderer, device, resampling, frame duplication, A/V
synchronization loop, demux, backend activation, or FFmpeg distribution.
