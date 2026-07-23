# PCN-0162: Controlled four-frame audio validation review

## Scope

Review the minimal correction required for recognizable independent physical audio evidence after accepted known-tone and decoded-fixture 80 ms probes.

## Root boundary

The failed subjective observation did not identify a decoder, sample-format, WASAPI, endpoint, or ownership defect. It identified a finite harness that stopped after one valid ordinary audio delivery.

The reviewed correction therefore changes only controlled validation duration:

- audio decode selection is `AudioOnly`;
- four decoded audio frames are transferred through the existing one-shot completion ingress;
- four scheduler reservations are delivered through the existing generation-matched timing and output boundary;
- four native WASAPI writes are acknowledged;
- one Start is acknowledged;
- cleanup begins only after the fourth accepted delivery and the existing observation hold.

Video retains the previous single-delivery behavior and existing mixed-media production configuration.

## Fixed limits

- required audio deliveries: 4;
- expected fixture sample frames: `4 × 1024 = 4096`;
- expected duration at 48 kHz: approximately 85.3 ms;
- unchanged audio buffer duration: 100 ms / 4,800 sample frames;
- minimum audio decode-operation budget: 4;
- minimum audio delivery-operation budget: 4.

## Failure contract

The first non-successful boundary is terminal.

- WouldBlock is counted once and is not retried;
- no reservation is resubmitted;
- no decoded frame is repeated or reconstructed;
- cancellation stops the run and preserves production-first cleanup;
- stream end before four deliveries is `DeliveryRejected`;
- decode-budget exhaustion is `DecodeRejected`;
- native evidence inconsistent with four delivered writes is `EvidenceRejected`;
- device fallback and recreation remain prohibited.

## Ownership review

Each decoded completion is transferred once to the sole bounded scheduler. Each scheduler reservation is completed once after its corresponding output acknowledgement. This change adds no queue, coalesced payload owner, retained raw sample buffer, thread, timer, pump, or retry policy.

The existing scheduler remains bounded with capacity 16, the fixed owned-byte limit, `DropOldest` only for the controlled run, and audio initial preference. Audio-only decode prevents video reservations from interleaving with the four controlled audio deliveries.

## Test coverage

Focused tests require:

- video remains one decode and one delivery;
- audio completes only after four decode transfers and four deliveries;
- four-delivery evidence is accepted with 4,096 sample frames, 16,384 signed-16 bytes, and one Start;
- WouldBlock after one successful audio delivery terminates on the second attempt without retry;
- audio operation limits below four reject before runner start;
- controlled audio maps to `AudioOnly` while video retains `AudioAndVideo`;
- ordinary production defaults remain unchanged.

## Physical gate

This PR does not run physical output. After merge, execute the existing video-gated controlled audio helper exactly once from clean approved main. Acceptance requires:

- canonical `Completed / Accepted` and `AudioAccepted`;
- delivery attempts / delivered: `4 / 4`;
- retained WouldBlock: `0`;
- native audio write attempts / acknowledgements: `4 / 4`;
- accepted frames: `4096`;
- Start acknowledgements: `1`;
- no video Present;
- operator observation `Audio audible: Yes` for recognizable fixture sound.

Paired verification remains blocked until that result is retained.

## Retained post-EOS physical result

The post-EOS scheduler drain was exercised exactly once from clean main
`d3cf3a32c66bb5a8dc322e6b1e524bd807c518b4`.

The canonical record was `DeliveryRejected / None`, with fixture bytes / packets
`66,939 / 2`, decode operations `4`, delivery attempts / delivered `3 / 2`, and
retained WouldBlock `0`. The runner stopped, the lifecycle disposed, and no external
binary or payload was published. Borrowed-owner verification was not reached because
the run terminated before evidence capture.

The operator heard a noise-like sound, not recognizable retained-fixture audio.
Two successful 1,024-frame deliveries represent approximately 42.7 ms at 48 kHz,
which remains consistent with the earlier short-duration observation, but does not
identify the third fixed lifecycle result.

The result refutes the reviewed assumption that two additional scheduler-owned audio
frames would remain after decoder EOS. The third delivery was not `AudioDelivered`;
`RetainedWouldBlock` is excluded. `Empty` is plausible but is not retained evidence.

## Revised next gate

Do not repeat the ordinary helper or any diagnostic on `d3cf3a32...`.

The already implemented bounded delivery-result diagnostic must be used exactly once
from a later merged main commit, after final-head and post-merge CircleCI success,
with the unchanged runtime, fixture, video gate, limits, endpoint, timing, and buffer.
It must use new repository-external diagnostic and failure paths and preserve the
ordered sequence including the third post-EOS result.

No production change, retry, recovery, endpoint fallback, additional audio probe, or
paired verification is reviewed until that exact result is retained.
