# PCN-0163: Controlled audio retained timing resume review

## Scope

Review and implement the minimal controlled-run correction after the one approved four-frame physical audio execution stopped on its second outer delivery.

## Retained physical evidence

The clean-main run at `63f8be0cc5663ccda0f82bd5eee1c5a3dd3201d2` retained these facts:

- canonical result `DeliveryRejected / None`;
- decode operations `3`;
- outer delivery attempts `2`;
- delivered `1`;
- retained WouldBlock `0`;
- runner stopped and lifecycle disposed;
- operator observation `Audio audible: No`, noise-like only.

The command is not repeated. The later PowerShell `else` parsing error occurred after record display and has no bearing on the physical result.

## Root boundary

The first decoded audio frame establishes the production presentation anchor and is delivered immediately. The next retained-fixture AAC frame is approximately `21.333 ms` later in source time. Because the controlled finite composition invokes the next delivery immediately, the unchanged production timing boundary returns `RetainedWait` and preserves the scheduler reservation.

The system controlled operations previously returned that retained result directly. The outer physical-run composition accepts only the expected delivered result and therefore translated the timing retention to `DeliveryRejected`.

This explains all retained counters without assuming a second native write:

- first outer delivery: one acknowledged audio delivery;
- second outer delivery: timing retention before output;
- no native WouldBlock evidence;
- no evidence-capture or borrowed-owner verification stage reached.

## Reviewed correction

Only `SystemPlaybackControlledWindowsPhysicalRunOperations` resumes controlled audio timing retention.

1. Call the existing lifecycle `DeliverOneAsync`.
2. If the result is not `RetainedWait`, return it unchanged.
3. Wait one upward-rounded `1024 / 48000` cadence.
4. Call the same lifecycle delivery again against the retained scheduler reservation.
5. Repeat at most once more if timing is still early.
6. Return the resulting fixed lifecycle result unchanged.

Video continues to call the lifecycle exactly once. The generic production lifecycle, scheduler delivery boundary, timing tolerances, presentation anchor, output buffer, and record schema are unchanged.

## Ownership and retry review

The resumed operation retains one existing reservation. It does not:

- decode another frame;
- transfer another completion;
- enqueue or reserve another scheduler item;
- reconstruct or repeat payload bytes;
- call WASAPI before the timing decision reaches `ProcessNow`;
- retry `WouldBlock` or output failure;
- create a queue, pump, timer owner, fallback, or recovery policy.

The delay is cancellation-aware and bounded. A third consecutive `RetainedWait` leaves the outer controlled run terminal rather than becoming an unbounded playback loop.

## Counter review

The physical composition counts one outer delivery attempt per decoded frame. Internal timing resumes are not native output attempts and do not increment the canonical delivery counter. A successful four-frame run therefore remains expected to report:

- delivery attempts `4`;
- delivered `4`;
- native audio write attempts / acknowledgements `4 / 4`;
- accepted sample frames `4096`;
- accepted bytes `16384`;
- Start acknowledgements `1`;
- retained WouldBlock `0`.

## Test coverage

Focused injected tests require:

- `RetainedWait -> AudioDelivered` performs one bounded delay and two lifecycle calls;
- `RetainedWait -> RetainedWouldBlock` returns WouldBlock without another delay or call;
- persistent `RetainedWait` stops after the fixed two-resume bound;
- normal production defaults remain video-first, mixed-media, and `DropIncoming`;
- controlled video remains mixed-media and receives no timing-resume wrapper;
- controlled audio remains `AudioOnly`, audio-first, and bounded `DropOldest`.

## Physical gate

This review and implementation execute no physical output. The failed record and negative operator observation remain retained. A new physical audio run is not authorized until this branch passes the routine final-head merge gate and the merged main commit is separately approved for one exact execution.
