# ADR-0129: Policy-gated Production Decoder Generation Factory Boundary

## Status

Accepted for milestone 0.0.9s.

## Context

ADR-0128 reviewed how the completed 0.0.9q source-bound decode／scheduler-delivery generation can be constructed without weakening the existing provenance, runtime verification, native ownership, scheduler, timing, or Windows output boundaries.

The pinned external FFmpeg candidate still has `LicenseReview.Pending`. A production factory must therefore refuse the default candidate before observing a caller-supplied runtime path or performing native work. At the same time, the ownership transaction must be executable under injected approved conditions so every construction and rollback rule can be tested without changing production provenance.

The existing native context lease consumes its own ABI gate and verified runtime lease during disposal. Video and audio contexts therefore cannot share one runtime／gate ownership chain. The factory also must not own the FLV source session or application-wide Windows activation, binding, stream, COM, D3D11, WASAPI, or OS-handle resources.

## Decision

### One finite policy-gated factory

Add `PlaybackProductionDecoderGenerationFactory` as a serialized, cancellation-aware factory for one complete generation product. `CreateAsync` performs one finite construction attempt and returns fixed results only.

The public production operations use `PlaybackFfmpegBackendCandidate.Provenance`. Because that candidate remains `LicenseReview.Pending`, the normal factory returns `LicenseReviewPending` before parameter binding, runtime location, filesystem observation, ABI verification, or native resource creation.

An internal injected operations seam permits approved test provenance. Approval alone is insufficient: `PlaybackDecoderBackendPolicy` must also allow the candidate's third-party and native-code properties. Undefined provenance or rejection flags fail closed.

### Complete construction order

After policy acceptance, construction proceeds in this exact order:

1. validate the live source session and borrowed Windows output boundary;
2. create one exact FLV decoder-parameter binding;
3. locate and verify one video runtime lease from the explicit caller directory;
4. acquire one video ABI gate;
5. independently locate and verify one audio runtime lease from the same explicit directory;
6. reject shared runtime identity;
7. acquire one audio ABI gate and reject shared gate identity;
8. open one complete source-bound H.264／AAC native decoder session;
9. create the source-bound one-frame decode boundary;
10. create one fresh count／byte-bounded scheduler and scheduler policy;
11. create generation timing;
12. create the 0.0.9q generation-matched scheduler delivery boundary;
13. return one `PlaybackProductionDecoderGeneration` only after exact source and generation revalidation.

No partial binding, runtime, gate, context, scheduler, timing, or delivery product is returned.

### Runtime and native ownership

The production operations call the existing installed-runtime locator twice. Both acquisitions use the exact pinned inventory and the same explicit fixed-drive directory, but produce distinct runtime lease objects. There is no `PATH`, registry, adjacent-directory, network, download, extraction, fallback-version, or bundling behavior.

The native-session factory consumes the binding and both ABI gates. On success, each opened context owns its gate and runtime chain. The generation root then owns the native session through the source-bound decode boundary and 0.0.9q delivery boundary.

On rejection, cancellation, contradiction, undefined result, or exception, the factory releases only resources whose ownership has not already transferred. Cleanup is reverse ordered and reference-deduplicated, so deliberately injected shared identities are disposed at most once.

### Borrowed source and Windows output

`PlaybackProductionDecoderGeneration` retains source identity but never disposes the source session. The parser／PCP composition remains the source owner.

The generation borrows an existing `PlaybackWindowsOutputBufferProcessingBoundary`. It does not activate Windows output, bind devices, initialize streams, create a window, or dispose application-owned output resources. Disposal of the generation stops and disposes only its source-bound decode, scheduler, timing, and delivery root.

### Product API and cleanup

The generation product forwards finite decoded-completion transfer, one-item delivery, and generation reset to the 0.0.9q root. `StopAsync`, synchronous disposal, and asynchronous disposal are idempotent.

Factory creation is serialized. Factory disposal cancels an active creation and waits for its ownership transaction to finish before disposing factory synchronization state. Completed generation products are independently owned by the caller and are not disposed by the factory.

### Fixed results and validation

Fixed factory results distinguish pending license review, policy rejection, invalid configuration, disposed source／output, binding rejection, video／audio runtime and ABI rejection, ownership conflict, native-session rejection, generation rejection, cancellation, disposal, and terminal fault.

Options for scheduler capacity／byte budget／full policy, playback policy, one-frame managed-byte limit, and A/V timing tolerances are copied and bounded at factory construction.

A narrow file-scoped CA1822 rationale records that the pre-admission cancellation／disposal result helper intentionally remains separate from the state-mutating completion path.

## Rejected alternatives

- Changing `PlaybackFfmpegBackendCandidate.Provenance` from Pending inside this milestone.
- Registering the candidate in `PlaybackDecoderBackendFactory` before approval.
- Observing the runtime path before policy evaluation.
- Sharing one runtime lease or one ABI gate across video and audio contexts.
- Returning a partially constructed generation.
- Allowing fallback runtime search or automatic download／extraction.
- Owning or disposing the source parser session.
- Activating or owning application-wide Windows resources in the decoder factory.
- Adding a resident decode／delivery loop, second queue, or hidden retry.

## Consequences

- The repository now contains an ownership-complete production generation factory boundary.
- The default pinned candidate still cannot perform native construction because its review is Pending.
- Approved injected tests verify exact ordering, independent ownership chains, policy-first behavior, cancellation, output/source validation, complete-product-only success, and reverse cleanup.
- Ten focused cross-platform tests pass together with the full routine CircleCI gate.
- Normal backend registration, production PCP ingress composition, generation replacement orchestration, Windows activation, FFmpeg distribution, and audible／visible playback remain outside this milestone.

## Next

0.0.9t — Production Generation Ingress and Replacement Composition Review.
