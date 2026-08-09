# PCN-0492: Exact ISO BMFF Regular Movie Scheduler Ingress

## Scope reviewed

- one-owner composition of ADR-0481 decode progress and the existing bounded
  playback scheduler;
- exact active-completion identity, frame sequence, role, and media-kind
  validation before one-time frame detach;
- monotonic scheduler sequence transfer with no inferred discontinuity and
  acceptance-only sequence advancement;
- cancellation-before-detach retry and fail-closed scheduler-then-progress
  cleanup on rejection or invalid state.

## Verification

- focused managed tests prove cancellation retains the exact completion and
  that retry transfers the same frame once with the exact scheduler sequence;
- focused tests inject enqueue rejection and prove terminal aggregate cleanup
  without advancing accepted-transfer state;
- scheduler enqueue is injected, and no scheduler run, runtime discovery,
  library loading, native invocation, output, or physical execution is used.

## Result

Accepted as the exact regular-movie managed-frame scheduler-ingress boundary
defined by ADR-0482. Scheduler execution, canonical sink delivery, application
integration, and all physical output remain unimplemented.
