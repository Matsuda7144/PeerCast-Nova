# PCN-0058 Pinned FFmpeg Candidate Validation

## Status

Verified candidate record. Activation remains pending legal review.

## External artifact validation

- GitHub API digest, published `checksums.sha256`, and local SHA-256 matched;
- archive size: 70,503,568 bytes;
- version: `n8.1.2-22-g94138f6973-20260715`;
- shared libraries enabled and static libraries disabled;
- `--enable-version3` present;
- `--enable-gpl` and `--enable-nonfree` absent;
- `flv` and `live_flv` demuxers present;
- LGPLv3 license text present;
- verifier completed without loading media or modifying PeerCastStation.

The downloaded candidate remains under `work` only and is excluded from all
Nova artifacts. License review remains pending, so activation remains rejected.

## Nova regression results

- Release build: zero warnings and zero errors;
- solution tests: 266 passed with zero failures and zero skips;
- format verification: passed;
- backend boundary gate: 19 tests x 20 runs = 380 executions passed;
- decoder lifecycle gate: 13 tests x 20 runs = 260 executions passed.
