# PCN-0168: Generation Delivery Trace Snapshot Propagation Review

## Scope

Review the smallest compatibility-preserving path for exposing the already-implemented fixed four-slot generation delivery trace through production and lifecycle snapshots.

This review adds no command, helper, record codec, hold checkpoint, native call, scheduler operation, timing operation, retry, or physical output.

## Existing foundation

PR #132 added `PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot` to the generation delivery owner. The trace is opt-in, fixed to four observations, appended under the existing delivery completion synchronization, and empty by default.

PRs #133–#135 added the bounded WASAPI trace, controlled-only configuration transport, and output-buffer detection. Ordinary production remains trace-disabled.

## Propagation contract

The implementation is split into two source PRs.

### PR A: generation factory opt-in

Add one boolean option to `PlaybackProductionDecoderGenerationFactoryOptions`:

```text
CaptureDeliveryObservations
```

Default:

```text
False
```

The production generation factory passes this value directly to the existing `PlaybackGenerationMatchedSchedulerDeliveryBoundary` constructor.

Requirements:

- no new allocation when false;
- no change to scheduler, timing, decode, output, reset, cancellation, or cleanup behavior;
- the option is cloned with the existing factory options;
- ordinary production and every existing constructor remain false;
- the future controlled render-evidence command is the only caller allowed to set true.

### PR B: snapshot exposure

Add `DeliveryTrace` as an init-only property to:

1. `PlaybackProductionPcpFlvCompositionSnapshot`;
2. `PlaybackApprovedExternalRuntimeWindowsLifecycleSnapshot`.

The property type is the existing:

```text
PlaybackGenerationMatchedSchedulerDeliveryTraceSnapshot
```

The production composition reads the active generation snapshot once while building its own snapshot and copies only `generationSnapshot.DeliveryTrace`.

The lifecycle boundary copies only `productionSnapshot.DeliveryTrace` into its snapshot.

Requirements:

- preserve every existing positional record constructor;
- do not expose the full generation snapshot;
- do not retain disposed generation owners;
- return the default trace when no live generation exists or capture is disabled;
- snapshot reads must not mutate counters or append observations;
- do not add an outer delivery decorator;
- do not serialize the trace yet.

## Compatibility

Existing callers that construct either snapshot positionally remain source-compatible because the new value is init-only.

Existing equality changes only when a caller explicitly sets the new property. Default snapshots remain equal to their previous semantic value because `DeliveryTrace` is the default record struct.

No existing physical-run or delivery-diagnostic record schema changes in these PRs.

## Concurrency and ownership

The generation owner remains the only writer of delivery observations.

Production and lifecycle layers perform read-only value copies. They do not acquire ownership of the generation, scheduler reservation, decoded frame, native resource, or output buffer.

A snapshot may race with normal delivery completion only through the generation owner's existing synchronized `Snapshot` property. No second lock or collection is introduced between delivery calls.

## Focused tests

PR A must prove:

- factory options default to false;
- option cloning preserves true and false;
- production root creation forwards the exact value;
- false keeps the generation trace empty;
- true permits the existing generation owner to retain observations.

PR B must prove:

- production snapshot copies an injected generation trace exactly;
- lifecycle snapshot copies the production trace exactly;
- absent generation and disabled capture produce the default trace;
- repeated snapshot reads are stable and side-effect free;
- existing positional snapshot construction remains valid.

## Physical gate

Both implementation PRs execute no physical output.

Do not run the ordinary helper, prior delivery diagnostic, tone probe, decoded-slice probe, future render-evidence command, or paired verification.

A physical render-evidence run remains blocked until generation propagation, hold checkpoints, canonical record, managed capture, helper, final-head CI, post-merge CI, and separate one-run approval are complete.
