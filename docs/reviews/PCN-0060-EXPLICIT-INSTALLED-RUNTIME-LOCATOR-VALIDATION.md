# PCN-0060 Explicit Installed Runtime Locator Validation

## Status

Verified. ABI and symbol validation remain pending for 0.0.6d.

## Scope

- one host-supplied directory only;
- exact inventory file names only;
- no `PATH`, registry, or default-location search;
- existing runtime inventory verifier owns size and SHA-256 checks;
- caller path excluded from all result models and diagnostics;
- fixed fail-closed results for unavailable directory, missing file, unexpected
  file, size mismatch, and digest mismatch;
- opened streams are disposed after verification;
- no ABI check, native load, decoder, or media input.

## Acceptance

- Release build and all solution tests pass;
- format verification passes;
- normal, unavailable-directory, missing-file, unexpected-file, and path
  non-disclosure tests pass;
- backend-boundary and decoder-lifecycle 20-run gates pass;
- no FFmpeg binary or external package enters the artifact.

## Recorded results

- Release build: zero warnings and zero errors;
- solution tests: 276 passed with zero failures and zero skips;
- format verification: passed;
- normal explicit directory: verified;
- unavailable directory, missing file, unexpected file: fixed fail-closed results;
- locked runtime file: fixed result with no path-bearing exception;
- result model exposes no path or free-form diagnostic field;
- backend boundary gate: 19 tests x 20 runs = 380 executions passed;
- decoder lifecycle gate: 13 tests x 20 runs = 260 executions passed;
- no FFmpeg binary, decoder package, or external dependency added.

An unrelated pre-existing relay timing test failed once during a final suite
attempt. Targeted reruns passed, and the subsequent clean Release build and
complete 276-test suite passed. No locator change was required.
