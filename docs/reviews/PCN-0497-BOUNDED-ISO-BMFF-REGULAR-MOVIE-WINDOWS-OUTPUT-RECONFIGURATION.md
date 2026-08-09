# PCN-0497: Bounded ISO BMFF Regular Movie Windows Output Reconfiguration

## Scope reviewed

- exact retained binding and stream configuration provenance;
- audio-before-video buffer and stream reset ordering;
- shared binding reset followed by exact role-generation rebind;
- stream reinitialization before configuration commit;
- complete previous-configuration rollback and permanent rollback fault;
- borrowed output ownership and caller-exclusive quiescence.

## Verification

- an inert fake Windows output chain is reconfigured from video/audio
  generations `7/11` to `13/17` across binding, stream, and buffer snapshots;
- one canonical video write after reconfiguration reaches the fake processor
  with generation `13`;
- an injected final initialization rejection performs all seven requested
  steps and all seven exact previous-configuration rollback steps;
- an injected rollback rejection produces a permanent `RollbackFaulted` state;
- no system output device, actual D3D11, actual WASAPI, native runtime,
  readiness capture, diagnostic path, or physical execution is used.

## Result

Accepted as the caller-quiesced Windows output reconfiguration transaction in
ADR-0487. Atomic delivery quiescence, timing reset, and sink/delivery generation
commit remain the next managed-only slice.
