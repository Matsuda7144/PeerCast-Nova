# PCN-0004 System Architecture

## Dependency rule

Composition stays at the host boundary. Core remains UI independent; protocol and network code do not depend on a UI or decoder. The Windows host owns operating-system resources, while Playback borrows its public output boundary.

## Current project responsibilities

| Project | Responsibility |
| --- | --- |
| `Nova.Foundation` | Shared low-level contracts such as clocks, identifiers, and configuration. |
| `Nova.Core` | UI-independent lifecycle, channel control state, use cases, and immutable status. |
| `Nova.Protocol.Pcp` | PCP wire codecs without socket, UI, or application-lifecycle ownership. |
| `Nova.Network` | Peer connections, HTTP/PCP sessions, bounded channel input, relay flow, and backpressure. |
| `Nova.Playback` | Bounded playback input and managed/native decode, scheduling, and output contracts. |
| `Nova.Windows` | Shared Win32 window, message loop, owner thread, bounded dispatcher, and Windows resource cleanup. |
| `Nova.App` | Minimal Windows user-facing composition for channel playback and PCP relay. |
| `Nova.Cli` | Development and controlled command entry point. |

## Host and module boundaries

- `Nova.App` composes Core, Network, and the shared Windows host.
- `Nova.Cli` composes Core, Network, Playback, and the shared Windows host.
- `Nova.Windows` depends on the Playback output contract; Playback does not own the Win32 window or message loop.
- Network implements Core lifecycle/control contracts and does not depend on Playback or UI.
- `Nova.Network` may expose payload through bounded data-plane interfaces. Media payload does not pass through Core status snapshots.
- Foundation has no dependency on other Nova projects. PCP codecs remain deterministic and transport independent.

## Current user-facing flows

The Windows app offers two accepted MVP flows. Playback receives a selected PCP channel and exposes a loopback-only HTTP-FLV stream to a compatible external player, including PCRPlayer and DSPlayer. Relay starts a bounded PCP relay listener and displays the LAN endpoint for another peer. The separate Nova-managed decoder／scheduler／Windows output route and regular MP4 integration are post-MVP additions, not MVP blockers.

## Rules

- UI code does not open peer sockets directly; hosts call module boundaries.
- Control status and media payload use separate paths.
- Every consumer has an explicit queue/backpressure and cancellation policy.
- Ownership transfer, finite shutdown, and cleanup are explicit.
- Public interfaces stay small until a concrete use case requires more.
