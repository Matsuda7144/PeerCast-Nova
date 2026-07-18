# ADR-0078: Late Item Disposition Policy Boundary

## Status

Accepted for the 0.0.7c technical boundary only. FFmpeg backend activation,
bundling, and distribution remain unapproved, and license review remains
`Pending`. This Accepted status does not approve any of them.

## Context

0.0.7a classifies one item as `VideoLate` or `AudioLate`, and 0.0.7b retains
that scheduler reservation without consumption. A separate policy boundary is
needed before any caller may nominate a video item for dropping or request an
audio recovery action. The policy must consume the existing timing result
without recomputing timestamps or playback position.

## Decision

`PlaybackLateItemDispositionPolicyBoundary` evaluates one existing late timing
result together with media kind, discontinuity, sequence, and media-specific
generation. It accepts only the exact pairs `Video`/`VideoLate` and
`Audio`/`AudioLate`; all other timing-result/media combinations fail closed.
It never owns or consumes a scheduler reservation or frame.

Video late policy is explicitly configured as either `Retain` or
`MarkDropCandidate`. Audio late policy is explicitly configured as either
`Retain` or `RequireRecovery`. The resulting `VideoDropCandidate` and
`AudioRecoveryRequired` values are recommendations only. No frame is dropped,
no reservation is acknowledged, and no audio sample is resampled or stretched.
The default policy retains both media kinds.

Undefined enums, negative sequence, and negative generation fail closed.
Generation mismatch or any defined non-`None` discontinuity latches
`ResetRequired`. Video and audio generations remain independent and change only
through explicit reset. Disposal is idempotent and owns no lower resource.

## Consequences

0.0.7c fixes the policy vocabulary and provides a direct bridge from the
existing 0.0.7a/0.0.7b late results while deliberately leaving all effects
unimplemented. Drop acknowledgement, keyframe awareness, repeated late-item
accounting, audio resampling/stretch, drift correction, precise timers,
complete A/V synchronization, FFmpeg activation/demux/codec wiring,
distribution/licensing approval, and real-media playback remain pending.
