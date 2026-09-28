# ADR-0490: Shared Manual and Automatic Recording Runtime

## Status

Proposed in PR #242. Implementation and managed verification are recorded in
PCN-0500; the PR remains a draft pending review and exact-head CI.

## Context

The recording foundation has a file sink, rule-driven automation, per-channel
session coordination, and a PCP relay fan-out recording source. A GUI needs an
explicit user-driven start and stop boundary. Representing a manual click as a
temporary `MatchAll` rule would conflate user intent with automation policy and
would make session ownership unclear when both paths act on one channel.

## Decision

Expose a GUI-independent `RecordingRuntime` with manual start, channel stop,
state lookup, automatic rule evaluation, and asynchronous shutdown operations.
Manual and automatic starts share the same per-channel coordinator and file
sink. Each session records whether it began manually or through an automatic
rule. An active manual session is not stopped or reclassified by later
automatic rule decisions.

Stop signals an in-flight source startup before waiting for the channel gate,
then reconciles the session state. Runtime shutdown stops recording sessions
before the PCP relay owners release fan-out readers. The PCP composition uses
the existing relay recording source provider so recording can read the relay's
shared stream without opening another PCP connection.

## Consequences

Application hosts can add manual recording without depending on automation
rules or recording implementation details. Manual and automatic requests for
one channel are serialized through one ownership boundary. A started file is
written to its partial path and is finalized when its session stops normally.

## Non-goals

This decision does not integrate the GUI in PR #230, add scheduling or a new
wire protocol, establish physical-channel acceptance, or change the accepted
Version 0.1.0 playback and relay MVP.
