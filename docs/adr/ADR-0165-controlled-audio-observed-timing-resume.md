# ADR-0165: Controlled audio observed timing resume

- Status: Proposed
- Milestone: 0.0.10d Phase 7
- Date: 2026-07-29

## Context

The separately authorized run at exact main
`e0809e9b711d5bb5101130a26ca19944a94a0d31` executed once through the
readiness-required entrypoint. It retained a canonical terminal record with:

- result `DeliveryRejected / None`;
- decode operations `4`;
- outer delivery attempts / delivered `3 / 2`;
- two acknowledged native audio writes;
- `2048` accepted sample frames and `16384` accepted bytes;
- no native WouldBlock or device loss;
- terminal record SHA-256
  `859651D2DED3417F4F55BBAD1E210D1B48AC9E274681D669D6CF1C516E851C78`;
- separate operator observation `Audio audible: Yes` for a recognizable beep.

The third audio reservation first returned `RetainedWait` with a requested wait
of approximately `6 ms`. The controlled timing wrapper instead delayed by the
fixed upward-rounded AAC cadence, approximately `21.3334 ms`. Windows timer
overshoot then moved the same reservation beyond the default `20 ms` audio-late
tolerance, producing `RetainedAudioRecoveryRequired`.

The four-slot delivery trace also recorded each timing resume as another
observation. A successful retained wait followed by delivery could therefore
consume two slots for one scheduler sequence even though the completed record
requires four unique delivered audio sequences.

## Decision

Keep production timing, late policy, scheduler ownership, fixture timestamps,
native output, buffer size, and the two-resume bound unchanged.

For the opt-in controlled audio evidence path:

1. read the latest bounded retained-wait observation from the existing
   generation delivery trace;
2. use its positive wait duration when it does not exceed the fixed AAC cadence;
3. otherwise fall back to the existing fixed cadence;
4. coalesce later observations for the same scheduler sequence into the same
   trace slot;
5. retain at most four unique audio scheduler sequences.

The trace remains opt-in. Normal production does not capture it and keeps the
existing fallback cadence behavior.

## Safety boundary

- no new decode, transfer, enqueue, reservation, native write, or output retry;
- only `RetainedWait` is resumed;
- WouldBlock, device loss, output failure, cancellation, and every other result
  remain terminal to the controlled wrapper;
- malformed, overflowed, non-audio, non-wait, non-positive, or over-cadence
  observations use the existing fixed fallback;
- the delay remains cancellation-aware and bounded to two resumes;
- same-sequence coalescing rejects inconsistent media kind or timestamp identity
  by marking the trace overflowed.

## Evidence gate

Cross-platform tests must establish:

- exact bounded observed wait selection;
- fail-closed fallback for untrusted trace data;
- same-sequence retained-wait to delivery coalescing;
- four distinct audio deliveries without overflow;
- unchanged fifth-distinct-sequence overflow;
- unchanged WouldBlock and fixed resume-limit behavior.

This ADR and its implementation do not authorize readiness capture or physical
execution. A later physical run requires a new exact merged main, fresh records,
separate readiness authorization, and separate one-run physical authorization.
