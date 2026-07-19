# PCN-0094: Recovery-aware pump orchestrator validation

## Objective
Validate that 0.0.8b composes the finite pump and bounded audio recovery without unbounded restart, generation drift, hidden reset, or ownership transfer.

## Automated validation

Run:

```powershell
dotnet restore PeerCastNova.slnx
dotnet build PeerCastNova.slnx -c Release --no-restore
dotnet test PeerCastNova.slnx -c Release --no-build
dotnet format PeerCastNova.slnx --verify-no-changes --no-restore
```

Required focused coverage:

1. `AudioRecoveryRequired -> Recovered -> pump reset -> pump restart -> Completed`.
2. Fixed video/audio generations are passed to recovery and pump reset.
3. Recovery-count exhaustion returns `RecoveryLimitReached` and latches `ResetRequired`.
4. Recovery-side `ResetRequired` propagates without restarting the pump.
5. Pump failure faults without invoking recovery.
6. Stop cancels an active pump and returns `Stopped`.
7. Explicit reset clears recovery before resetting pump generations.

## Invariants

- The orchestrator never recovers unless the pump returns `AudioRecoveryRequired`.
- A successful recovery cannot restart the pump until pump generation reset succeeds.
- A failed or reset-required recovery cannot restart the pump.
- Recovery count is finite and visible in the snapshot.
- Video/audio generations remain stable for the complete orchestration run.
- Injected pump and recovery boundaries are not disposed by the orchestrator.

## Deferred validation

Real device migration, audio activation, deadline timing, drift correction, frame duplication, resampling, and audible/visible media playback remain outside this milestone.
