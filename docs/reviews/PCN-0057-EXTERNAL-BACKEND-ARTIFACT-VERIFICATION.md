# PCN-0057 External Backend Artifact Verification

## Status

Verified.

## Decoder selection evidence

Microsoft documents native Media Foundation containers including ASF, AVI,
MP3, MPEG-4, and ADTS, but not FlashVideo. FFmpeg documents `flv` and
`live_flv` demuxers. FFmpeg is LGPL 2.1-or-later by default, while optional GPL
or nonfree configuration changes redistribution obligations. FFmpeg publishes
source and links to third-party Windows builds rather than publishing Windows
binaries itself.

Therefore no FFmpeg binary is bundled in 0.0.6a. A future optional external
backend must record its exact build, license configuration, source provenance,
and SHA-256, then pass this verifier before activation.

## Acceptance

- Release build and all tests pass;
- format verification passes;
- backend-boundary and decoder-lifecycle 20-run gates pass;
- no external decoder, package, executable, DLL, or media payload is added.

## Recorded results

- Release build: zero warnings and zero errors;
- solution tests: 264 passed with zero failures and zero skips;
- format verification: passed;
- backend boundary gate: 19 tests x 20 runs = 380 executions passed;
- decoder lifecycle gate: 13 tests x 20 runs = 260 executions passed;
- no FFmpeg binary, decoder package, executable, DLL, or media payload.
