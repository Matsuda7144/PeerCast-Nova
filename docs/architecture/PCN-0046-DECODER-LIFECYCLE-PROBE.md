# PCN-0046 Decoder Lifecycle Probe Architecture

## Scope

Milestone 0.0.5d adds a controlled diagnostic composition around the existing
0.0.5c decoder runner. It adds no real decoder and does not alter Network,
Core, or playback-input payload ownership.

```text
official PeerCastStation
        |
        v
PcpChannelSession
        |
        v
PcpPlaybackLatencyBuffer
        |
        v
PlaybackDecoderRunner
        |
        v
CLI-internal counting decoder
        |
        +-- fixed counters only
```

## Stop ownership

The probe races five bounded observations:

- configured decoder-input count;
- runner completion;
- session completion;
- independent wall-clock duration;
- caller cancellation.

When the input count, duration, caller cancellation, or runner completes first,
the probe requests session stop. Input-limit, duration, and caller paths also
request owner stop on the runner. The session and runner are then awaited
separately. Pending packets discarded only during final shutdown are not
reported as active latency skips; active queue overflow remains visible.

## Evidence boundary

The counting decoder records:

- callback input count;
- callback byte count;
- reset count;
- complete count;
- dispose count.

The acceptance gate compares these values with the runner-owned submitted and
reset counters. The decoder does not branch on container bytes and does not
retain callback memory.

## Privacy boundary

The terminal summary contains only fixed classification/lifecycle/session enums,
integer counters, an optional HTTP status code, and booleans. It contains no
free-form error text or values derived from the media content.
