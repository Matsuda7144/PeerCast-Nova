# PCN-0325: Caller-Explicit ISO BMFF Movie Codec Packet Event Queue Deadline Observation

## Decision

Accept pure `Pending`, `Ready`, or `Expired` classification of every ADR-0314
deadline at one caller-explicit signed tick, with exact boundary behavior and
signed deltas retained. No clock acquisition, repeated observation, dispatch,
scheduling, native operation, rendering, or physical execution occurs.
