# PCN-0010 Validation Status

## Completed by static artifact review

- XML parsing for every project file
- resolution of every `ProjectReference`
- acyclic project dependency graph
- independent atom-name padding model
- independent HTTP request model
- source-provenance guard
- ZIP integrity test
- manual review of PipeReader ownership boundaries
- explicit nullable flow for HTTP version parsing

## Completed on Windows

- .NET SDK 10.0.301 restore
- clean Release build of all ten projects with zero warnings and zero errors
- all 156 xUnit tests passed (including the 0.0.3a through final 0.0.4 suites)
- TCP Transport self-test passed
- PCP Framing self-test passed
- live IPv4 loopback interoperability with PeerCastStation 6.0.0.0:
  HTTP 200, OLEH version 1218, terminal `ok` code 1
- official Stable 4.1.0 live probe accepted with Atom trace `oleh`, `ok`
- official master development 7.1.1.0 live probe accepted with the same trace

See `PCN-0011-LIVE-INTEROPERABILITY-REPORT.md` and
`PCN-0012-TWO-BUILD-INTEROPERABILITY-REPORT.md` for sanitized 0.0.2c evidence.
See `PCN-0020-TWO-BUILD-CHANNEL-SESSION-REPORT.md` for the 0.0.3 packet-session
evidence.
See `PCN-0026-TWO-BUILD-RELAY-SERVER-INTEROPERABILITY.md` for the 0.0.4c
accept-coordinator evidence.
See `PCN-0028-COORDINATED-RELAY-INTEROPERABILITY.md` for the 0.0.4d owned
wire-to-wire coordinator evidence.
See `PCN-0030-FAULT-RESILIENT-RELAY-ACCOUNTING-VALIDATION.md` for the 0.0.4e
ingress-accounting evidence.
See `PCN-0032-BOUNDED-LIVE-RELAY-DIAGNOSTIC-VALIDATION.md` for the 0.0.4f
executable diagnostic evidence.
See `PCN-0034-SINGLE-COMMAND-CONTROLLED-RELAY-GATE-VALIDATION.md` for the
0.0.4g orchestration, failure, privacy, and cleanup evidence.
See `PCN-0036-EXPLICIT-RELAY-READINESS-VALIDATION.md` for the 0.0.4h readiness
contract and stream-orchestration evidence.
See `PCN-0038-CONTROLLED-REAL-MEDIA-RELAY-VALIDATION.md` for final 0.0.4
official-upstream real-media evidence.

## 0.0.2c decision

Milestone 0.0.2c is **Verified**. The two-build interoperability gate in
ADR-0022 is complete, and work may proceed to 0.0.3 Channel.

## 0.0.3a validation

- Channel control foundation compiles with the strict analyzer set;
- all 12 Core tests pass;
- finite module-stop timeout and cancellation continuation paths pass;
- formatting verification reports no source changes.

## 0.0.3b validation

- content-free observation accepted by local PeerCastStation 6.0.0.0;
- the first observed CHAN included `id`, `pkt`, `info`, and `trck`;
- later observed CHAN messages carried `id` and `pkt`;
- observation timeout and complete-Atom size limits pass synthetic peer tests;
- CHAN control parsing, malformed fields, unknown fields, duplicates, strict
  strings, and text limits pass synthetic tests;
- packet payload is absent from the returned control model;
- all 65 tests and both self-tests pass with zero build warnings and errors.

## 0.0.3c validation

- Network-to-Core metadata and track mapping passes synthetic tests;
- a combined metadata/packet observation creates exactly one Core revision;
- absent partial fields are preserved and empty values remain observable;
- 32-bit position advance, wrap, duplicate, and backward-discontinuity paths
  pass deterministic boundary tests;
- channel-ID mismatch and bitrate overflow are rejected before mutation;
- all 72 tests and both self-tests pass with zero build warnings and errors;
- `dotnet format --verify-no-changes --no-restore` reports no changes.

## 0.0.3d validation

