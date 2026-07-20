# PCN-0126: PCP Chunk-to-FLV Parser Composition Review

## Scope

Validate the architecture for a future finite connection from the awaited PCP channel packet callback to the 0.0.9g one-event FLV parser facade and the 0.0.9e one-event packet-submission state machine.

This milestone changes documentation only. It does not add an `IPcpChannelPacketConsumer` implementation, native-session generation factory, runtime PCP composition, decoder execution, decoded-frame delivery, backend activation, or output wiring.

## Reviewed evidence

### PCP callback lifetime

- `PcpChannelPacket` documents its payload as callback-scoped memory that must not be retained after consumer completion.
- `IPcpChannelPacketConsumer.ConsumeAsync` is awaited and explicitly defines that await as the upstream backpressure boundary.
- `PcpChannelStreamReader` decodes one top-level Atom at a time, awaits the packet consumer, and disposes the frame containing the payload only after the callback completes.
- The stream reader advances packet sequence and packet／byte counters only after consumer success.
- Callback cancellation propagates when the owning session token is cancelled.
- Other callback exceptions are wrapped as `PcpChannelConsumerException` and become a consumer failure rather than a retryable transport failure.
- `PcpChannelSession` keeps the same Core channel identity, local session identity, consumer, and packet sequence across bounded reconnect attempts.

### Parser and decoder lifetime

- `PlaybackOneEventFinalEosFlvParser` accepts caller-pushed memory and reports exact consumed bytes and bounded carry bytes.
- It returns at most one source-owned completion per operation and rejects another operation while that completion remains active.
- Its final input is explicit and emits video and audio EOS through separate exact-source completions.
- `PlaybackFiniteOneEventPacketSubmissionStateMachine` retains the original native packet or flush completion and parser completion when native send requires frame receive.
- Each `Advance` performs at most one receive and at most one retry after explicit frame progress.
- `NoReceiveProgress` leaves the submission active and therefore cannot be treated as successful callback completion.

## Accepted composition

The next implementation must add one serialized `Nova.Playback` boundary that implements `IPcpChannelPacketConsumer` and owns the current parser／decoder composition generation while leaving the network session and callback packet non-owned.

### No-copy caller suffix

The accepted path does not copy the PCP payload or its unconsumed suffix into another encoded queue.

- Keep `ConsumeAsync` incomplete while the callback payload is in use.
- Maintain a local offset only for the current callback.
- Advance by exactly the parser's `ConsumedByteCount`.
- Treat bytes copied into the parser's bounded incomplete-record carry as consumed.
- When one completion is returned before the payload ends, process that completion first and resume with the remaining callback slice only after the active-completion gate opens.
- Return successfully only when the entire callback payload is consumed and no parser or decoder completion remains active.

This uses the already-awaited PCP callback as the complete upstream backpressure boundary and creates no second encoded owner.

### Finite decoder pause

When submission returns `ReceiveRequired`, the callback remains open. The implementation may invoke only bounded state-machine `Advance` operations. It may continue after `FrameProgressAndStillPending`, but must terminate fail closed on no receive progress, cancellation, undefined results, source mismatch, native failure, or configured work-budget exhaustion.

No parser call and no new PCP payload may be admitted while a submission remains active.

### Generation creation

After the parser reports `StreamDescriptionAvailable`, one injected generation factory must create the exact matching decoder-parameter binding, opened native decoder session, and fresh submission state machine. Media may not be submitted before that generation exists. Factory failure consumes no later media and terminates the composition safely.

The future implementation must not activate the production FFmpeg backend implicitly. Injected cross-platform generation creation is required for routine tests, while production gate acquisition remains separately reviewed.

### Final signaling

The packet-consumer contract contains no end-of-input callback. Final input must therefore be a separate owner-invoked operation.

- Empty PCP payload is not EOS.
- TCP close, reconnect, timeout, stop, caller cancellation, consumer failure, and protocol failure are not parser final input.
- The owner may request finalization only after deciding that the source ended normally and no reconnect remains.
- Finalization drives the parser facade's video and audio EOS completions through the same bounded submission rules.

### Discontinuity and replacement

`PcpChannelPacket.Discontinuity` is the only packet-level parser reset signal. It is applied once before parsing that packet.

Before the parser resets and disposes its old source session, the composition must release the old state-machine submission, dispose the old state machine, and dispose the old native decoder session. A new decoder generation is created only after the reset parser produces a new stream description. Reconnect without explicit discontinuity preserves the current parser and decoder generation.

### Cleanup

Required terminal cleanup order is:

1. close admission and cancel finite work;
2. dispose the submission state machine, preserving native-packet-before-parser-completion cleanup;
3. dispose the native decoder session, preserving audio-before-video cleanup;
4. dispose the parser facade and its active completion, source session, carry, and configuration;
5. release generation-factory ownership.

The network session, callback packet, and callback payload are never disposed or retained by the composition.

## Rejected alternatives

- Returning from `ConsumeAsync` with an unconsumed caller suffix.
- Retaining `PcpChannelPacket` or its payload after callback completion.
- Copying each PCP payload into the older queued playback-input path.
- Adding a parser-suffix queue, second encoded packet queue, or detached completion.
- Parsing new bytes while the parser or decoder completion remains active.
- Treating `NoReceiveProgress` or operation-budget exhaustion as successful backpressure.
- Inferring EOS from transport termination or an empty packet.
- Resetting the parser source session before releasing the old native generation.
- Adding an unbounded parse／receive loop, delay, spin, or background pump.

## Required 0.0.9i validation

The implementation milestone must include focused cross-platform tests for:

1. exact PCP channel／session identity and monotonic packet sequence;
2. one callback containing multiple FLV records and exact suffix accounting;
3. a split FLV record copied only into the parser's bounded carry;
4. parser completion backpressure before the remaining callback suffix;
5. packet and flush `EAGAIN` with bounded frame progress;
6. no-progress and work-budget failure without callback-memory escape;
7. stream-description generation creation before media submission;
8. explicit finalization with video then audio EOS;
9. transport close, reconnect, stop, and cancellation not being inferred as EOS;
10. discontinuity replacement and no cross-generation source reuse;
11. callback exception propagation through the PCP consumer-failure path;
12. idempotent ordered cleanup.

## Deferred

- 0.0.9i runtime boundary implementation;
- real production ABI gate acquisition and backend activation;
- bounded decoded-frame drain and ownership;
- decoded scheduler ingress and output delivery;
- discontinuity-time `avcodec_flush_buffers` policy;
- MP4／fragmented MP4 parsing;
- runtime bundling／distribution;
- controlled audible／visible playback.

## CI

CircleCI remains the routine Linux merge gate. Because 0.0.9h is documentation-only, the final branch head must still pass restore, Release build, the full test suite, and format verification.

Status: Complete when ADR-0118, this review, and the progress documents are synchronized and the final CircleCI head passes.