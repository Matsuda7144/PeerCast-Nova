# PCN-0105 Diagnostic Managed Frame Copy Transaction Validation

## Scope

Milestone 0.0.8m connects one bounded H.264 packet and one bounded AAC packet to the existing managed frame copy boundary. It retains only explicitly owned managed frames and the aggregate native decoder lease on success.

## Implemented evidence

- video send and one video copy attempt precede audio send and one audio copy attempt;
- `Copied` requires exactly one captured managed frame;
- `NeedMoreInput` requires no captured frame;
- successful completion owns copied video/audio frames and the same aggregate native lease;
- completion disposal releases audio frame, video frame, then aggregate native lease;
- rejected, contradictory, and faulted paths dispose any captured frames and the complete lease;
- invalid codecs and empty payloads are rejected before operations;
- no native pointer or callback-scoped view escapes;
- no conversion, output adaptation, sink delivery, packet loop, flush, demux, production activation, redistribution, or approval-state change is introduced.

## Verification

The final pull-request head must pass Windows and Ubuntu Release build, full tests, and format verification.