- a buffered following top-level Atom remains unexamined at decoder yield;
- packet framing succeeds when every wire byte arrives separately;
- a gated consumer prevents the following buffered CHAN from reaching Core;
- cancellation stops a blocked, cancellation-aware consumer path;
- packet-size limits reject data before pooled ownership and Core mutation;
- metadata-only CHAN, QUIT, and payload-bearing discontinuity paths pass;
- inconsistent Atom, packet, and text limits are rejected at setup;
- all 81 tests and both self-tests pass with zero build warnings and errors.

## 0.0.3e validation

- one loopback write containing OLEH, OK, CHAN, and QUIT proves that the exact
  same PipeReader crosses the handshake boundary without losing buffered data;
- reconnect tests preserve the local session ID, next relay position, and
  monotonic packet sequence;
- relay-full and consumer-failure paths are terminal, while only finite
  transport/timeout/truncated-input failures are retryable;
- session stop cancels a blocked live read, closes the TCP connection, and
  reaches Core `Stopped` state;
- a packet consumer can request nonblocking stop from its own callback without
  deadlock;
- partial terminal summaries retain observed HTTP and CHAN counts while
  excluding identifiers, metadata, addresses, payload contents, positions, and
  exception text;
- the finite Channel Session Probe received four controlled live packets from
  PeerCastStation 6.0.0.0 after HTTP 200, then stopped cooperatively;
- all 89 tests and both self-tests pass with zero build warnings and errors.

## 0.0.3f validation

- Core exposes only fixed fault category, stage, attempt, and reconnect counts;
- no string, exception, identifier, endpoint, metadata, payload, position, or
  protocol code is present in the fault snapshot;
- state and fault publication, stop persistence, reconnect clearing, requested
  stop, retry exhaustion, remote outcomes, timeout, protocol, and consumer
  paths pass deterministic tests;
- isolated official Stable 4.1.0 and development 7.1.1 peers each returned
  HTTP 200 and delivered four live packets before cooperative finite stop;
- all 95 tests and both self-tests pass with zero build warnings and errors;
- `dotnet format --verify-no-changes --no-restore` reports no changes.

## 0.0.3 decision

Milestone 0.0.3 Channel is **Verified**. The privacy-safe observable fault
boundary and official stable/development finite packet-session gate are
complete. Work may proceed to 0.0.4 Relay.

## 0.0.4a validation

- one pooled copy remains valid independently of the ingress callback memory;
- packet-count and byte-count bounds include the in-flight child callback;
- a slow child and a failing sink stop independently while a healthy child and
  the upstream consumer continue;
- owner removal, fan-out stop, finite stop timeout, and cancellation-violating
  child behavior are deterministic;
- a child that has not completed cleanup retains its finite capacity slot;
- delivered, discarded, and pending counters contain no identifier, endpoint,
  metadata, payload content, exception, or free-form text;
- explicit stream discontinuity reaches each accepted child unchanged;
- all 105 tests and both self-tests pass with zero build warnings and errors;
- detailed evidence is recorded in `PCN-0022-RELAY-FANOUT-VALIDATION.md`.

## 0.0.4 status

Milestone 0.0.4a bounded relay fan-out is **Implemented**.

## 0.0.4b validation

- isolated official Stable 4.1.0 and development 7.1.1 clients produced the
  same strict HTTP/HELO shape without persisting identifiers or values;
- valid requests reserve a bounded child slot before 200 and gate writes until
  OLEH/OK completes;
- a full fan-out produces 503/OLEH/QUIT without a child queue;
- CHAN serialization reuses a 72-byte prefix and does not copy the shared
  payload into that prefix;
- wrong channel, non-ASCII input, malformed HELO, handshake timeout, remote
  QUIT, fan-out stop, write timeout, and write failure remain finite;
- a cancellation-violating writer returns session control at the finite
  cleanup boundary while retaining its bounded fan-out slot until real exit;
- downstream results have no string, exception, identifier, endpoint,
  metadata, payload, or position property;
- both official build lines accepted Nova's downstream handshake and received
  four CHAN packets / 16 bytes with zero discard and completed cleanup;
- all 122 tests and both self-tests pass with zero build warnings and errors;
- all 62 Network tests passed ten consecutive runs (620 executions);
- detailed evidence is recorded in
  `PCN-0024-TWO-BUILD-DOWNSTREAM-INTEROPERABILITY.md`.

