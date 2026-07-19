# ADR-0085: Bounded audio recovery boundary

## Status
Accepted for 0.0.8a.

## Context
0.0.7h preserves `AudioRecoveryRequired` as an explicit continuous-pump stop result. Continuing playback without a bounded recovery contract would risk reusing partially reset output state or silently changing generations.

## Decision
Introduce `PlaybackAudioRecoveryBoundary` as a serialized, cancellation-aware recovery transaction.

The boundary:

- invokes an injected audio recovery effect with the current audio generation;
- permits one to sixteen attempts with an optional bounded retry delay;
- keeps every retry on the same audio generation;
- invokes one video/audio generation reset only after the effect reports recovery;
- exposes attempt count, recovery generation, media generations, and state;
- latches attempt exhaustion, explicit reset requests, and cancellation as `ResetRequired`;
- latches exceptions, undefined results, and reset failure as `Faulted`;
- cancels an active operation before explicit boundary reset or disposal.

## Ownership
The boundary does not own or dispose the injected recovery effect or generation reset implementation. It owns only its semaphore, operation cancellation source, counters, and state.

## Consequences
0.0.8a creates an effect seam and a strict transaction contract, but does not yet choose a platform audio recovery mechanism or automatically resume the 0.0.7h pump. A later boundary may compose pump stop, recovery, and restart while preserving the finite-run policy.

## Non-goals
No audio resampling, time stretching, drift correction, device enumeration, device migration, keyframe selection, frame duplication, native audio activation, or claim of complete A/V synchronization.
