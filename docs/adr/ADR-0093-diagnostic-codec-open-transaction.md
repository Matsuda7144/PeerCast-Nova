# ADR-0093: Diagnostic Codec-Open Transaction

## Status

Accepted for milestone 0.0.8i.

## Context

Milestone 0.0.8h prepares two independently owned native decoder contexts and transfers bounded H.264 and AAC parameters into them. The existing 0.0.6h boundary opens one transferred context through `avcodec_open2`, accepts only native result zero, and disposes a rejected or faulted context fail closed.

A diagnostic integration step is required before packet submission can be considered. The two contexts cannot share one parent gate/runtime ownership chain, and a partially opened pair must not survive an audio-open failure.

## Decision

Add a finite diagnostic-only transaction that accepts the complete 0.0.8h two-context lease.

The transaction opens video first and audio second through the existing codec-open boundary. It does not call native functions directly and introduces no codec options.

Any video rejection stops before audio open. Any audio rejection, unexpected exception, invalid input, or disposed input fails closed. Rejected and faulted paths dispose the complete aggregate lease, whose established reverse cleanup order is audio then video.

Success returns the same aggregate lease after both contexts report `Opened`. The transaction does not create a second ownership alias or detach either context.

Public outcomes remain fixed enum values and expose no pointers, paths, native return codes, symbol addresses, option text, or native error strings.

## Consequences

Milestone 0.0.8i ends with two independently owned, parameter-configured, opened native decoder contexts. Packet submission, frame reception, flush, end-of-stream handling, demux, production activation, redistribution, and license approval remain outside scope.
