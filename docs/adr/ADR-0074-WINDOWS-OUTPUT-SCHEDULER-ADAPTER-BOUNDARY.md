# ADR-0074: Windows Output Scheduler Adapter Boundary

## Status

Accepted for the 0.0.6y technical boundary only. FFmpeg backend activation,
bundling, and distribution remain unapproved, and license review remains
`Pending`. This Accepted status does not approve any of them.

## Context

0.0.6r owns a bounded mixed audio/video scheduler queue. 0.0.6x processes one
validated Windows video frame or audio buffer. Direct use of the existing
`RunOneAsync` cannot support output backpressure because it dequeues and
disposes an item before the sink result is known.

## Decision

`PlaybackWindowsOutputSchedulerAdapterBoundary.PumpOneAsync` reserves and
dispatches at most one scheduler-owned item. The production source seam uses an
internal scheduler reservation held under its existing run serialization gate.
The scheduler retains ownership throughout the reservation. Only
`VideoProcessed` or `AudioProcessed` acknowledges, removes, and disposes the
item. `WouldBlock`, initialization/binding requirements, resize, device loss,
format rejection, invalid items, cancellation, exceptions, and unknown results
release the reservation without consuming it. No result implicitly retries or
drops an item.

The adapter converts canonical `VideoBgra32`, `AudioSigned16`, and
`AudioFloat32` managed frames directly into the existing 0.0.6x buffer records.
It strictly validates media kind, format, known non-negative presentation
timestamp, sequence, discontinuity, dimensions/sample layout, plane count,
stride, byte length, and the adapter's current video/audio generation before
dispatch.

When both media kinds are ready, the first pump prefers video and each
successful dispatch makes the opposite kind the next preference. A missing
preferred kind falls back to the other kind. Retryable or failed dispatch does
not advance fairness state. This is deterministic fairness, not timestamp-based
A/V synchronization.

Reset forwards audio then video reset to the existing buffer boundary, updates
independent generations, and restores the initial preference. It does not
discard reserved or queued scheduler items.

Reset is transactional across that fixed order. If audio reset succeeds but
video reset returns failure, cancellation, or disposal, the adapter makes one
audio rollback attempt to the prior generation. Successful rollback preserves
the prior snapshot and the original fixed video result. Failed rollback commits
the known partial generations to the snapshot, enters `ResetFaulted`, and
returns `PartialResetFault`. Normal pumping then returns `ResetRequired` without
reserving or consuming an item. Only a later successful explicit reset restores
`Ready`; no retry loop or implicit item drop is introduced.
Dispose closes admission and waits for the one in-flight pump, but does not
dispose the scheduler, buffer processor, or activation resources. Cleanup
authority remains with their existing owners and repeated reset/dispose is
safe.

No native pointers, handles, or HRESULTs cross this adapter contract.
Non-Windows returns before calling the dispatcher.

## Consequences

0.0.6y implements an injectable production scheduler adapter and explicit
single-step drive. It does not implement an automatic loop, timer thread,
wall-clock sleep, A/V synchronization, drift correction, frame dropping,
default-device monitoring, GUI/message pumping, FFmpeg activation or codec
wiring, runtime distribution, licensing approval, or real-media playback.
