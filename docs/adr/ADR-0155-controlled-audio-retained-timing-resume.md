# ADR-0155: Controlled audio retained timing resume

- Status: Accepted
- Milestone: 0.0.10d Phase 5
- Date: 2026-07-23

## Context

The separately approved four-frame physical audio run executed exactly once from clean main `63f8be0cc5663ccda0f82bd5eee1c5a3dd3201d2` with the approved external runtime and retained fixture.

The run produced:

- `Result: DeliveryRejected`;
- `Evidence: None`;
- decode operations: `3`;
- delivery attempts: `2`;
- delivered: `1`;
- retained WouldBlock: `0`;
- stopped runner and disposed lifecycle;
- operator observation `Audio audible: No`, with only a noise-like transient.

No rerun, alternate endpoint, wider buffer, additional probe, or paired verification was performed.

The first audio frame was delivered. The second decoded frame retained the existing scheduler reservation because its presentation timestamp was approximately one AAC frame cadence, `1024 / 48000` seconds, ahead of the monotonic playback position. The production timing boundary correctly returned `RetainedWait`. The finite controlled runner treated every result other than `AudioDelivered`, `VideoDelivered`, and cancellation as terminal `DeliveryRejected`.

The defect is therefore not a WASAPI write rejection and not a production timing-policy defect. It is a controlled finite harness that did not resume a caller-owned retained timing reservation.

## Decision

Keep the ordinary production timing, scheduler, one-frame delivery, output, and caller-driven retention contracts unchanged.

For controlled physical audio operations only:

- when delivery returns `RetainedWait`, retain the same scheduler reservation;
- wait one exact retained-fixture AAC frame cadence, rounded upward to a `TimeSpan` tick;
- resume the same lifecycle delivery operation;
- allow at most two timing resumes for one outer controlled delivery operation;
- return immediately on `AudioDelivered`, `RetainedWouldBlock`, cancellation, device loss, reset, invalid item, output failure, or any other result;
- count the outer controlled delivery once, preserving the expected canonical `Delivery-Attempts: 4` for four acknowledged audio writes.

A timing resume is not an output retry. `RetainedWait` occurs before managed conversion reaches a native WASAPI write. No decoded frame is repeated, reconstructed, re-enqueued, resubmitted, copied, coalesced, or redelivered.

## Fixed bounds

- decoded sample frames per retained AAC frame: `1024`;
- sample rate: `48000 Hz`;
- cadence: approximately `21.3334 ms` after upward tick rounding;
- maximum timing resumes per outer delivery: `2`;
- required outer audio deliveries: `4`;
- unchanged audio buffer: `100 ms / 4800 sample frames`.

## Preserved terminal behavior

- `RetainedWouldBlock` remains caller-visible and terminal for the controlled run;
- no sleep or resume occurs for video;
- no delivery result other than `RetainedWait` is resumed;
- cancellation interrupts the bounded delay;
- a third consecutive `RetainedWait` is returned and remains terminal at the outer composition;
- no fallback, device recreation, endpoint change, resampling, amplification, synthesis, silence insertion, or timestamp mutation is introduced.

## Evidence and next gate

Cross-platform tests inject the delivery and delay boundaries to prove:

- one retained timing result resumes to `AudioDelivered`;
- a later `RetainedWouldBlock` is not resumed;
- repeated retained timing stops at the fixed resume bound.

This change performs no physical execution. The failed one-run evidence remains retained as the diagnosis basis. Any later physical audio execution requires a separately approved clean-main commit and remains exactly once with no retry.
