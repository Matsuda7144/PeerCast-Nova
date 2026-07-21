# PCN-0132: Decoded Completion Transfer and Scheduler Ingress Review

## Milestone

0.0.9n — Decoded Completion Transfer and Scheduler Ingress Review

## Reviewed boundaries

- `PlaybackSourceBoundDecodedFrameCompletion`
- `PlaybackPcpFlvAwaitedDecodeCompositionBoundary`
- `PlaybackDiagnosticManagedFrameSchedulerIngressTransaction`
- `BoundedPlaybackScheduler`
- generation-matched FFmpeg context lifetime

## Findings

1. `BoundedPlaybackScheduler.Enqueue` consumes the incoming frame on every result. Accepted frames become scheduler-owned; every rejected or dropped path disposes the frame. There is no safe rollback after enqueue.
2. The current decoded completion has no detach primitive, so scheduler transfer cannot be implemented without either double disposal or a second frame copy.
3. Video and audio decoded sequences are independent, while the scheduler enforces one monotonic cross-media sequence.
4. The scheduler owns queued frames but not the matching decoder-context lifetime needed by later FFmpeg conversion and delivery.
5. A discontinuity reset must dispose old queued and reserved output ownership before replacing the native decoder generation.

## Accepted contract

- Add one exact-active-completion, one-shot frame detach.
- Validate source, decoder generation, discontinuity generation, stream, media, sequence, managed byte count, frame liveness, and active ownership before detach.
- Reserve a generation-wide downstream sequence but commit it only after `Enqueued` or `EnqueuedAfterDroppingOldest`.
- Call scheduler enqueue once; never reconstruct or retry a consumed rejected frame.
- Keep stream-local decoded sequence as identity only.
- Mark the first accepted frame of a replacement generation with `DecoderReset`; use `None` thereafter.
- Keep one scheduler-generation owner and matching context leases alive until all scheduler frames and reservations are released.
- On discontinuity or terminal failure, close admission, settle／dispose active completion, dispose scheduler ownership, then dispose the decode boundary and native session.
- Keep direct awaited sink consumption and scheduler transfer mutually exclusive.

## Scope guard

This review changes documentation only. It does not add detach runtime code, scheduler ingress runtime code, production generation factory, ABI-gate acquisition, backend activation, output lifecycle, FFmpeg distribution, or audible／visible playback.

## Next

0.0.9o — One-shot Decoded Completion Transfer and Scheduler Ingress Boundary.