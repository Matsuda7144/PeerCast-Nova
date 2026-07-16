# ADR-0034 Single-Command Controlled Relay Gate

## Status

Accepted for Milestone 0.0.4g. The TCP-table readiness mechanism was
superseded by ADR-0035 in 0.0.4h; the conjunctive end-to-end gate remains.

## Context

The bounded relay diagnostic and downstream Channel Session Probe already
provide content-free process results, but the controlled live procedure
requires an operator to coordinate two terminals. Manual timing can connect
the downstream too early, miss the finite relay window, or record only one
process exit status. The final 0.0.4 evidence must require both processes to
complete successfully without weakening the existing limits or inspecting
media contents.

Automation must not enumerate or alter PeerCastStation channels, expose the
relay outside loopback, attach to an unrelated local service, leave a
background process running, or treat a single successful half of the path as
an end-to-end pass.

## Decision

Add `Test-PeerCastStationRelayEndToEnd.ps1` as a Windows orchestration layer.
It performs these bounded steps:

1. validate the controlled Channel ID and all finite numeric parameters;
2. reject an already occupied downstream listen port before starting a job;
3. start one owned background job that waits for Nova's IPv4-loopback
   listener for at most 10 seconds by default;
4. run the existing relay diagnostic in the foreground;
5. connect the existing Channel Session Probe only after the new listener is
   observed;
6. wait at most 10 additional seconds for downstream completion after the
   relay process exits;
7. stop and remove only the job created by the script;
8. return success only when the relay exits 0 with `Acceptance Gate: Passed`
   and the downstream exits 0 with `Stop Trigger: PacketLimit`.

The downstream connection is always `127.0.0.1`. The upstream remains an
explicit operator-selected endpoint. The script invokes the already-built CLI
assembly and adds no protocol, packet-type, bootstrap, or payload semantics.

## Consequences

- One command now produces both fixed CLI summaries and one final
  `End-to-End Gate` result.
- An occupied loopback port fails before any downstream connection attempt.
- Relay failure, downstream failure, listener-start timeout, or completion
  timeout cannot produce exit code 0.
- Channel ID and upstream endpoint are not added to captured product output;
  operators must still redact the command line itself from published logs.
- Full 0.0.4 Relay remains pending until this gate passes against a
  user-selected real media channel served by an official PeerCastStation.
