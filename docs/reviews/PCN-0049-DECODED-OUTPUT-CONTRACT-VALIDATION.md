# PCN-0049 Decoded Output Contract Validation

## Status

**Verified.**

## Candidate contents

- canonical interleaved PCM audio frame contract;
- canonical packed BGRA32 video frame contract;
- fixed sequence/timing/discontinuity metadata;
- strict payload-layout validation and finite dimensions/rates/durations;
- awaited `IPlaybackDecodedFrameSink` callback boundary;
- runner-to-factory sink injection;
- content-discarding null sink for lifecycle-only operation;
- seven deterministic tests for canonical layout validation, default/injected
  sink selection, and sink backpressure through the decoder runner.

No real decoder, decoded queue, renderer, audio device, native dependency, or
media output is included.

## Executed acceptance

```powershell
./build.ps1

dotnet format PeerCast-Nova.sln --verify-no-changes
```

The solution test count was 240: the verified 233-test 0.0.5d baseline plus
seven decoded-output contract tests.

Recorded results:

- Release build passed;
- 240 tests passed with zero failures and zero skips;
- Transport and PCP framing self-tests passed;
- `dotnet format PeerCast-Nova.sln --verify-no-changes` passed;
- decoder lifecycle repetition passed 20 runs (13 tests per run, 260 test executions);
- the blocking-sink test proves that the runner remains active while the sink
  callback is pending and releases the encoded lease after completion;
- no package reference or native decoder dependency is added.

## Decision

ADR-0041 and PCN-0048 are accepted. Milestone 0.0.5e is verified. This does not
claim a real decoder backend, decoded queue, renderer, output device, A/V clock,
or audible/visible playback.
