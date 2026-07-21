# ADR-0130: Production Generation Ingress and Replacement Composition Review

## Status

Accepted for milestone 0.0.9t.

## Context

ADR-0129 added `PlaybackProductionDecoderGenerationFactory` and one complete `PlaybackProductionDecoderGeneration` that owns source-bound decode, bounded scheduler, timing, and generation-matched output delivery while borrowing the parser source and application-owned Windows output boundary.

The existing `PlaybackPcpFlvAwaitedDecodeCompositionBoundary` cannot safely adopt that product by replacing only its generation factory. It owns a different `PlaybackPcpFlvAwaitedDecodeGeneration`, requires synchronous generation creation, directly accesses its decode boundary, and settles decoded completions through `IPlaybackPcpFlvDecodedCompletionSettlement`.

That settlement contract accepts `Consumed`, then revalidates and disposes the still-owned completion. Production `Transfer`, however, detaches the managed frame, disposes the completion, and transfers ownership into the scheduler. Post-transfer completion validation would therefore always contradict the ownership contract. The 0.0.9s product also does not yet forward encoded packet submission or one-frame receive operations needed by the parser path, and production replacement requires asynchronous generation stop before the parser disposes the borrowed source session.

## Decision

### Add a production-specific composition boundary

Implement a new production PCP／FLV generation composition in 0.0.9u. Keep `PlaybackPcpFlvAwaitedDecodeCompositionBoundary` unchanged as the consumed-settlement diagnostic and comparison boundary.

The production boundary owns:

- one `PlaybackOneEventFinalEosFlvParser`;
- one dedicated `PlaybackProductionDecoderGenerationFactory`;
- at most one active `PlaybackProductionDecoderGeneration`;
- fixed runtime-directory text copied from application configuration;
- one operation gate and stop source;
- bounded parser／decode operation accounting.

It borrows one application-owned `PlaybackWindowsOutputBufferProcessingBoundary`. It does not own Windows activation, binding, stream initialization, COM, D3D11, WASAPI, window, or OS-handle lifetimes.

### Forward finite encoded ingress through the production product

Extend the production generation root with internal finite forwarding operations rather than exposing a raw decode boundary or constructing another decoder generation:

- observe the exact decode snapshot;
- begin exactly one source-owned parser completion submission;
- receive exactly one decoded result in ordinary, pending-submission-relief, or flush-drain mode with the existing bounded budget;
- transfer exactly one decoded completion to the existing scheduler;
- deliver, reset, and stop through the existing 0.0.9s product API.

The delivery root remains the singular owner of the decode boundary and native session. Internal forwarding is non-owning and valid only while the product is live.

### Serialize generation mutation

`ConsumeAsync`, input finalization, ordinary receive, flush drain, decoded transfer, one-item delivery, generation reset, discontinuity replacement, and stop all serialize through one composition operation gate.

This makes `Transfer` concurrent with neither delivery nor replacement. `PlaybackDecodedCompletionSchedulerIngressResult.Cancelled` without caller or stop cancellation is therefore an ownership contradiction and terminal failure rather than a retry signal.

No public call owns an unbounded loop. Pending packet／flush relief may repeat only inside the existing parser callback and only under the configured decode-operation, decoded-frame, and managed-byte budgets.

### Transfer is ownership settlement

Before transfer, validate exact source identity, decoder and discontinuity generation, media kind, stream ID, stream-local sequence, managed byte count, frame liveness, and frame role.

Call production `Transfer` once.

- `Transferred`: scheduler ownership is committed. Do not access, validate, or dispose the completion afterward.
- `Cancelled` with caller／stop cancellation: terminate the operation and let generation cleanup release any still-attached completion.
- `Cancelled` without cancellation: fail closed.
- invalid completion, sequence exhaustion, enqueue rejection, generation fault, or disposal: terminal composition failure.

Do not reuse `IPlaybackPcpFlvDecodedCompletionSettlement` for production transfer. Its `Consumed` contract intentionally retains completion ownership and is incompatible with one-shot detach.

The existing scheduler ingress treats enqueue rejection as terminal. The production composition does not add capacity polling, a second queue, or hidden transfer retry. Applications must drive finite delivery frequently enough to remain within the configured scheduler bounds. Any explicitly configured accepted drop-oldest policy remains a scheduler decision, not a composition override.

### Keep delivery caller-driven and finite

Expose one serialized `DeliverOneAsync` that delegates to the active production generation and processes at most one scheduler reservation.

