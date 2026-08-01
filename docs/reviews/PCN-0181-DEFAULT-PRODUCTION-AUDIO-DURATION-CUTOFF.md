# PCN-0181: Default Production Audio-duration Cutoff

## Status

Implemented and verified as a non-physical production policy.

## Selected default

- maximum owned audio duration: `250 ms`;
- media scope: `AudioOnly`;
- unknown audio duration: conservatively derived from samples;
- saturation policy: `DropIncoming`;
- video duration inference: none.

## Controlled boundary preservation

The controlled physical runner continues to construct explicit scheduler
options with no duration cutoff, `All` scope, and derivation disabled. Existing
physical evidence, commands, and consumed authorizations are unchanged and
were not exercised.

## Verification

Managed tests verify the production defaults and the controlled runner's
explicit unchanged duration configuration. Scheduler tests cover audio-only
accounting, sample derivation, rounding, saturation, ownership, and cleanup.
Routine solution build, tests, and format checks remain required.

## Remaining work

Complete all-media duration handling and unknown-video policy remain
unimplemented. This review establishes only the default production audio bound.