Milestone 0.0.4b downstream PCP child wire/session is **Implemented** and its
two-build downstream interoperability is **Verified**. At that checkpoint,
the owning accept coordinator and continuous real-channel
upstream-to-Nova-to-downstream verification remained pending.

## 0.0.4c validation

- one server owns the bounded listener and all accepted child sessions while
  the upstream session and fan-out remain caller-owned;
- two simultaneous children complete independent handshakes and receive the
  same packet without a second payload owner;
- server stop cancels a child still waiting for its HTTP request and waits for
  its cleanup;
- TCP connection capacity and fan-out child capacity remain separate, and a
  second peer receives the finite relay-full path when the fan-out is full;
- caller cancellation and explicit stop have fixed terminal reasons, and the
  lifecycle is single-use;
- aggregate snapshots contain no string, exception, endpoint, identifier,
  metadata, payload, or position property;
- official Stable 4.1.0 and development 7.1.1 each completed one accepted
  handshake and received four CHAN packets / 16 bytes through the server with
  zero discard, cleanup failure, or internal failure;
- all 127 tests and both self-tests pass with zero build warnings and errors;
- all 67 Network tests passed ten consecutive runs (670 executions);
- detailed evidence is recorded in
  `PCN-0026-TWO-BUILD-RELAY-SERVER-INTEROPERABILITY.md`.

Milestone 0.0.4c bounded downstream relay server is **Implemented** and its
two-build interoperability is **Verified**. Full 0.0.4 Relay remains in
progress until continuous real-channel upstream-to-Nova-to-downstream
verification is complete.

## 0.0.4d validation

- one owner starts and stops the upstream session, fan-out, and downstream
  server in a defined order;
- fan-out idle waiting includes queued and in-flight packets, returns at a
  finite timeout, and observes cancellation;
- natural upstream completion performs finite drain, while requested stop and
  caller cancellation stop both network boundaries promptly;
- upstream reconnect preserves one downstream child and changes the next
  request position from 0 to the last accepted position 100;
- two packets from separate upstream connections arrive through the same
  downstream connection in order;
- a truncated upstream response becomes fixed `UpstreamFailed` while Core
  contains only its fixed failure category, stage, and counts;
- coordinator results contain no string, exception, endpoint, identifier,
  metadata, payload, or position property;
- synthetic upstream wire through Nova delivered 18 packets / 72 bytes to
  official Stable 4.1.0 and 19 packets / 76 bytes to development 7.1.1, with
  zero discard, incomplete cleanup, or internal failure;
- an exploratory packet-only synthetic source delivered 20 packets to an
  official Stable upstream, but that peer did not re-relay the fixture to
  Nova; no unobserved bootstrap or packet-type value was inferred;
- all 132 tests and both self-tests pass with zero build warnings and errors;
- all 72 Network tests passed ten consecutive runs (720 executions);
- detailed evidence is recorded in
  `PCN-0028-COORDINATED-RELAY-INTEROPERABILITY.md`.

Milestone 0.0.4d owned relay coordinator is **Implemented** and its controlled
wire-to-wire and official downstream interoperability are **Verified**. Full
0.0.4 Relay remains in progress until a controlled real media channel passes
from an official upstream through Nova without guessed or copied semantics.

## 0.0.4e validation

- the coordinator counts a packet only after the bounded fan-out accepts it;
- the live and terminal ingress snapshots report the same fixed packet and
  payload-byte totals;
- one complete packet followed by an incomplete upstream atom produces fixed
  `UpstreamFailed` with a null upstream terminal result, while ingress and
  downstream delivery both retain 1 packet / 2 bytes;
- finite drain and awaited shutdown still complete on that failure path;
- the ingress snapshot exposes no identifier, endpoint, metadata, payload,
  position, exception, or peer-controlled text;
- the isolated interoperability acceptance gate now reads the coordinator
  ingress aggregate instead of treating a missing upstream result as zero;
- all 132 tests and both self-tests pass with zero build warnings and errors;
- all 72 Network tests passed ten consecutive runs (720 executions);
- detailed evidence is recorded in
  `PCN-0030-FAULT-RESILIENT-RELAY-ACCOUNTING-VALIDATION.md`.

