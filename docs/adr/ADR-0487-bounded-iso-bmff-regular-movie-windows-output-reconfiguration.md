# ADR-0487: Bounded ISO BMFF Regular Movie Windows Output Reconfiguration

## Status

Accepted as the caller-quiesced Windows output reconfiguration transaction
required before regular-movie timing generation can be committed.

## Decision

Retain the exact caller-supplied video binding, audio binding, video stream,
and audio stream configuration for one already bound and initialized Windows
output chain. Reconfigure both media generations in one finite ordered
operation:

1. reset audio and video output buffers;
2. reset audio and video initialized streams;
3. reset the shared resource binding;
4. bind the same formats and dimensions with the requested role generations;
5. initialize the same streams with those generations.

`Reset` and `DeviceLost` reset outcomes both mean that the relevant old native
state was released. Every other outcome rejects the requested configuration.
After any rejection or exception, repeat the complete sequence with the exact
previous configurations under a non-cancellable cleanup token. A successful
rollback leaves the owner ready for retry and does not increment its committed
count. A failed rollback permanently faults the owner.

The transaction borrows the existing activation, binding, stream, and buffer
lifetimes. It neither activates nor disposes them. Its caller must hold
exclusive playback quiescence for the whole operation; the transaction alone
does not prevent a separately retained sink from writing between layer resets.

## Consequences

Regular MP4 now has one bounded operation that restores cross-layer generation
coherence instead of resetting only the final buffer layer. Distinct video and
audio generations remain exact after binding and initialization, even though
the shared binding reset accepts only one provisional cleanup generation.

## Non-goals

Timing reset, decoded-frame reservation release, sink-generation commit,
top-level delivery serialization, device reactivation, native or physical
execution during verification, and application pumping are not included.
