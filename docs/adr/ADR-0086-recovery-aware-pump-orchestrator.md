# ADR-0086: Recovery-aware pump orchestrator

## Status
Accepted for 0.0.8b.

## Context
0.0.7h stops the finite continuous pump with `AudioRecoveryRequired`. 0.0.8a owns a bounded audio-recovery transaction, but neither boundary owns the policy that decides whether playback may restart after recovery.

Without a separate owner, callers can accidentally restart without clearing the pump latch, recover indefinitely, change generations between attempts, or collapse recovery failure into a generic pump result.

## Decision
Add `PlaybackRecoveryAwarePumpOrchestrator` as a serialized owner around one pump boundary and one audio-recovery boundary.

The orchestrator:

- runs the finite pump with fixed video/audio generations;
- invokes audio recovery only after `AudioRecoveryRequired`;
- bounds the number of recovery transactions;
- keeps the same generations across pump, recovery, and restart;
- clears the pump latch through generation reset only after recovery succeeds;
- restarts the pump only after that reset succeeds;
- propagates non-recovery pump results explicitly;
- exposes running, recovering, stopping, reset-required, faulted, and disposed states;
- supports cancellation-aware stop and explicit reset.

Recovery-attempt exhaustion and recovery-side reset requests latch `ResetRequired`. Exceptions, undefined dependency results, and reset failures latch `Faulted`.

Explicit reset serializes recovery reset before pump generation reset. This order prevents a stale recovery latch from surviving into a newly reset pump run.

## Ownership
The orchestrator does not own or dispose the injected pump or recovery boundaries. It owns only its linked run cancellation source and orchestration state.

## Non-goals
This milestone does not:

- activate or migrate an audio device;
- select a new audio generation;
- resample or stretch audio;
- duplicate or select video frames;
- perform drift correction;
- implement precise deadlines;
- claim complete A/V synchronization;
- run unbounded recovery or playback loops.

## Consequences
The playback path now has one finite owner for `pump -> recover -> reset -> restart`. Device-specific recovery effects and real playback integration remain separate future work.
