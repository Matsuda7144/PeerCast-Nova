# PCN-0177: Opt-in Decoded-buffer Duration Cutoff

## Status

Implemented and verified as a non-physical scheduler boundary.

## Implemented boundary

- optional positive duration budget with a ten-minute configuration hard limit;
- strict rejection and disposal of unknown duration when the cutoff is enabled,
  unless the later reviewed audio derivation is explicitly selected;
- distinct single-frame-over-limit result;
- existing full-policy behavior for aggregate duration saturation;
- duration ownership across queued, output-reserved, and in-flight frames;
- snapshot duration accounting and cleanup;
- production generation option cloning and validation;
- unchanged behavior when the option is not selected.

## Verification scope

Focused managed tests cover unknown duration, a single oversized duration,
aggregate drop-incoming behavior, multi-frame drop-oldest behavior, in-flight
ownership, cleanup, and option hard limits. Routine solution build and tests
provide the integration gate.

No physical Windows audio or video execution is required or authorized by this
review.

## Remaining work

ADR-0171 later selects a 250 ms audio-only production cutoff. A later policy
must still define all-media handling for streams with unavailable video
duration before the overall decoded-buffer duration cutoff can be called
complete.

PCN-0179 establishes opt-in exact audio derivation. Unknown video duration and
the default production value remain unresolved.
