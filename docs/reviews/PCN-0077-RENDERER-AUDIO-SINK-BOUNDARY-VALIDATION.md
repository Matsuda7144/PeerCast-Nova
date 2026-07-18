# PCN-0077 Renderer / Audio Sink Boundary Validation

## Scope

0.0.6s fixes the pure-managed contracts between canonical scheduler output and
future video renderer/audio device implementations. It performs no OS output.

## Contract checks

- Video: BGRA32, width, height, stride, generation, exact payload length.
- Audio: S16/F32 interleaved PCM, sample rate, channels, sample count,
  generation, exact payload length.
- Maximum payload: 256 MiB.
- Submit serialization: at most one callback per individual sink.
- Video/audio admission: independent.
- Ownership: caller-owned, callback-scoped payload; retaining sinks must copy.
- Fixed outcomes cover unsupported format, device lost, temporary
  unavailability, rejection, cancellation, disposal, and failure.

## Verification

- Release build: passed with 0 warnings and 0 errors.
- Full test suite: 548 passed, 0 failed, 3 existing Windows skips (551 total).
- Renderer/audio boundary tests: 14 passed, 0 failed, 0 skipped.
- `dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore`: passed.
- `git diff --check`: passed.

Status: Verified.
