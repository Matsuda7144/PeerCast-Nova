# PCN-0070 Frame Plane / Sample Boundary Validation

## Scope

Milestone 0.0.6l validates callback-scoped FFmpeg 8.1 video planes and audio
sample planes against the managed metadata established in 0.0.6k. It adds no
copy, conversion, decoded-frame output, demux, queue, renderer, or device.

## Boundary checks

- Native frame and data pointers remain inside the 0.0.6j callback.
- The public callback view is a pointer-free `ref struct` with bounded access.
- Format, plane count, dimensions, sample count, channel count, stride, and
  total referenced bytes are checked before the consumer is called.
- Null pointers, malformed line sizes, overflow, and excessive sizes fail
  closed with fixed results.
- Unknown timestamp and duration values from 0.0.6k are preserved unchanged.
- Consumer rejection preserves the context; unexpected native failure disposes
  it; unsupported platforms do not access native memory.

## Verification

- `build.ps1`: passed; Release build warning 0 / error 0.
- Full test suite: 434 total, 431 passed, 0 failed, 3 existing Windows skips.
- Related frame plane/sample tests: 12 passed, 0 failed, 0 skipped.
- `dotnet format PeerCast-Nova.sln --verify-no-changes`: passed.
- `git diff --check`: passed.
