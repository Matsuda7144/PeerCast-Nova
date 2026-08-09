# PCN-0494: Exact ISO BMFF Regular Movie Windows Output Sink Adaptation

## Scope reviewed

- borrowed existing Windows output buffer owner with caller-explicit video and
  audio generations;
- exact canonical BGRA32 and interleaved PCM buffer projection without payload
  copying or lifetime extension;
- serialized callbacks, accepted-only per-role counts, cancellation
  propagation, and fail-closed output rejection;
- sink disposal that leaves the shared output lifecycle owned by its caller.

## Verification

- focused managed tests build activation, binding, initialization, and output
  boundaries entirely from inert fakes, then verify exact video/audio buffer
  fields, payload identity, generations, and first-audio start semantics;
- focused tests inject `WouldBlock`, verify it becomes terminal sink rejection,
  and prove disposing the sink does not dispose the borrowed output boundary;
- no system D3D11, WASAPI, runtime discovery, library loading, native
  invocation, output-device access, or physical execution is used.

## Result

Accepted as the exact regular-movie Windows output sink adapter defined by
ADR-0484. Timing admission, output reset/recovery orchestration, application
integration, and all physical output remain unimplemented.
