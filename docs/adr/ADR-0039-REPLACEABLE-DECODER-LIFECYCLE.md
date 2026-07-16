# ADR-0039: Replaceable Decoder Lifecycle

- Status: Accepted
- Date: 2026-07-14

## Context

Milestone 0.0.5b can classify a bounded encoded-media prefix, but selecting a
native decoder immediately would allow one backend API, ownership model, or
license to define the Playback architecture. The same prefix used for
classification also contains decoder-required header bytes and may span
multiple PCP packets.

Playback therefore needs a backend-neutral lifecycle before any real decoder
is introduced. The lifecycle must preserve the existing bounded-memory and
finite-stop guarantees even when a decoder ignores cancellation.

## Decision

Add a one-shot `PlaybackDecoderRunner` and small backend contract in
`Nova.Playback`.

- `IPlaybackDecoderFactory.Create` is synchronous and non-blocking. A null
  result means no installed backend supports the recognized container.
- Classification and decoder bootstrap share an independently owned transient
  buffer. Its default maximum is 64 KiB and its hard maximum remains 1 MiB.
- Once recognition succeeds, the complete retained bootstrap is submitted
  exactly once. Bytes from the recognizing packet beyond the bootstrap bound
  are submitted once as a direct packet remainder.
- Subsequent packets use the existing `PlaybackPacketLease` memory directly.
  An encoded input is valid only until the returned callback `ValueTask`
  completes.
- Decoder callbacks are sequential. Every explicit source or latency
  discontinuity invokes `ResetAsync` before the corresponding packet is
  decoded.
- Natural input completion invokes `CompleteAsync`, then disposes the decoder.
- Caller cancellation and owner stop use a finite configurable deadline. If a
  callback ignores cancellation, the runner returns after the deadline while
  the active callback retains at most one packet lease. Cleanup and decoder
  disposal continue only as observation of that already-running callback.
- Results contain fixed enums, counts, and a deferred-cleanup flag only. They
  do not contain media bytes, identifiers, metadata, positions, peer text,
  exception text, or backend-specific values.
- The decoded-frame representation and ownership contract are deliberately
  deferred until a backend comparison establishes the smallest safe common
  boundary.

## License boundary

The lifecycle interfaces, runner, bootstrap buffer, and test fakes are original
Apache-2.0 project code and contain no decoder implementation. A real decoder
must be added behind a separately documented backend assembly or operating
system adapter. Its source, binaries, build options, notices, redistribution
obligations, and runtime-loading boundary must be reviewed before it is made a
default dependency.

## Consequences

- FFmpeg, Media Foundation, GStreamer, or another backend can be evaluated
  without moving backend types into Network, Core, or UI.
- Classification bytes are not lost before decoder creation.
- Normal packets do not incur an additional runner-owned payload copy.
- Discontinuities cannot silently continue backend state from an earlier byte
  range.
- Finite owner shutdown is preserved even for a non-cooperative decoder, at
  the cost of explicitly observable deferred cleanup.
- Audible or visible playback, codec support, decoded-frame ownership, clocks,
  and A/V synchronization remain unclaimed.
