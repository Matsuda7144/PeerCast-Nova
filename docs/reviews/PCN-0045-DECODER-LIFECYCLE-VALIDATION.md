# PCN-0045 Decoder Lifecycle Validation

## Status

**Verified.**

A Windows validation run using the repository-selected .NET 10 SDK completed
the Release build with zero source warnings and zero errors. All 214 solution
tests passed with zero failures and zero skips, both CLI self-tests completed,
and `dotnet format PeerCast-Nova.sln --verify-no-changes` completed with no
output.

All 13 `PlaybackDecoderRunnerTests` then passed for 20 consecutive repetitions:
260 lifecycle test executions with zero failures and zero skips.

## Verified cases

The deterministic suite covers:

- split FLV header reconstruction and one-time bootstrap replay;
- recognizing-packet bytes beyond the bootstrap bound submitted once as a
  direct remainder with adjusted stream position;
- pre-recognition discontinuity clearing old bootstrap bytes;
- post-recognition discontinuity ordering as reset before decode;
- unsupported decoder factory result;
- factory exception and decoder exception lease cleanup;
- caller cancellation with a decoder callback that ignores cancellation;
- owner stop with finite return, one retained lease, and delayed cleanup;
- natural complete-before-dispose ordering;
- `CompleteAsync` ignoring cancellation and using a finite deadline;
- one-shot runner enforcement and rejection after disposal;
- finite option bounds and a fixed content-free result schema.

## Validation history

The first Windows build exposed one analyzer error in production code because an
independent stop-deadline `Task.Delay` intentionally did not inherit caller
cancellation. The implementation now passes `CancellationToken.None`
explicitly.

The next build exposed seven warnings-as-errors in the new test source. These
were corrected by using `Assert.ThrowsAsync`, the predicate overload of
`Assert.Single`, `ArgumentOutOfRangeException.ThrowIfLessThan`, and the concrete
array return type. A final formatter-only line break was corrected in fix3.

After those corrections:

- Release build: passed;
- solution tests: 214 passed, 0 failed, 0 skipped;
- decoder lifecycle repetition: 13 tests x 20 runs = 260 passed;
- format verification: passed with no output;
- Transport and PCP framing self-tests: passed.

## Ownership and privacy evidence

The tests establish that:

- cooperative callbacks release their packet lease before runner completion;
- cancellation-ignoring callbacks retain no more than one active lease;
- delayed cleanup releases that lease after the callback actually completes;
- reset is serialized before the discontinuous encoded input;
- terminal results contain only fixed enums, booleans, and counters;
- no channel ID, payload, prefix, metadata, endpoint, position, session ID,
  exception text, or decoded media is returned.

No real decoder, codec package, native binary, decoded-frame contract, renderer,
or audio/video output is included.

## Decision

ADR-0039 and the backend-neutral lifecycle design are accepted and verified.
Milestone 0.0.5c is complete. Real decoder selection remains deferred until the
decoded output contract and frame ownership boundary are defined.
