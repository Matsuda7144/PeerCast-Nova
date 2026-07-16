# ADR-0024: Unwrap PCP Positions and Report Backward Discontinuities

- Status: Accepted
- Decision: Expand each serialized 32-bit PCP packet position into a monotonic
  64-bit Core stream position using modulo-2^32 forward deltas.
- Decision: Treat a nonzero modulo delta below 2^31 as forward movement. A
  numerically smaller value in that range is a 32-bit wrap.
- Decision: Treat a modulo delta at or above 2^31 as backward or ambiguous.
  Report an explicit discontinuity without moving Core state or committing the
  observed wire position.
- Decision: Treat repeated wire positions as idempotent observations.
- Reason: PCP exposes a 32-bit position while long-running Nova sessions need a
  monotonic observable position. Guessing that a backward value is valid would
  silently corrupt relay, playback, or recording state.
- Consequence: A serialized live channel session owns one adapter and position
  tracker across reconnect attempts. A future live session decides how a
  reported discontinuity affects buffers and consumers.
- Consequence: A legitimate forward jump of 2^31 bytes or more is deliberately
  ambiguous and requires an explicit higher-level policy rather than guessing.
- Scope: This ADR defines control state only. It does not retain packet bytes or
  implement relay backpressure.
