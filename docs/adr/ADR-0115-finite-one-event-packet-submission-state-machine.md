# ADR-0115: Finite One-event Packet Submission State Machine

Status: Accepted

## Context

0.0.9d added one exact-source metadata-aware packet／flush boundary. A successful operation consumes the active parser completion, while packet or null-flush `EAGAIN` retains the native packet or flush state together with that same parser completion.

The next layer must coordinate one retained event without creating another encoded queue, a resident decoder loop, or an implicit retry pump. It must also preserve per-stream sequence and EOS state rather than relying only on native return codes.

## Decision

Add one `PlaybackFiniteOneEventPacketSubmissionStateMachine` with separate finite `Begin` and `Advance` operations.

`Begin`:

- accepts exactly one active source-owned packet or per-stream EOS completion;
- requires exact source-session identity with the opened native decoder session;
- validates stream identity and a strictly increasing per-stream sequence before native submission;
- rejects new input while another submission is active;
- rejects packet or repeated EOS input after that stream's flush has been accepted or EOF observed;
- performs exactly one metadata-aware packet send or null flush;
- commits sequence and flush state only after native acceptance;
- retains the 0.0.9d completion unchanged when native send returns `EAGAIN`.

`Advance`:

- operates only on the one retained completion;
- performs at most one frame-receive attempt;
- performs no retry when that receive reports no progress;
- performs at most one retained packet or flush retry after explicit frame progress;
- returns explicit accepted, still-pending, EOF, no-progress, cancellation, rejection, and failure results;
- keeps retained ownership on repeated `EAGAIN`;
- releases the native packet before the parser completion on cancellation, terminal result, failure, or disposal.

The state machine tracks independent video and audio sequence, flush-accepted, and decoder-EOF state. It does not own the source session or native decoder session.

## Consequences

0.0.9e provides a bounded one-event submission coordinator. Every public transition contains at most one initial send, or one receive plus one retry. There is no hidden loop, sleep, spin, second encoded queue, or speculative consumption.

The existing frame-receive primitive is used only as bounded native progress. 0.0.9e does not expose decoded-frame ownership, deliver frames, perform continuous drain, connect PCP input, activate the production backend, distribute FFmpeg, or claim audible／visible playback.

A later milestone must validate this state machine with a retained controlled FLV fixture before broader parser composition or bounded decoded-frame delivery is attempted.

## Validation

Injected cross-platform tests cover immediate acceptance, retained packet and flush backpressure, parser blocking, one-receive／one-retry bounds, repeated `EAGAIN`, no-progress behavior, strict sequence commit, post-flush rejection, per-stream EOF, source mismatch, concurrent input, cancellation, native failure, and idempotent cleanup. CircleCI remains the routine merge gate.
