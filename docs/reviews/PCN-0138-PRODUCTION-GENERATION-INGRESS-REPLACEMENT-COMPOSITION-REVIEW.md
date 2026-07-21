# PCN-0138: Production Generation Ingress and Replacement Composition Review

## Milestone

0.0.9t

## Validation target

Review the exact ownership and sequencing contract needed to connect `PlaybackProductionDecoderGeneration` to awaited PCP／FLV parsing, source-bound packet submission, one-frame decode, scheduler transfer, caller-driven delivery, asynchronous stop, and discontinuity replacement.

## Existing incompatibilities

The current awaited decode composition cannot directly receive the 0.0.9s product:

1. it owns `PlaybackPcpFlvAwaitedDecodeGeneration`, not `PlaybackProductionDecoderGeneration`;
2. its generation factory is synchronous while production creation is awaited and policy-gated;
3. it directly accesses a separately owned decode boundary;
4. its settlement returns `Consumed`, then validates and disposes a still-owned completion;
5. production `Transfer` detaches the frame and disposes the completion;
6. its replacement path disposes the generation synchronously before parser reset;
7. its public surface has no production one-item delivery or explicit output reset operation.

A transferred completion must never be revalidated or disposed by the composition after `Transferred`.

## Selected composition

Add one new production-specific composition in 0.0.9u. Preserve `PlaybackPcpFlvAwaitedDecodeCompositionBoundary` unchanged for consumed-settlement diagnostics and regression comparison.

The new composition owns one parser, one dedicated production factory, and at most one complete production generation. It borrows the application-owned Windows output boundary and copies, but does not inspect, the configured runtime-directory text until the factory policy gate admits creation.

## Required production product forwarding

Add internal, lifetime-checked forwarding through the existing production product／root:

- exact decode snapshot;
- one parser completion submission;
- one ordinary, pending-relief, or flush-drain receive using the existing budget;
- one decoded completion transfer;
- one scheduler delivery;
- one generation reset;
- asynchronous stop.

No raw decode boundary or native owner escapes. The 0.0.9s delivery root remains the sole owner of scheduler, decode, native contexts, ABI gates, and runtime leases.

## Serialized operation contract

One operation gate serializes:

- PCP packet consumption;
- final EOS submission;
- ordinary receive;
- flush drain;
- transfer;
- one-item delivery;
- reset;
- discontinuity replacement;
- terminal cleanup.

`Transfer` must therefore never overlap `DeliverOneAsync` or replacement. An unrequested `Cancelled` or `AlreadyRunning` result is a terminal contradiction.

## Decoded transfer contract

Before transfer, validate:

- exact live parser source identity;
- exact decoder and discontinuity generation;
- exact media kind and stream ID;
- expected stream-local sequence;
- positive bounded managed byte count;
- attached completion ownership;
- live managed frame and matching frame role.

Result handling:

| Transfer result | Composition behavior | Completion ownership |
|---|---|---|
| `Transferred` | commit transferred-frame count and continue | scheduler owns frame; do not touch completion |
| `Cancelled` with caller／stop cancellation | cancel operation and run ordered cleanup | generation cleanup releases attached ownership |
| `Cancelled` without cancellation | terminal contradiction | generation cleanup |
| `InvalidCompletion` | terminal transfer failure | ingress disposes／faults |
| `SequenceExhausted` | terminal transfer failure | ingress disposes／faults |
| `EnqueueRejected` | terminal scheduler saturation／policy failure | ingress disposes／faults |
| `Faulted`／`Disposed`／undefined | terminal generation failure | generation cleanup |

Do not map `Transferred` to the old `Consumed` settlement. Do not add a transfer retry loop or another decoded queue.

## Packet and decode flow

For one ordinary PCP packet:

1. acquire the composition gate;
2. validate channel／session／packet sequence;
3. parse one bounded FLV operation at a time;
4. create a generation only after exact `StreamDescriptionAvailable`;
5. submit one source-owned packet／EOS completion;
6. when native send requires relief, receive and transfer one frame, then perform only the existing bounded retry transition;
7. repeat relief only within configured operation／frame／byte budgets;
8. ensure no active completion or submission remains;
9. commit packet sequence and accepted bytes;
10. release the gate.

