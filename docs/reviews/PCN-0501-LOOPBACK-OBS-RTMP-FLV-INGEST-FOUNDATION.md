# PCN-0501: Loopback OBS RTMP FLV Ingest Foundation

## Scope

The independent OBS foundation starts at main
`1193569f79274a2797e544f8796c85183227eda2`. The decision is documented in
[ADR-0491](../adr/ADR-0491-loopback-OBS-RTMP-FLV-ingest-foundation.md).

Review covers one-generation IPv4 loopback listening, a minimal classic
RTMP3/AMF0 publisher subset, finite parsing and bootstrap budgets, AVC/AAC-LC
FLV tagging, IDR admission, awaited PCP HEAD/DATA publication, single-client
ownership and terminal cleanup. Existing PR #229/#230 branches are untouched.

## Verification contract

- Independent wire fixtures cover chunk formats, extended timestamp/delta,
  fragmented reads, interleaving, abort, size/context/global memory budgets,
  malformed input and acknowledgements during large-message reception.
- AMF fixtures cover valid primitives/objects/ECMA arrays and reject malformed,
  duplicate, nonfinite, deeply nested and oversized values.
- Managed localhost tests cover the OBS publisher command sequence, existing
  HTTP-FLV composition, duplicate publisher rejection, lifecycle, backpressure,
  consumer failure, timeout, cancellation and explicit stop versus disconnect.
- FLV tests cover bounded bootstrap/DATA, original codec/sample bytes,
  timestamp/PreviousTagSize, initial IDR gate and codec-change rejection.
- Existing managed FLV parser tests verify the new output's stream description,
  samples, extended timestamp and negative composition-time interpretation.
- Release solution build/test, format and diff checks are required locally.

## Evidence boundary

The slice provides a library host, not application broadcast registration,
relay listener ownership or YP publication. No physical OBS/player/native
output action is performed. Actual OBS interoperability and physical A/V are
UNVERIFIED. Managed test results do not inherit the previous fMP4 physical
acceptance. Full RTMP, enhanced codecs, reconnect, multiple streams, WAN and
long-duration position wrap are deferred.
