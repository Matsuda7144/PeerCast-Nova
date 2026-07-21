# PCN-0136: Production Decoder Generation Factory and Activation Review

## Milestone

0.0.9r — Production Decoder Generation Factory and Activation Review

## Reviewed boundaries

- `PlaybackFfmpegBackendCandidate`
- `PlaybackDecoderBackendPolicy`
- `PlaybackDecoderBackendFactory`
- `PlaybackDecoderBackendInstalledRuntimeLocator`
- `VerifiedPlaybackDecoderRuntimeLease`
- `PlaybackDecoderFfmpegAbiSymbolGate`
- `PlaybackDecoderFfmpegAbiSymbolGateLease`
- `PlaybackBoundedFlvDecoderParameterBindingFactory`
- `PlaybackBoundedFlvNativeDecoderSessionFactory`
- `PlaybackDecoderFfmpegContextLease`
- `PlaybackSourceBoundOneFrameDecodeBoundary`
- `BoundedPlaybackScheduler`
- `PlaybackAvTimingDecisionBoundary`
- `PlaybackGenerationMatchedSchedulerDeliveryBoundary`
- Windows output activation／binding／stream-initialization／buffer-processing boundaries

## Findings

1. The complete finite decode-to-output generation exists after 0.0.9q, but no production factory creates it from a live FLV source and verified runtime ownership.
2. The pinned FFmpeg candidate remains `LicenseReview.Pending`. The backend policy rejects non-approved provenance, so the current production result must be `LicenseReviewPending` before runtime-path or native observation.
3. The installed-runtime locator verifies only one caller-supplied fixed-drive directory against the exact five-file inventory. It intentionally performs no `PATH`, registry, conventional-location, or network search.
4. An ABI gate lease owns loaded modules and symbols while retaining its parent runtime lease.
5. A context lease disposes its context, ABI gate, and parent runtime lease. Sharing one runtime lease between video and audio would let the first context cleanup invalidate the second.
6. The source-bound native-session factory already rejects a shared gate and consumes binding plus both distinct gate owners on every result.
7. Production creation therefore requires two independent runtime→gate→context chains, even though both chains verify the same directory and inventory.
8. The FLV source session remains parser／composition-owned. Parameter leases are copied and consumed by native creation; source identity is borrowed.
9. The 0.0.9q generation owns scheduler, decode, native session, contexts, timing, and finite output admission, but borrows the Windows buffer processor.
10. Windows activation owns COM, D3D11, WASAPI, and OS handles and enforces owning-thread borrows. Binding and stream initialization must stay with the application／window lifecycle rather than decoder generations.
11. `InitializationRequired`, `BindingRequired`, `ResizeRequired`, and device loss are explicit outer-owner actions, not permission for hidden activation or device recreation.
12. `PlaybackDecoderBackendFactory` is a generic provenance-audited selector. Registering an FFmpeg backend while the candidate is pending would contradict the existing legal gate.

## Selected 0.0.9s shape

Add one policy-gated production generation factory that:

- evaluates candidate provenance before observing the runtime directory;
- returns fixed pending／rejected results without native work;
- accepts one explicit host-installed runtime directory and exact pinned inventory;
- creates one exact FLV parameter binding;
- independently verifies a video runtime lease and an audio runtime lease;
- independently acquires distinct video and audio ABI gate leases;
- passes the binding and gates once to the source-bound native-session factory;
- constructs one source-bound decode boundary, bounded scheduler, generation timing boundary, and 0.0.9q delivery boundary;
- returns only a complete owning generation product;
- borrows but never disposes the application-owned Windows buffer processor;
- unwinds every partial failure in reverse ownership order;
- performs no retry, search fallback, download, extraction, resident work, or second queue.

The default production path remains legally unavailable while the pinned candidate review is pending. Focused tests may inject approved provenance and factory operations to validate ownership, but production metadata is not mutated by tests or implementation.

## Required factory results

The implementation should expose fixed outcomes covering at least:

- `Created`;
- `LicenseReviewPending`;
- `PolicyRejected`;
- `InvalidConfiguration`;
- `SourceDisposed`;
- `RuntimeRejected`;
- `VideoGateRejected`;
- `AudioGateRejected`;
- `ParameterBindingRejected`;
- `NativeSessionRejected`;
- `GenerationRejected`;
- `Cancelled`;
- `Faulted`.

Undefined subordinate outcomes and contradictory result／owner pairs are terminal and must not produce a generation.

## Construction and ownership order

1. policy／provenance gate;
2. source, generation, options, and borrowed-output validation;
3. exact managed FLV parameter binding;
4. video runtime verification;
5. video ABI gate acquisition;
6. independent audio runtime verification;
7. independent audio ABI gate acquisition;
8. source-bound H.264／AAC native-session opening;
9. source-bound one-frame decode boundary;
10. fresh bounded scheduler;
11. generation-scoped timing boundary;
12. 0.0.9q generation-matched delivery boundary;
13. return one owning generation product.

Ownership transfers are explicit. Once the native-session factory is called, it consumes the binding and both gates on every result. Once 0.0.9q construction succeeds, it is the generation's cleanup root.

## Failure and cleanup rules

- No partial product is observable.
- Before native-session transfer, dispose unconsumed audio gate/runtime, video gate/runtime, and binding in reverse acquisition order.
- After native-session success, decode／generation construction failure disposes scheduler or timing if created, then decode/native ownership.
- Never dispose a runtime lease twice after a context has consumed its chain.
- Never dispose the borrowed FLV source or Windows output processor.
- Cancellation before a transfer commits no downstream owner; cancellation after a synchronous transferred call is translated only after deterministic cleanup.
- Cleanup exceptions do not change a fixed failure result or expose filesystem paths, native pointers, handles, or HRESULTs.

## Replacement rule

A later production composition owner replaces generations as:

1. block new upstream input and delivery;
2. `StopAsync` the old generation and await active reservation release;
3. dispose all old scheduler and decoder-native ownership;
4. retain the application Windows environment;
5. create the new generation at scheduler sequence zero with new generation identities.

There is no overlapping old/new native decoder lifetime and no rollback to the old generation if new creation fails.

## Activation and distribution guard

- Do not update `PlaybackFfmpegBackendCandidate.Provenance.LicenseReview`.
- Do not register an FFmpeg backend in `PlaybackDecoderBackendFactory`.
- Do not add bundled binaries, download code, archive extraction, installer behavior, runtime discovery, or redistribution claims.
- Keep the host-installed exact pinned runtime policy.
- A later explicit approval review must address license notices, distribution obligations, source availability obligations, packaging, and backend registration.

## Scope guard

This milestone changes documentation only. It does not implement the factory, approve the candidate, observe a runtime directory, load native modules, create production contexts, activate Windows devices, connect the normal backend factory, start a pump, distribute FFmpeg, or claim audible／visible playback.

## Next

0.0.9s — Policy-gated Production Decoder Generation Factory Boundary.
