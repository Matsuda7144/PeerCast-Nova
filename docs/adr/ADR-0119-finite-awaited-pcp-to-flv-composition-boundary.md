# ADR-0119: Finite Awaited PCP-to-FLV Composition Boundary

Status: Accepted

## Context

ADR-0118 selected a finite composition from the awaited PCP packet-consumer callback through the one-event FLV parser facade and finite packet-submission state machine. The implementation must keep callback-scoped PCP payload memory valid without retaining it after callback completion, preserve parser-owned backpressure, and avoid introducing a second encoded queue or resident decode loop.

The existing PCP stream reader awaits `IPcpChannelPacketConsumer.ConsumeAsync` before releasing one packet and decoding the next top-level Atom. The existing FLV parser reports exact consumed bytes and retains only one bounded incomplete record. The existing submission state machine accepts exactly one source-owned packet or per-stream EOS completion and exposes bounded receive／retry transitions.

## Decision

Add `PlaybackPcpFlvCompositionBoundary` in `Nova.Playback` as a serialized implementation of `IPcpChannelPacketConsumer`.

The boundary:

- is constructed for one exact PCP channel ID, channel-session ID, and initial packet sequence;
- validates monotonic exact PCP packet sequence before parser work;
- borrows PCP payload memory only while the awaited callback remains incomplete;
- advances one callback-local suffix offset only by the parser's exact `ConsumedByteCount`;
- processes every parser completion before passing the remaining suffix back to the parser;
- permits one bounded parser carry for an incomplete record but stores no caller suffix and creates no second encoded queue;
- bounds parser operations and decoder advances independently for every callback or explicit finalization operation;
- creates one exact decoder generation only after `StreamDescriptionAvailable` through an injected `IPlaybackPcpFlvCompositionGenerationFactory`;
- requires that generation to reference the exact parser-created source session;
- owns the finite submission state machine and native decoder session as one generation while the parser continues to own the source session;
- sends one source-owned parser completion through `Begin`, then uses bounded `Advance` transitions only when decoder output is required;
- treats `NoReceiveProgress`, contradictory parser accounting, undefined results, identity or sequence mismatch, generation mismatch, rejected submission, and work-budget exhaustion as terminal fail-closed results;
- exposes fixed enum state, counters, generation state, and a fixed-message composition exception without payload, identifiers, endpoint text, or native error text;
- exposes `CompleteInputAsync` as the only EOS entry and emits video then audio EOS through the existing one-event parser facade;
- never infers EOS from an empty PCP packet, TCP closure, reconnect, stop, timeout, cancellation, or failure;
- disposes the old submission state machine and native session before applying an explicit parser discontinuity reset and creating a replacement source generation;
- does not reset parser or decoder generation for reconnect alone;
- disposes the submission state machine before the native session, then the parser, then the injected generation factory;
- makes terminal failure, cancellation, finalization, and disposal idempotent at the public ownership boundary.

`PlaybackPcpFlvCompositionGeneration` owns only the state machine and native session. It retains the exact parser-created source session as a non-owning identity reference.

The generation factory remains injected in 0.0.9i. Acquisition of verified ABI-gate leases, construction of a production generation, and backend activation remain later explicit milestones.

## Consequences

One live PCP packet can now pass through real FLV parsing, exact source generation validation, packet／flush submission, and finite decoder backpressure while the upstream callback remains the sole borrowed-memory lifetime and backpressure boundary.

The implementation adds no complete-packet queue, caller-suffix copy, background worker, native frame drain loop, decoded-frame ownership, scheduler ingress, Windows output activation, backend-factory wiring, runtime distribution, or audible／visible playback claim.

The next milestone is 0.0.9j Bounded Decode Iteration and Per-stream EOS Drain Review. It must define how accepted packet and flush transitions expose decoded frames and bounded drain progress without weakening the 0.0.9i callback and source-generation ownership contract.

## Validation

Thirteen focused cross-platform tests cover multi-record callback suffix handling, split parser carry, exact identity and sequence, generation creation, packet and flush `EAGAIN`, decoder no-progress, operation limits, explicit finalization, empty input and cancellation, discontinuity replacement, fixed terminal failure, and idempotent ordered cleanup.
