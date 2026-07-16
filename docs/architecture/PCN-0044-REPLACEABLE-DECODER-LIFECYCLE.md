# PCN-0044 Replaceable Decoder Lifecycle

## Purpose

Milestone 0.0.5c inserts a backend-neutral decoder ownership boundary after
bounded media classification. It does not bundle a codec library, produce a
decoded frame, or render audio/video.

```text
PcpPlaybackLatencyBuffer
  -> PlaybackPacketLease
     -> bounded classifier/bootstrap
        -> synchronous IPlaybackDecoderFactory
           -> one IPlaybackDecoder lifetime
              -> future decoded-frame boundary
```

Only `Nova.Playback` knows the decoder contract. Network owns transport and
packet production; Core and UI remain decoder-independent.

## Bootstrap handoff

The classifier and bootstrap use the same maximum byte bound, 64 KiB by
default. Before recognition, packet bytes are copied into the transient
bootstrap. A discontinuity clears both classification and bootstrap state
before the new candidate is observed.

After recognition:

1. create the decoder synchronously from fixed container evidence;
2. submit the retained bootstrap exactly once;
3. if the recognizing packet extends beyond the bootstrap capacity, submit
   that uncopied remainder exactly once while retaining its lease;
4. clear and return the bootstrap only after the callback using it completes;
5. submit later packet leases directly, without another payload copy.

The packet sequence and stream position are callback-scoped operational data.
They are not included in the terminal result or normal diagnostics.

## Callback and reset order

All callbacks are serialized by the runner.

```text
normal packet:        Decode(packet)
discontinuity packet: Reset() -> Decode(packet)
natural completion:   Complete() -> Dispose()
```

A reset is required for both source and latency discontinuities. The packet
that carries the discontinuity is decoded only after reset succeeds.

## Finite stop and ownership

Caller cancellation and owner stop cancel the active callback, then wait for
the configured stop deadline. The default is two seconds; accepted values are
10 milliseconds through 30 seconds.

If the callback completes, the runner disposes its active memory and decoder
normally. If it ignores cancellation:

- the runner returns a fixed owner-stopped result, or propagates caller
  cancellation;
- `CleanupDeferred` is true;
- only the active operation remains alive;
- at most one packet lease is retained by that operation;
- the lease/bootstrap is released in its `finally` block when the callback
  actually completes;
- decoder disposal follows that completion and exceptions are observed but not
  copied into diagnostics.

`CompleteAsync` and `DisposeAsync` have the same finite deadline behavior.

## Result surface

`PlaybackDecoderRunResult` contains only:

- fixed end reason;
- fixed classification state, container, and evidence basis;
- submitted input and byte counts;
- reset count;
- deferred-cleanup boolean.

No payload, prefix, hash, identifier, metadata, endpoint, stream position,
backend name, free-form error, or exception is returned.

## Deferred boundary

- real decoder backend selection and packaging;
- codec discovery and negotiation;
- decoded audio/video frame format and ownership;
- frame queues and backpressure;
- audio clock, video scheduling, and A/V synchronization;
- controlled audible/visible playback verification.