Milestone 0.0.4e fault-resilient relay accounting is **Implemented** and its
deterministic failure path is **Verified**. Full 0.0.4 Relay remains in
progress until controlled real-media official-upstream verification succeeds.

## 0.0.4f validation

- the CLI relay diagnostic binds only IPv4 loopback and has a finite 1 second
  through 10 minute observation window;
- upstream connection, idle timeout, reconnect count, child count, child
  packet/byte queue, drain, handshake, write, and cleanup boundaries remain
  finite;
- strict parsing rejects missing channel ID, invalid channel ID, port zero,
  invalid listen port, duration below the minimum, zero packet target,
  excessive reconnects, unsafe host text, unknown options, and duplicate
  options;
- an occupied loopback listen port returns fixed `DownstreamFailed`, omits the
  Ready marker and sensitive inputs, fails the acceptance gate, and completes
  cleanup;
- the acceptance result contains only fixed enums, numeric counters, and a
  boolean, and the CLI does not print channel ID, metadata, payload, position,
  exception, or peer-controlled text;
- an isolated executable chain accepted 46 packets / 184 bytes from a
  synthetic upstream and delivered 4 packets / 16 bytes to a separate CLI
  downstream session;
- that run reported one accepted handshake, zero discard, zero incomplete
  cleanup, zero internal failure, completed shutdown, `Acceptance Gate:
  Passed`, and process exit code 0;
- all 147 tests and both self-tests pass with zero build warnings and errors;
- all 15 CLI tests passed ten consecutive runs (150 executions);
- detailed evidence is recorded in
  `PCN-0032-BOUNDED-LIVE-RELAY-DIAGNOSTIC-VALIDATION.md`.

Milestone 0.0.4f bounded live relay diagnostics is **Implemented** and its
isolated executable path is **Verified**. The command is now ready for a
user-selected controlled real-media channel. Full 0.0.4 remains pending until
that official-upstream run passes.

## 0.0.4g validation

- one command starts the bounded relay diagnostic and one owned downstream
  Channel Session job;
- an occupied listen port is rejected before the job or a downstream
  connection is created;
- the downstream waits for a new IPv4-loopback listener with a finite start
  deadline and has a finite session deadline;
- the final gate requires relay exit 0, relay acceptance, downstream exit 0,
  and downstream packet-limit stop;
- an isolated success path accepted 46 packets / 184 bytes and delivered 4
  packets / 16 bytes with zero discard, incomplete cleanup, or internal
  failure;
- an upstream-absent path returned exit 1, and an occupied-port path failed
  before connection;
- success and failure output omitted the supplied Channel ID, and no owned job
  or isolated listener remained after completion;
- all 147 tests and both self-tests pass with zero build warnings and errors;
- detailed evidence is recorded in
  `PCN-0034-SINGLE-COMMAND-CONTROLLED-RELAY-GATE-VALIDATION.md`.

Milestone 0.0.4g single-command controlled relay gate is **Implemented** and
its isolated success and rejection paths are **Verified**. Full 0.0.4 remains
pending until a user-selected official-upstream real-media run passes.

## 0.0.4h validation

- coordinator readiness completes with fixed `Ready`, `Stopped`, or `Failed`
  and contains no endpoint, identifier, exception, or peer text;
- successful bind publishes Ready before downstream connection;
- occupied-port bind publishes Failed and does not expose a local endpoint;
- unstarted stop publishes Stopped, while canceling one readiness observer
  leaves coordinator ownership unchanged;
- the CLI emits its Ready marker only after the explicit Network outcome;
- the one-command tool consumes the relay output stream and no longer inspects
  the OS TCP table;
- an occupied listener received no downstream connection, while an absent
  upstream produced a complete exit-1 result after legitimate readiness;
- the isolated success path again accepted 46 packets / 184 bytes and
  delivered 4 packets / 16 bytes with zero discard, incomplete cleanup, or
  internal failure;
- all 150 tests and both self-tests pass with zero build warnings and errors;
- all 75 Network tests and all 15 CLI tests pass ten consecutive runs (750 and
  150 executions);
- detailed evidence is recorded in
  `PCN-0036-EXPLICIT-RELAY-READINESS-VALIDATION.md`.

