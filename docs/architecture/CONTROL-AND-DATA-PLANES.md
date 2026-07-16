# Control Plane and Data Plane

## Overview

```text
                       CONTROL PLANE

UI / CLI
   |
   v
Nova.Core  ---- commands, lifecycle, status, faults ----> Nova.Network
   ^                                                        |
   |                                                        |
   +---------------- immutable status snapshots ------------+


                         DATA PLANE

Upstream TCP
   |
   v
PCP framing / stream session
   |
   +----> Relay sink      bounded queue; disconnect slow child
   |
   +----> Player sink     latency-oriented bounded buffer
   |
   +----> Recorder sink   no silent loss; explicit discontinuity/fault
```

## Rules

1. Media payload bytes do not travel through `NovaApplication`.
2. A generic event bus must not be used as a media transport.
3. Every consumer owns an explicit backpressure policy.
4. Buffers are finite and observable.
5. Cancellation and shutdown propagate from the owning stream session.
6. Discontinuities are data, not hidden log messages.
7. Status exposed to UI is immutable and rate-limited independently of payload flow.

## Current relay stream unit

`PcpRelayPacket` now exposes to one child callback:

- monotonically increasing sequence;
- byte position or protocol position when available;
- numeric packet type;
- callback-scoped payload memory;
- explicit discontinuity details when present.

It deliberately contains no channel, peer, or session identifier. Child
termination and queue status use a separate fixed-field snapshot. The bounded
downstream PCP writer and relay coordinator completed this path in 0.0.4.

## Current playback input unit

`PcpPlaybackLatencyBuffer` implements a separate player policy:

- one owned pooled copy of each accepted callback-scoped payload;
- independent packet and byte bounds;
- oldest-queued eviction to prefer recent data;
- explicit `Source` and `LatencySkip` discontinuity flags;
- one disposable payload lease for a single reader;
- fixed counters for queued, outstanding, received, dequeued, and skipped
  data;
- finite graceful completion and immediate owner stop.

This 0.0.5a boundary has no decoder or renderer. Container probing, media
decode, clocks, A/V synchronization, and actual output remain later Playback
slices.
