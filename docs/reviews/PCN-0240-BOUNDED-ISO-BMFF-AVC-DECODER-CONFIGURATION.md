# PCN-0240: Bounded ISO BMFF AVC Decoder Configuration

## Scope

Review bounded parsing and ownership of the selected visual entry's `avcC`
record.

## Findings

- Missing and duplicate configuration boxes are distinct failures.
- Current box type, location, size, and header width are revalidated.
- Configuration bytes and parameter-set counts have explicit hard limits.
- Reserved bits, nonempty SPS/PPS, high-profile suffix, and exact length fail
  closed.
- Returned configuration and parameter sets do not alias input memory.
- No NAL conversion, decoder, or native operation occurs.

## Decision

Accept bounded AVC decoder-configuration parsing. AVC packet framing and demux
event projection remain incomplete.
