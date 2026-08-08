# PCN-0276: Bounded ISO BMFF AAC Declared Decoder Capability Binding

## Scope

Review managed binding of AAC plan facts to one caller capability declaration.

## Findings

- Missing or disposed symbol coverage fails before declaration admission.
- Invalid ranges, codec mismatch, unsupported sample rate, and unsupported
  channel count remain distinct results.
- Successful binding retains exact declaration, coverage, plan, and provenance.
- Disposal clears the complete managed ownership chain.
- No runtime, module, ABI version, symbol address, decoder discovery, native
  call, context, or physical execution is accessed.

## Decision

Accept bounded declared AAC decoder-capability binding. Verified runtime
capability binding and all native decoder execution remain incomplete.
