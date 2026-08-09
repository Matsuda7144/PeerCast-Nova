# PCN-0321: Exact ISO BMFF Movie Codec Packet Event Queue Clock Time

## Decision

Accept exact rational clock-time projection for bounded queue entries using a
caller-explicit nonzero tick rate. `BigInteger` multiplication retains exact
numerators and the existing denominator remains unchanged. No division,
rounding, deadline, scheduling, native operation, rendering, or physical
execution occurs.
