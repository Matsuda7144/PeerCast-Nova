# ADR-0051 User-Provided FFmpeg Runtime

## Status

Accepted in milestone 0.0.6c1 and hardened by ADR-0052 in 0.0.6c2.
License review remains pending.

## Decision

PeerCast Nova does not bundle or redistribute prebuilt FFmpeg binaries. FFmpeg
is always supplied separately by the user or host environment and is used only
through dynamic linking after the configured runtime passes Nova's integrity,
ABI, and policy gates.

Milestone 0.0.6c1 accepts exactly one explicit runtime directory supplied by the
host. Nova does not search `PATH`, inspect the registry, or probe conventional
installation directories. The directory must contain exactly the five files in
the pinned runtime inventory. Discovery results contain fixed categories only;
the supplied path is never included in diagnostics, logs, or exception text.

This narrows ADR-0049's unresolved distribution choice: the bundling route is
not adopted for the foreseeable implementation. A future decision to bundle
FFmpeg requires a new ADR and separate legal approval.

## License consequence

Not redistributing FFmpeg removes Nova's binary-redistribution path from the
current implementation, but it does not complete the LGPLv3 review. Dynamic
linking still leaves product obligations to assess and implement, including
appropriate copyright/attribution display and a practical means to access the
applicable license text. This ADR is an engineering distribution decision, not
legal advice or license approval.

## Out of scope

- automatic runtime discovery;
- ABI or symbol validation (planned for 0.0.6d);
- native library loading;
- decoder creation or media input;
- changing the candidate's `Pending` license-review state.
