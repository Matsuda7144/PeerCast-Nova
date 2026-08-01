# PCN-0180: Audio-scoped Decoded-duration Cutoff

## Status

Implemented and verified as a non-physical scheduler policy boundary.

## Implemented behavior

- explicit `All` and `AudioOnly` duration scopes;
- unchanged strict all-media default;
- zero video duration accounting in audio-only scope;
- unchanged video count and byte ownership;
- normal duration full-policy handling for audio;
- required pairing of non-default scope with an enabled duration cutoff;
- production generation option validation.

## Verification

Managed tests prove that unknown video remains accepted and uncounted only in
audio scope, known video also contributes no audio duration, audio saturation
still applies the selected full policy, and incomplete or undefined scope
configurations fail closed.

No physical execution or readiness operation is involved.

## Remaining work

PCN-0181 later selects a 250 ms audio-only production default. Complete
all-media duration handling, including unknown video, remains incomplete.