Ordinary receive and flush drain remain explicit one-frame caller operations. Each available frame is transferred exactly once before the operation returns.

## Caller-driven delivery contract

`DeliverOneAsync` runs outside the PCP callback and handles at most one reservation.

Committed progress:

- video delivered;
- audio delivered;
- deliberate late-video drop.

Nonterminal retained results:

- empty;
- timing wait;
- output `WouldBlock`;
- initialization required;
- binding required;
- resize required;
- audio recovery required;
- clock not started;
- reset required.

The application decides when to invoke delivery again, reconfigure its Windows output lifecycle, or invoke the explicit finite reset. The composition does not automatically pump or activate output.

Invalid item, context loss, conversion failure, output failure, product fault, unexpected disposal, undefined result, or `AlreadyRunning` are terminal.

## Scheduler saturation

The existing scheduler ingress rejects nonaccepted enqueue results terminally after one transfer attempt. The production composition therefore:

- does not wait for scheduler capacity while holding callback-scoped payload;
- does not detach and reconstruct a completion;
- does not own a scheduler-capacity signal or retry queue;
- preserves explicitly configured scheduler drop-oldest behavior only when the scheduler itself accepts it;
- treats rejected admission as a fixed terminal composition result.

Controlled application composition must call finite delivery frequently enough to remain within scheduler count and byte bounds.

## Generation creation

Creation occurs only after a live exact parser source description exists. Await:

`factory.CreateAsync(runtimeDirectory, source, borrowedOutput, decoderGeneration, parserGeneration, cancellationToken)`

Validate that a successful product references the exact source and generation and is live. No product is installed before validation.

Map pending review, policy rejection, invalid configuration, source／output unavailability, runtime／ABI rejection, ownership conflict, native-session rejection, generation rejection, cancellation, disposal, and fault to fixed composition results.

The unchanged pinned candidate keeps the default path at `LicenseReviewPending` before filesystem or native work.

## Discontinuity replacement order

For a validated discontinuity packet:

1. close old-generation admission;
2. detach old product from composition state;
3. await old `StopAsync`;
4. verify old product is disposed;
5. invoke parser discontinuity reset;
6. allow parser reset to dispose the old source session and increment parser generation;
7. parse replacement header and codec configuration;
8. await new generation creation only after the new source description;
9. install only the validated new product;
10. commit the packet only after the whole payload succeeds.

Once step 2 begins, replacement is destructive. Cancellation completes mandatory cleanup and leaves the composition terminal. There is no old-product revival, overlap, rollback, or partial packet commit.

The first transferred frame of a nonzero discontinuity generation retains the existing scheduler `DecoderReset` marker and 0.0.9q one-shot reset admission.

## Windows lifecycle boundary

The composition never owns or mutates:

- COM apartment;
- D3D11 device;
- WASAPI resource;
- window token or message loop;
- output activation;
- device binding;
- stream initialization;
- application output disposal.

The composition snapshot copies parser-derived video dimensions, audio rate／channels, decoder generation, and discontinuity generation for an application coordinator. Delivery may retain the first replacement frame until application binding／initialization matches the new generation.

Device loss is surfaced as reset required. Recovery remains one explicit application decision plus one finite generation reset; there is no automatic recovery loop.

## Terminal cleanup order

`StopAsync` and terminal failure use:

1. close admission and cancel active work;
2. await the composition gate;
3. stop／dispose active generation;
4. dispose parser and source session;
5. dispose dedicated production factory;
6. leave borrowed Windows output live.

Failure during cleanup must not replace the first fixed terminal result or revive admission.

## Documentation-only validation

0.0.9t changes ADR／PCN and synchronized progress documents only. Runtime source and tests remain unchanged. The final branch head must pass the routine CircleCI restore, Release analyzer build, full tests, and format verification.

## Scope guard

This review does not:

- change the pinned candidate from `LicenseReview.Pending`;
- register an FFmpeg backend;
- implement the new composition boundary;
- alter existing consumed-settlement composition behavior;
- activate or configure Windows output;
- add scheduler-capacity retry or a second queue;
- add continuous input／decode／delivery pumping;
- decide FFmpeg distribution;
- claim audible or visible playback.

## Next milestone

0.0.9u — Finite Production Generation Ingress and Replacement Composition Boundary.
