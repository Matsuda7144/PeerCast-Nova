# PCN-0139: Finite Production Generation Ingress and Replacement Composition Boundary

## Milestone

0.0.9u

## Validation target

Validate one finite production owner from awaited PCP packet input through bounded FLV parsing, exact source-bound packet／flush submission, one-frame native receive, one-shot scheduler transfer, caller-driven one-item delivery, explicit reset, and stop-old-before-parser-reset discontinuity replacement.

## Implemented boundary

`PlaybackProductionPcpFlvCompositionBoundary` is a dedicated production composition. The existing `PlaybackPcpFlvAwaitedDecodeCompositionBoundary` remains unchanged for diagnostic and consumed-settlement comparison.

The boundary owns one parser, one production creator, at most one complete production generation, and one serialized operation／stop lifecycle. It borrows the Windows output buffer-processing boundary.

## Ownership contract

### Normal generation

`application → Windows activation／binding／stream／buffer boundary ← borrowed by composition and generation`

`composition → parser → source session`

`composition → production generation → delivery → scheduler ingress → scheduler frames`

`production generation → source-bound decode → native H.264／AAC contexts → independent ABI gates／runtime leases`

The composition never owns a second native or scheduler lifetime.

### Completion transfer

Before transfer, composition validates exact source, generation, stream, media, source sequence, frame role, and managed bytes.

On `Transferred`:

`decoded completion → frame detached → scheduler ownership`

`decoded completion → disposed by ingress → decode backpressure released`

After the result returns, composition performs no further access or disposal of that completion.

### Replacement

`validate packet → detach old admission → await old StopAsync → parser discontinuity reset／old source disposal → new description → new generation → packet commit`

There is no old／new overlap, revival, rollback, or partial commit.

## Internal forwarding assertion

The public production generation surface remains limited to transfer, delivery, reset, stop, identity, and snapshots.

One assembly-internal forwarding bridge reaches the singular generation-owned decode boundary for exact snapshot, submission, and one-frame receive. The concrete bridge uses narrowly scoped `UnsafeAccessor` field access and revalidates product, root, delivery, source, and generation liveness before every operation. Test roots implement the same internal forwarding interface directly.

No raw native pointer, context, gate, runtime lease, decoder owner, or Windows resource is exposed.

## Operation serialization

One operation gate covers:

1. packet consumption;
2. explicit encoded-input finalization;
3. one-frame ordinary receive;
4. one-frame flush drain;
5. decoded completion transfer;
6. one-item delivery;
7. explicit generation reset;
8. discontinuity replacement;
9. terminal cleanup;
10. stop and disposal.

No resident loop or parallel generation operation is permitted.

## Result assertions

### Transfer

- `Transferred`: commit transfer count and never touch completion again.
- `Cancelled`: preserve ownership when cancellation happened before detach; terminal cleanup resolves it.
- `EnqueueRejected`: terminal scheduler-admission failure, no polling or retry.
- invalid／exhausted／faulted／disposed／undefined: terminal transfer failure.

### Delivery

- video／audio delivered: commit one delivered count.
- deliberate video drop: commit one dropped count.
- wait／would-block／initialization／binding／resize／audio recovery／clock／reset required: retain the reservation and return nonterminal.
- invalid context／conversion／output／undefined／fault／disposed: terminal delivery failure.

### Creation

Factory results are mapped without weakening the 0.0.9s policy gate. The unchanged pinned candidate maps to `LicenseReviewPending` and terminal cleanup before native generation creation.

## Focused tests

`PlaybackProductionPcpFlvCompositionBoundaryTests` covers:

1. Pending candidate fails after exact stream description without accepting the packet.
2. Bootstrap plus interleaved video／audio packets creates one exact generation and submits both events.
3. Retained packet relief transfers one frame before exactly one retained retry.
4. Ordinary receive transfers one frame outside the PCP callback.
5. Caller-driven delivery retains an item through `WouldBlock` and then commits one video delivery.
6. Scheduler admission rejection is terminal and performs no retry.
7. Explicit finalization accepts independent video／audio flushes and independent drains reach decoder EOF.
8. Discontinuity fully stops the old generation before replacement generation creation.
9. Cancellation after destructive old-generation stop never revives the old generation or commits the replacement packet.
10. Stop disposes generation and creator ownership while leaving the borrowed output boundary live.

The tests use the real bounded FLV parser, real finite submission state, real one-frame decoded completion, and real one-shot detach semantics. Only production generation creation, scheduler storage／delivery results, and output behavior are injected.

## Analyzer history

The first runtime source compiled successfully with analyzers disabled. SARIF status probes then isolated three intentional diagnostics:

- `CA1068`: the forwarding signature preserves the existing one-frame decode contract, where a reusable cumulative budget follows cancellation;
- `CA1822`: fixed terminal exception construction remains beside the composition lifecycle rather than a detached utility;
- `CA2250`: cancellation can arrive from subordinate fixed results or the linked stop token and must be translated only after terminal cleanup.

Each diagnostic is documented and disabled only for the relevant new source file in `.editorconfig`. Temporary CircleCI diagnostic configurations were removed and the routine configuration was restored byte-for-byte.

## Routine validation

The implementation and focused-test head passed:

- `dotnet restore PeerCast-Nova.sln`
- `dotnet build PeerCast-Nova.sln --configuration Release --no-restore`
- `dotnet test PeerCast-Nova.sln --configuration Release --no-build --no-restore`
- `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`

The final documentation head must pass the same routine gate before Ready for Review and squash merge.

## Scope guard

This milestone does not:

- approve or mutate the pinned FFmpeg candidate;
- register the candidate in the normal backend factory;
- bundle, download, extract, or redistribute FFmpeg;
- activate or recreate Windows devices;
- add a resident playback pump or automatic recovery loop;
- add a second encoded or decoded queue;
- implement MP4／fragmented MP4;
- claim audible or visible playback.

## Next milestone

0.0.9v — FFmpeg Candidate License, Backend Registration, and Distribution Review.
