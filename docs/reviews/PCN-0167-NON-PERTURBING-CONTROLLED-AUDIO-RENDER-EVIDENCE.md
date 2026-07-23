# PCN-0167: Non-perturbing controlled audio render evidence review

## Scope

Review the smallest evidence boundary that can distinguish the contradictory controlled physical-audio results without repeating either command or changing production behavior.

## Retained evidence

The ordinary helper on clean main `d3cf3a32c66bb5a8dc322e6b1e524bd807c518b4` retained `DeliveryRejected / None`, decode operations `4`, delivery attempts / delivered `3 / 2`, retained WouldBlock `0`, and a noise-like but unrecognizable sound.

The exact-result diagnostic on clean main `fcab3837722efb716decbc175e1dee678c939d9b` retained `Completed / Accepted`, decode operations `4`, delivery attempts / delivered `4 / 4`, four ordered `AudioDelivered` results, retained WouldBlock `0`, complete cleanup, preserved borrowed owners, and no audible sound.

Neither command may be repeated. The historical third result from the ordinary helper is not recoverable.

## Review finding

The existing exact-result decorator is logically transparent but not temporally invisible. It awaits the inner delivery and then acquires a separate lock and appends to a list before the caller can continue. In a finite decode and post-EOS drain harness, that continuation can change the interval before the next decoder or scheduler operation.

The existing aggregate native evidence also ends at `ReleaseBuffer` and `Start` acknowledgement. It does not retain the capacity and padding values already read by each write, or the queued padding immediately before controlled cleanup. Consequently accepted counters cannot distinguish endpoint consumption from accepted-but-unrendered submission.

## Reviewed design

### Generation delivery trace

Add a fixed four-slot trace to `PlaybackGenerationMatchedSchedulerDeliveryBoundary`.

- allocate no unbounded collection;
- append only while the existing delivery owner holds its synchronization during completion;
- retain scheduler sequence and source timestamp offset, not wall-clock time;
- retain the exact timing result and final generation-delivery result;
- retain reservation completion as retained, delivered, or dropped;
- mark overflow instead of replacing an earlier observation.

The trace is exposed through existing generation, production, and lifecycle snapshots. The controlled diagnostic command reads it after the run. No outer operations decorator is used.

### Native write trace

Extend the system WASAPI calls with one optional fixed observer.

- record the capacity and padding values already returned by `GetBufferSize` and `GetCurrentPadding`;
- derive available frames with checked unsigned accounting;
- record requested frames and bytes, Start request, GetBuffer acknowledgement, ReleaseBuffer acknowledgement, Start acknowledgement, and normalized result;
- record at most four writes;
- make the observer null by default so ordinary production does not allocate or retain diagnostic state.

A controlled-only option permits one `GetCurrentPadding` query immediately before the existing Stop call. It is a single read-only checkpoint, not polling or a wait strategy.

### Composition checkpoints

The controlled composition captures:

1. after required deliveries and before the existing observation hold;
2. after the hold and before cleanup;
3. pre-Stop padding through the native observer.

These are snapshots of already-owned counters and bounded observations. They perform no decode, transfer, scheduler reservation, delivery, write, retry, or endpoint change.

### Canonical record

Add a separate schema for render evidence. The record:

- accepts audio only;
- requires the approved runtime and fixture identities;
- serializes exactly four fixed scheduler/timing slots and four fixed native-write slots;
- includes hold-start, pre-cleanup, and pre-Stop summaries;
- rejects overflow, inconsistent counts, negative values, impossible capacity/padding relationships, or non-audio observations;
- states `Audio-Acceptance-Claimed: False` and `External-Binary-Or-Payload-Published: False`.

The managed child capture promotes only a complete canonical record or a sanitized fixed failure record. Raw stdout and stderr remain process-local.

## Test coverage

Focused tests require:

- generation observations preserve four sequences, timestamp offsets, timing results, final results, and completion modes in order;
- no observation is appended by snapshot reads;
- overflow is fail-closed;
- default production options create no native render observer;
- four successful injected writes retain exact capacity, padding, available, requested frames / bytes, ReleaseBuffer, and Start state;
- WouldBlock and malformed padding remain distinct and bounded;
- the optional pre-Stop query executes once only when controlled render evidence is enabled;
- hold-start and pre-cleanup snapshots do not invoke operations;
- the new command uses direct system operations rather than the old delivery decorator;
- codec parsing rejects paths, payload-like fields, trailing slots, overflow, and count contradictions;
- existing physical-run and delivery-diagnostic schemas remain byte-compatible.

## Physical gate

The review and implementation PRs execute no physical output. The retained `d3cf3a32...` and `fcab383...` commands are never repeated.

After implementation merges and both final-head and post-merge CircleCI succeed, a later one-run command may be separately approved with unchanged runtime, fixture, endpoint policy, buffer, timing, bounds, and observation hold, plus new repository-external record paths. Paired verification remains blocked.
