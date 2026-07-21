# PCN-0144 CI Evidence

## Scope

This appendix records the routine Linux gate for the controlled retained-fixture production activation validation. It does not add a new architecture decision or widen the physical Windows／FFmpeg claims in PCN-0144.

## Verified implementation head

The controlled integration test head `66fd0b75f6f23129c0224fc0a884156e251bb27e` passed the routine CircleCI job containing:

- solution restore;
- Release build with analyzers;
- full solution tests;
- format verification.

## Documentation finalization

The one-shot documentation-finalization job on `48cb718fa2f409d0c14eb0d2926692212709bd8f` ran the same routine gate successfully before it:

- inserted the 0.0.9z CHANGELOG entry;
- restored `.circleci/config.yml` byte-for-byte to the routine configuration;
- removed its one-shot finalizer script;
- produced documentation commit `525960e41c1775d28adf4b846460b6ed3643e10e`.

The restored routine CircleCI configuration blob is `6184cadc823ea50362062d3727780c501907e71f`.

## Final readiness gate

The pull request is Ready for Review only after the branch head containing this appendix passes the same restore, Release build with analyzers, full solution tests, and format verification. No physical Windows output, real FFmpeg loading, or audible／visible playback claim is derived from this Linux gate.
