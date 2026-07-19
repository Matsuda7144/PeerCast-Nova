# PCN-0095: Deadline-aware Run Budget Policy Validation

## Scope

Validate milestone 0.0.8c as a pure managed policy that derives one finite `PlaybackLateAwareContinuousPumpOptions` budget from caller-owned monotonic `now` and `deadline` values.

## Required behavior

- Valid non-negative monotonic inputs produce a remaining duration without reading a clock or waiting.
- The safety margin is removed before a run budget is issued.
- Budgeted run duration never exceeds either usable deadline time or the requested maximum run duration.
- Budgeted pump count never exceeds either the requested maximum or the number of complete minimum-duration pump units fitting in the run budget.
- A deadline with no complete minimum pump unit returns `DeadlineReached` and no pump options.
- Existing idle delay, idle limit, dispatch options, and nested policy objects are preserved without ownership transfer.
- Negative timestamps, reversed deadlines, invalid margins, invalid minimum pump duration, or invalid nested pump options fail closed as `InvalidConfiguration`.
- Unexpected checked arithmetic failure returns `Failure` without output options.

## Explicit non-goals

- No pump execution or resident scheduling loop.
- No timer, delay, sleep, or clock acquisition.
- No dispatch, reservation acknowledgement, drop, recovery, reset, or generation effect.
- No measured-cost adaptation or precise preemption.
- No decoder selection, FFmpeg backend activation, native resource access, rendering, or audible output.

## Automated coverage

`PlaybackDeadlineAwareRunBudgetPolicyBoundaryTests` covers:

1. deadline-derived duration and pump-count reduction;
2. preservation of stricter caller-requested limits;
3. safety-margin deadline exhaustion;
4. rejection when fewer than one minimum pump unit fits;
5. negative and reversed monotonic inputs;
6. invalid nested pump configuration.

## Validation commands

```text
dotnet restore PeerCast-Nova.sln
dotnet build PeerCast-Nova.sln --configuration Release --no-restore
dotnet test PeerCast-Nova.sln --configuration Release --no-build
dotnet format PeerCast-Nova.sln --verify-no-changes --no-restore
```

## Activation statement

Milestone 0.0.8c adds no reference from `PlaybackDecoderBackendFactory` or any other activation path. FFmpeg backend activation remains unapproved.
