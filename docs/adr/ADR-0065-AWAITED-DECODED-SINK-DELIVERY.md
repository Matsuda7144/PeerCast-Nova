# ADR-0065 Awaited Decoded Sink Delivery

## Status

Accepted for 0.0.6p.

## Decision

Canonical frames produced by the 0.0.6n adapter or 0.0.6o managed conversion
boundary are delivered by awaiting `IPlaybackDecodedFrameSink.WriteAudioAsync`
or `WriteVideoAsync`. One delivery boundary permits at most one active callback
and owns no producer/consumer queue. Calls that wait for the boundary therefore
receive sink backpressure directly and preserve invocation order.

The caller cancellation token is passed unchanged. Cancellation and sink
exceptions become fixed results and preserve the decoder context. Only an
unexpected integration failure disposes the context. Sequence and discontinuity
are forwarded deterministically.

Temporary payload remains valid through the awaited callback and is cleared in
every completion path. A sink that retains data must copy it before its callback
completes. The owned input managed frame is never consumed or disposed.

Disposal closes admission but deliberately does not dispose the private
`SemaphoreSlim`. An active callback can complete and release it safely; an
already-waiting delivery acquires it and returns the fixed integration-failed
result without touching the context. `SemaphoreSlim.AvailableWaitHandle` is
never used, so this leaves no kernel wait handle requiring disposal.

## Non-goals

This milestone adds no queue, buffering policy, A/V clock, synchronization,
drop/reorder policy, renderer, audio device, demux, backend activation, or FFmpeg
distribution.