Map delivered audio／video and deliberate late-video drop as committed progress. Map empty and retained wait／backpressure／initialization／binding／resize／audio-recovery／clock-not-started states outward without consuming the reservation. Surface device-loss or timing-generation reset requirements and provide one explicit serialized reset operation.

`AlreadyRunning` is impossible under the composition gate and therefore terminal if observed. Invalid item, context loss, conversion failure, output failure, undefined result, product fault, or unexpected disposal are terminal.

Do not automatically deliver from a PCP packet callback and do not add a resident output pump. The application remains responsible for invoking finite delivery.

### Create only after an exact parser source exists

Await production generation creation only after `StreamDescriptionAvailable` and exact live parser source validation.

Pass the configured runtime-directory text, borrowed output boundary, checked decoder generation, and parser discontinuity generation to `CreateAsync`. Map pending license review, policy rejection, runtime／ABI rejection, native-session rejection, cancellation, and fault to fixed production composition results.

The pinned candidate remains `LicenseReview.Pending`. Default production composition therefore fails closed before runtime-directory observation or native work. Approved injected operations remain available only for focused tests.

### Stop old generation before parser discontinuity

For a discontinuity packet:

1. validate channel, session, and packet sequence before destructive work;
2. detach the old generation from new admission;
3. asynchronously stop it and await scheduler, decoded completion, decode boundary, and native-session cleanup;
4. only then call parser discontinuity reset, which disposes the old borrowed source session and increments parser generation;
5. parse the replacement FLV header and configuration;
6. create the new generation only after the replacement source description exists.

There is no overlapping generation, old-generation revival, or rollback. Once old-generation stop begins, caller cancellation cannot restore the old generation. Mandatory cleanup completes first; cancellation or replacement failure then leaves the composition terminal and disposes the parser source.

The packet sequence and accepted-byte counters commit only after the entire discontinuity payload is processed successfully. Failure after destructive replacement is terminal rather than partially committed.

### Keep Windows lifecycle external

Generation creation requires only a live borrowed output boundary, not generation-matched application binding or stream initialization.

A replacement generation may therefore exist before the application has rebound or initialized output for its new generation and stream configuration. Finite delivery retains the reservation and reports initialization, binding, resize, clock, or reset requirements. The composition does not activate devices or reconfigure the application-owned output environment.

The future composition snapshot copies decoder／discontinuity generation and parser-derived video／audio configuration needed by the application coordinator. It does not transfer parser source ownership or expose native handles.

### Asynchronous terminal cleanup

Add `StopAsync` as the authoritative cleanup path:

1. close new admission and cancel the active operation;
2. await the composition operation gate;
3. stop and dispose the active production generation;
4. dispose the parser and its source session;
5. dispose the dedicated production factory;
6. leave the borrowed Windows output boundary untouched.

Terminal parser, submission, transfer, delivery, replacement, undefined-result, and ownership contradictions use the same ordered cleanup and preserve one fixed terminal result. Synchronous disposal may bridge to `StopAsync`; asynchronous disposal awaits it directly.

## Rejected alternatives

- Replacing only `IPlaybackPcpFlvAwaitedDecodeGenerationFactory` in the existing consumed-settlement composition.
- Treating scheduler transfer as `Consumed` and revalidating the detached completion.
- Constructing a second decode boundary around the 0.0.9s product.
- Exposing the raw decode boundary, native session, context, gate, runtime lease, or output handles.
- Resetting the parser before the old generation has stopped using its source session.
- Keeping old and new generations alive concurrently.
- Reviving or rolling back to the old generation after destructive replacement begins.
- Automatically activating, binding, initializing, or disposing Windows output resources.
- Automatically delivering inside the PCP callback or adding a resident pump.
- Adding scheduler-capacity polling, hidden transfer retry, fallback runtime, or a second decoded queue.

## Consequences

- The exact incompatibilities between consumed settlement and production scheduler transfer are documented before runtime composition changes.
- 0.0.9u can add one production-specific boundary without weakening the existing diagnostic composition or duplicating decoder ownership.
- Replacement will preserve source lifetime until old native ownership is fully gone.
- Output backpressure and application lifecycle requirements remain explicit finite results.
- The unchanged pinned candidate still prevents default native creation.
- Runtime source and tests remain unchanged in this review milestone.

## Next

0.0.9u — Finite Production Generation Ingress and Replacement Composition Boundary.
