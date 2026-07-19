# PCN-0102 Diagnostic Packet / Frame Transaction Validation

## Scope

Milestone 0.0.8j composes the bounded diagnostic H.264/AAC input with the existing native packet-send and frame-receive boundary for the two opened contexts produced by 0.0.8i.

It does not claim frame extraction, conversion, demux, playback output, production activation, redistribution, or license approval.

## Implemented evidence

- H.264 video, AAC audio, and non-empty payloads are required before operations;
- video send and one video receive attempt occur before audio operations;
- audio send and one audio receive attempt occur only after accepted video progress;
- packet send accepts only `Sent`;
- frame receive accepts only `FrameAvailable` or `NeedMoreInput`;
- every rejected or faulted path disposes the complete lease;
- successful bounded progress retains the lease;
- cleanup remains audio then video and is idempotent;
- only fixed receive states are returned;
- no native frame, pointer, packet bytes, symbol, path, or native error value is exposed;
- no packet loop, flush, demux, output delivery, or activation change was added.

## Validation plan

- Release build on Windows and Ubuntu;
- full test suite on Windows and Ubuntu;
- format verification on Windows and Ubuntu;
- focused tests for operation order, accepted receive states, rejection cleanup, exception cleanup, and codec validation.
