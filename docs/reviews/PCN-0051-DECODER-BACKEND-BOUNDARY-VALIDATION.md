# PCN-0051 Decoder Backend Boundary Validation

## Status

Verified.

## Candidate contents

- host-owned backend interface;
- immutable bounded capability descriptors;
- deterministic priority and ordinal-ID selection;
- lazy selected-backend initialization;
- fixed initialization-failure mapping without backend exception retention;
- unsupported-container behavior compatible with the existing runner;
- seven deterministic boundary tests;
- no real decoder, dynamic loader, native dependency, or codec package.

## Recorded validation

- Release build: passed with zero warnings and zero errors;
- solution tests: 247 passed with zero failures and zero skips;
- existing Transport and PCP framing self-tests: passed.
- `dotnet format PeerCast-Nova.sln --verify-no-changes`: passed;
- decoder lifecycle repetition: 20/20 runs passed, 13 tests per run and 260
  executions total.

The formal artifact inspection excludes `bin`, `obj`, native binaries,
unapproved packages, local paths, channel identifiers, and media payloads.
