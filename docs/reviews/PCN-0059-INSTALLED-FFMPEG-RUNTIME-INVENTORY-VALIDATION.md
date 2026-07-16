# PCN-0059 Installed FFmpeg Runtime Inventory Validation

## Status

Verified. Activation remains pending license review.

## Runtime inventory result

- `avcodec-62.dll`: 70,881,792 bytes;
- `avformat-62.dll`: 22,070,784 bytes;
- `avutil-60.dll`: 2,937,856 bytes;
- `swresample-6.dll`: 723,968 bytes;
- `swscale-9.dll`: 12,570,624 bytes;
- all five SHA-256 values matched the pinned 2026-07-15 candidate;
- missing, unexpected, size mismatch, and digest mismatch have fixed results;
- stream ownership remains with the caller;
- no native library was loaded by the Playback verifier.

The staged DLLs remain under `work` only and must not enter Nova artifacts.

## Nova regression results

- Release build: zero warnings and zero errors;
- solution tests: 270 passed with zero failures and zero skips;
- format verification: passed;
- backend boundary gate: 19 tests x 20 runs = 380 executions passed;
- decoder lifecycle gate: 13 tests x 20 runs = 260 executions passed.