Milestone 0.0.4h explicit relay readiness is **Implemented** and its contract
and executable paths are **Verified**. Full 0.0.4 remains pending until a
user-selected official-upstream real-media run passes.

## 0.0.4 final validation

- a bounded observation found an official HTTP 200 response with ASCII status
  and field names plus two unknown field values containing high octets;
- upstream response field values now accept bounded legacy `obs-text`, while
  downstream requests, status lines, names, controls, CRLF, and size limits
  remain strict;
- six tests cover the directional compatibility and rejection boundary;
- the compatibility probe completed HTTP 200, `oleh`, `ok`, remote version
  1218, and process exit 0 against PeerCastStation 6.0.0.0;
- the controlled 60-second end-to-end run accepted 1,338 packets / 17,381,199
  bytes and delivered four packets / 51,633 bytes downstream;
- one downstream handshake was accepted with zero discard, incomplete
  cleanup, or internal failure and completed shutdown;
- relay acceptance, downstream packet-limit stop, the end-to-end gate, and
  process exit all passed;
- the report omits Channel ID, metadata values, payload contents, positions,
  session identifiers, and the command line;
- all 156 tests and both self-tests pass with zero build warnings and errors;
- all 81 Network tests pass ten consecutive runs (810 executions);
- detailed evidence is recorded in
  `PCN-0038-CONTROLLED-REAL-MEDIA-RELAY-VALIDATION.md`.

Milestone 0.0.4 Relay is **Verified**. The next milestone is 0.0.5 Playback.

## 0.0.5a validation

- `Nova.Playback` depends on Network without a reverse Network, Core,
  Protocol, decoder, renderer, or UI dependency;
- callback-scoped PCP payload is copied once into packet- and byte-bounded
  owned pooled memory;
- packet and byte overflow prefer recent data and mark the first retained
  packet with a fixed latency discontinuity;
- individually oversized packets are skipped before payload allocation;
- source and local latency discontinuities remain distinct fixed flags;
- one reader receives disposable leases with outstanding ownership counters;
- graceful completion drains, owner stop immediately discards pending data,
  and canceled observation does not mutate ownership;
- all 13 Playback tests pass 20 consecutive runs (260 executions);
- all 169 tests and both self-tests pass with zero build warnings and errors;
- detailed evidence is recorded in
  `PCN-0041-PLAYBACK-INPUT-VALIDATION.md`.

Milestone 0.0.5a Playback Input Foundation is **Implemented** and its synthetic
contracts are **Verified**. Full 0.0.5 remains pending format detection,
decoder/renderer lifecycle, A/V output, and controlled real-media playback.

## 0.0.5b validation

- transient container evidence is bounded to 64 KiB by default and 1 MiB at
  the configurable hard maximum;
- retained prefixes are cleared on discontinuity and every terminal or
  disposal path;
- results contain only fixed enums and counters without signature bytes,
  identifiers, endpoints, metadata, positions, peer text, or exceptions;
- independently implemented rules recognize FLV, Ogg, MPEG-TS, ASF, WebM,
  and Matroska while unsupported input remains unknown;
- the finite CLI gate owns session, playback input, classifier, timeout, and
  cooperative shutdown;
- controlled PeerCastStation 6.0.0.0 input classified as FlashVideo with HTTP
  200, zero skips, zero pending/outstanding packets, and completed shutdown;
- all 29 Playback tests pass 20 consecutive runs (580 executions);
- all 31 CLI tests pass 10 consecutive runs (310 executions);
- all 201 tests and both self-tests pass with zero build warnings and errors;
- detailed evidence is recorded in
  `PCN-0043-MEDIA-SIGNATURE-VALIDATION.md`.

Milestone 0.0.5b Media Signature Classification is **Implemented** and its
synthetic rules, privacy/lifecycle bounds, and controlled official-upstream
FlashVideo classification are **Verified**. Full 0.0.5 remains pending decoder
lifecycle, A/V output, and controlled audible/visible playback.

## Broader validation still pending

- Linux CI execution;
- Windows CI execution in the repository's hosted CI environment.

These remain publication and cross-platform confidence checks; they do not
change the completed 0.0.2c interoperability decision.
