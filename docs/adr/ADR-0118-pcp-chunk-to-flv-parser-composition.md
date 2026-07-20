# ADR-0118: PCP Chunk-to-FLV Parser Composition

Status: Accepted

## Context

0.0.9g provides a caller-pushed FLV parser facade that emits at most one source-owned packet or per-stream EOS completion per operation. 0.0.9e accepts exactly one such completion and retains it across decoder `EAGAIN` while bounded frame receive progress is attempted.

The existing PCP data plane has a compatible but strict lifetime model. `PcpChannelStreamReader` decodes one top-level Atom, constructs one `PcpChannelPacket`, awaits `IPcpChannelPacketConsumer.ConsumeAsync`, and disposes the frame containing the packet payload only after that callback completes. The payload must not be retained after callback completion. A consumer exception becomes a terminal `PcpChannelConsumerException`, while cancellation propagates through the same awaited boundary.

One PCP payload may contain more than one FLV record. The FLV parser may therefore return one completion after consuming only a prefix of the callback payload. Returning from the callback at that point would invalidate the unconsumed suffix. Copying the callback payload into the older queued playback-input path would create another encoded owner and weaken the exact parser-completion backpressure model.

The packet-consumer interface also has no normal-completion callback. Transport loss, reconnect, stop, cancellation, and graceful media finalization must not be collapsed into an inferred EOS signal.

## Decision

Select one future finite `PlaybackPcpFlvCompositionBoundary` in `Nova.Playback`. It will implement `IPcpChannelPacketConsumer` and expose a separate explicit finalization operation for its owner.

### Callback ownership and suffix handling

For one `ConsumeAsync` callback:

- require the exact configured PCP channel ID and channel-session ID;
- require the expected monotonic PCP packet sequence;
- treat `PcpChannelPacket.Payload` as borrowed callback memory only;
- retain no reference to the packet or payload after `ConsumeAsync` completes;
- keep the callback incomplete until every payload byte is consumed by the parser or copied into the parser's bounded incomplete-record carry;
- maintain one local offset and advance it by exactly `PlaybackBoundedFlvParserOperation.ConsumedByteCount`;
- reject negative, excessive, contradictory, or zero-progress accounting fail closed;
- after a parser completion consumes only a prefix, process that completion before parsing `payload.Slice(offset)`;
- never copy the caller suffix into a composition-owned queue or return normally with an unconsumed suffix.

The awaited PCP callback itself is the upstream backpressure boundary. Keeping it incomplete preserves callback-memory lifetime and prevents the stream reader from decoding the next top-level Atom.

### Parser and decoder backpressure

The composition must serialize one callback or explicit finalization operation at a time.

When parsing produces one source-owned completion:

- do not call the parser again while that completion remains active;
- submit it through the exact source-bound 0.0.9e state machine;
- on immediate packet or flush acceptance, continue with the remaining caller suffix;
- on `ReceiveRequired`, keep the same callback open and invoke only bounded `Advance` transitions;
- permit another `Advance` only after the previous transition returned explicit frame progress and the submission remains pending;
- treat `NoReceiveProgress`, undefined results, operation-budget exhaustion, source mismatch, cancellation, or native failure as terminal composition failure;
- never complete the PCP callback successfully while the state machine retains a submission.

Every callback must have explicit parser-operation and decoder-advance limits beneath hard project limits. A parser step must consume bytes, produce one completion or stream description, or return a terminal result. No sleep, spin, resident parse loop, background task, hidden retry pump, or second encoded queue is allowed.

### Source-session creation and replacement

Before media submission, `StreamDescriptionAvailable` must establish one exact generation containing:

- the parser-created `PlaybackBoundedDemuxSession`;
- one matching FLV decoder-parameter binding;
- one source-bound opened native decoder session supplied through an injected, separately approved generation factory;
- one fresh one-event packet-submission state machine.

The composition must not submit media without that exact generation. It must not reuse a native decoder session or submission state machine across parser source-session replacement.

A `PcpChannelPacket.Discontinuity` is applied exactly once before the first parser call for that packet. Replacement order is:

1. close composition admission for the current operation;
2. release any retained state-machine submission;
3. dispose the old state machine;
4. dispose the old native decoder session;
5. invoke the parser facade with explicit discontinuity so it disposes the old demux session and resets carry, bootstrap, sequence, EOS, final, and generation state;
6. parse the new callback payload and create a new exact generation only after a new stream description becomes available.

A reconnect without an explicit packet discontinuity continues the same PCP consumer, packet sequence, parser generation, and decoder generation. Transport replacement alone is not a parser reset signal.

### Explicit finalization

`IPcpChannelPacketConsumer` does not define end-of-input. The composition therefore must not infer final input from an empty packet, TCP close, reconnect, timeout, stop, cancellation, or callback failure.

The owner may call the separate explicit finalization operation only after it has decided that the media source ended normally and no reconnect will occur. That operation:

- accepts no callback payload;
- invokes the one-event final-EOS parser facade explicitly;
- submits video and audio EOS one completion at a time through the same state machine;
- applies the same bounded receive and retry rules as packet submission;
- completes only after both per-stream flushes are accepted or decoder EOF is observed;
- treats cancellation, transport failure, owner stop, and composition failure as cleanup, not EOS.

### Failure and cleanup

Because `ConsumeAsync` has no result value, the future boundary must maintain a fixed enum-based terminal snapshot and surface a dedicated fixed-message consumer exception on terminal failure. The network layer may wrap it as `PcpChannelConsumerException`; payload, identifiers, positions, endpoints, and exception text must not enter the public snapshot.

Normal disposal and every terminal path must be idempotent and ordered:

1. stop admission and cancel active finite work;
2. dispose the state machine so retained native packet ownership is released before its parser completion;
3. dispose the native decoder session, which releases audio before video;
4. dispose the parser facade, which releases any active completion, demux session, carry, and codec configuration;
5. release composition-owned generation-factory resources.

The composition does not own or dispose `PcpChannelSession`, `PcpChannelPacket`, or its callback payload.

## Consequences

0.0.9h selects a zero-suffix-copy, awaited composition model. PCP callback lifetime, parser active-completion ownership, and decoder `EAGAIN` ownership form one continuous backpressure chain. Consumed-byte accounting is exact and caller suffix never escapes its callback.

The older queued playback-input path is not used for this source-bound decoder composition because it would add another encoded copy and cannot preserve the exact parser completion as the sole pending encoded owner.

The next milestone is 0.0.9i Finite Awaited PCP-to-FLV Composition Boundary. It will implement this reviewed contract with injected generation creation and focused cross-platform tests. Production FFmpeg activation, decoded-frame delivery, continuous drain, Windows output activation, runtime distribution, and audible／visible playback remain unchanged.

## Validation

The review is grounded in the existing awaited PCP consumer lifetime, one-top-level-Atom stream reader, stable reconnecting channel session, explicit discontinuity metadata, bounded FLV parser facade, exact source-bound native decoder session, and finite one-event submission state machine. This milestone changes documentation only. The final branch head must pass CircleCI restore, Release build, the full test suite, and format verification.