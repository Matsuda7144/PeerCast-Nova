# ADR-0091: Diagnostic Decoder Configuration

## Status

Accepted for diagnostic-only managed parameter preparation.

## Context

The bounded fixture carries encoded H.264/AAC packets and timing. The existing codec-parameter boundary also needs explicit H.264 dimensions and codec data plus AAC sample rate, channel count, and codec data. Deriving those values from packet payloads would add parser responsibility to the diagnostic harness.

## Decision

Add a separate bounded configuration contract with explicit video and audio metadata. Delegate validation, defensive copying, padding, and ownership to the existing codec-parameter factory.

A successful preparation owns exactly two managed parameter leases. Disposal runs audio first and video second. If audio preparation fails, the already-created video owner is disposed before return.

## Results

Missing configuration is invalid. Rejected video or audio metadata has separate fixed results. Unexpected failures return a fixed fault result after cleanup.

## Non-goals

No packet parsing, native context allocation, native parameter transfer, codec open, packet submission, production activation, license-state change, general container demux, audible output, or visible output is added.
