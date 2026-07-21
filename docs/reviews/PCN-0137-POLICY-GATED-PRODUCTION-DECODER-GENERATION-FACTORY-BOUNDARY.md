# PCN-0137: Policy-gated Production Decoder Generation Factory Boundary

## Milestone

0.0.9s

## Validation target

Validate one finite production factory transaction from backend provenance and policy through exact FLV binding, two independent verified runtime／ABI chains, source-bound native session, one-frame decode, bounded scheduler, timing, and 0.0.9q generation-matched output delivery.

## Required behavior

- Evaluate candidate provenance and backend policy before binding, runtime-directory observation, ABI loading, or native work.
- Preserve the pinned candidate's `LicenseReview.Pending` state and return `LicenseReviewPending` through the public production operations.
- Permit construction only under injected Approved provenance plus an allowing backend policy.
- Reject null／disposed source, disposed borrowed output, negative generations, invalid options, undefined provenance, and undefined stage results fail closed.
- Create the exact FLV parameter binding before runtime acquisition.
- Acquire video runtime and gate before independently acquiring audio runtime and gate.
- Reject reference-equal runtime or gate ownership.
- Return only one complete generation product after native session and root identity revalidation.
- Keep source and Windows output resources borrowed and live after generation disposal.
- Cancel and await active creation during factory disposal.
- Add no fallback runtime search, download, bundling, second queue, resident loop, or hidden retry.

## Ownership assertions

### Success

`source owner → borrowed source session`

`generation → delivery → ingress → scheduler frames → decode boundary → native session → audio context → audio gate → audio runtime`

`generation → delivery → ingress → scheduler frames → decode boundary → native session → video context → video gate → video runtime`

`application owner → Windows activation／binding／stream／buffer boundary ← borrowed by generation`

The caller owns the returned `PlaybackProductionDecoderGeneration`. The factory does not retain or dispose completed products.

### Partial failure

Before native-session success, cleanup releases audio gate／runtime, video gate／runtime, and binding in reverse ownership order. Reference-equal injected identities are disposed only once.

After native-session success, both runtime chains are owned through their contexts. Root construction failure disposes the native session and both chains. Root success transfers all generation ownership to the returned product.

## Focused tests

`PlaybackProductionDecoderGenerationFactoryTests` covers:

1. Pending review stops before binding or runtime observation.
2. Backend policy rejection stops before binding or runtime observation.
3. Approved construction uses exact order, distinct runtime／gate identities, one complete product, and borrowed source／output resources.
4. Shared runtime identity is rejected before audio gate and native creation.
5. Shared gate identity is rejected and each runtime is released once.
6. Video runtime rejection releases only the already-owned binding.
7. Native-session rejection consumes binding／gates and releases both runtime chains.
8. Root rejection disposes the complete native chain and returns no partial product.
9. Pre-cancelled creation performs no binding or runtime work.
10. Disposed output is rejected before parameter or runtime ownership.

## Diagnostic history

The initial routine CircleCI run failed during analyzer execution. Temporary compile/analyzer status contexts proved runtime and test compiler success and isolated one `CA1822` warning in the pre-admission fixed-result helper. A narrow file-scoped rationale was added because that helper intentionally avoids the state mutation performed by the admitted `Finish` path.

A subsequent routine run exposed focused fixture expectation failures. Per-test status contexts showed that only assertions comparing construction calls were affected: resource cleanup had been written into the same log. The fixture was corrected to keep operation order and disposal counts separate. All ten focused tests then passed.

All temporary diagnostic CircleCI configurations were removed. `.circleci/config.yml` was restored byte-for-byte to the routine main configuration before milestone validation.

## Routine validation

The implementation head passed:

- `dotnet restore PeerCast-Nova.sln`
- `dotnet build PeerCast-Nova.sln --configuration Release --no-restore`
- `dotnet test PeerCast-Nova.sln --configuration Release --no-build --no-restore`
- `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`

The final documentation head must pass the same routine gate before Ready for Review and squash merge.

## Scope guard

This milestone does not:

- approve or mutate the pinned FFmpeg candidate provenance;
- register an FFmpeg backend in the normal backend factory;
- bypass the Pending license gate;
- download, extract, bundle, or redistribute FFmpeg;
- create or own a source parser／PCP composition;
- activate, bind, initialize, or own Windows device resources;
- add production replacement orchestration, continuous pumping, or automatic recovery;
- claim audible or visible playback.

## Next milestone

0.0.9t — Production Generation Ingress and Replacement Composition Review.
