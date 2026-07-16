# PCN-0004 System Architecture

## Dependency Rule

Composition stays at the host boundary. Project references point only toward
the contracts and deterministic codecs they consume:

```text
                    Nova.Cli / Future Host
                      |       |       |
                      v       v       v
                Nova.Core  Nova.Playback ---> Nova.Network
                    |                         |       |
                    v                         v       v
             Nova.Foundation          Nova.Core  Nova.Protocol.Pcp
```

`Nova.Core` does not reference the concrete network implementation. The host
constructs both, while `Nova.Network` implements Core lifecycle/control
contracts. `Nova.Protocol.Pcp` is a deterministic codec with no Nova project
dependency.

`Nova.Playback` depends on the Network packet boundary for its initial PCP
adapter. Network does not reference Playback, a decoder, renderer, or UI.

## Module Responsibilities

### Nova.Foundation

Logging, configuration contracts, clocks, identifiers, and other
lowest-level infrastructure abstractions. It depends on no Nova module.

### Nova.Core

Application lifecycle, channel control-plane state, and use-case orchestration.
It is UI-independent and never transports media payload bytes.

### Nova.Network

Peer connections, protocol framing, relay flow, timeouts, and bandwidth
management. It must not depend on Player or UI.

### Nova.Playback

Bounded player input ownership, latency-oriented skip policy, and future media
format/decoder/renderer orchestration. It never places payload bytes in Core
and cannot make Network depend on a media backend.

### Nova.Cli

Development and diagnostics entry point. It is replaceable by future UIs.

## Rules

- UI does not call sockets directly.
- Network does not know the UI or media decoder.
- Core coordinates modules through interfaces.
- Foundation has no dependency on other Nova projects.
- Protocol codecs do not know sockets, UI, logging, or application lifecycle.
- Channel status snapshots are immutable control-plane objects.
- Media payload ownership and backpressure stay in the Network data plane.
- Public APIs remain minimal until the protocol stabilizes.
