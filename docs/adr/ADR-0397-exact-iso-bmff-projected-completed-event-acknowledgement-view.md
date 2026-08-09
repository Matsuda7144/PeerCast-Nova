# ADR-0397: Exact ISO BMFF Projected Completed-Event Acknowledgement View

## Status

Accepted as a post-0.0.10e non-physical managed composition-foundation slice.

## Decision

Apply the existing acknowledgement-view projector to one exact ADR-0396
completion. Retain the full projected source and reconstruct every initial and
continuation segment acknowledgement under contiguous zero-based global
indexes, reference-exact cursor continuity, and exact root/final cursor
identity.

## Non-goals

Clock conversion, deadline or dispatch metadata, delivery, scheduling, runtime
access, native execution, rendering, or physical execution are not included.
