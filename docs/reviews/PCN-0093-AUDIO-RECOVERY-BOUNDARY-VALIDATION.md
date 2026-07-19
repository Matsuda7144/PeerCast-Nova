# PCN-0093: Audio recovery boundary validation

## Scope
Validate the bounded 0.0.8a audio recovery transaction independently of native audio APIs and real-media playback.

## Automated checks

1. Recovered effect triggers exactly one generation reset and returns `Recovered`.
2. Retry preserves the same audio generation and uses the configured bounded delay.
3. Attempt exhaustion returns `AttemptLimitReached` and latches `ResetRequired`.
4. Generation reset failure returns `Failure` and latches `Faulted`.
5. Explicit reset cancels an active recovery operation and restores `Ready` only after serialization.
6. Invalid generations, attempt counts, and retry delays fail closed without invoking dependencies.
7. Concurrent recovery calls return `AlreadyRunning`.
8. Undefined dependency results and exceptions latch `Faulted`.
9. Disposal cancels active work and permanently returns `Disposed`.

## CI commands

```text
dotnet build PeerCast-Nova.sln -c Release --no-restore
dotnet test PeerCast-Nova.sln -c Release --no-build
dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore
```

## Acceptance
Ubuntu and Windows CI must pass build, the full test suite, and format verification. No unresolved review thread may remain before merge.

## Deferred validation
Native audio device reset, audible output, automatic pump restart, recovery backoff tuning, device migration, resampling, time stretching, and complete A/V synchronization remain outside 0.0.8a.
