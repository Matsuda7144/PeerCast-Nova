# PCN-0052 Backend Selection Probe Validation

## Status

Verified.

## Automated validation

- Release build: passed with zero warnings and zero errors;
- solution tests: 248 passed with zero failures and zero skips;
- lifecycle probe now traverses the backend registry;
- one new capability/no-eager-creation test added;
- `dotnet format PeerCast-Nova.sln --verify-no-changes`: passed;
- decoder lifecycle repetition: 20/20 runs passed (260 executions);
- no codec package or native dependency added.

## Required completion

Run the existing controlled PeerCastStation lifecycle probe. Acceptance requires
`Lifecycle Gate: Passed`, one backend/decoder creation, one disposal, matching
runner/decoder input and byte counts, no deferred cleanup, and zero pending or
outstanding packet leases. Then rerun format and the 20-run lifecycle gate.

## Controlled real-media result

- HTTP status: 200;
- classification: `FlashVideo` / `FileHeader`;
- stop trigger: input limit;
- selected backend/decoder creates: 1;
- decoder disposes: 1;
- runner and decoder inputs: 4 / 4;
- runner and decoder bytes: 45879 / 45879;
- skipped, pending, and outstanding packets: 0;
- deferred cleanup: false;
- shutdown completed: true;
- lifecycle gate: passed.

The channel identifier and media payload were not recorded.
