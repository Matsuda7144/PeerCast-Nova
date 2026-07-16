# PCN-0005 Solution and Project Structure

## Target

- .NET 10 LTS
- C# latest language version supported by the selected SDK
- Central package version management
- Warnings treated as errors
- Nullable reference types enabled

## Current Projects

| Project | Type | References |
|---|---|---|
| Nova.Foundation | Class Library | none |
| Nova.Core | Class Library | Foundation |
| Nova.Protocol.Pcp | Class Library | none |
| Nova.Network | Class Library | Core, Foundation, Protocol.Pcp |
| Nova.Playback | Class Library | Network |
| Nova.Cli | Console | Core, Network, Playback, Protocol.Pcp |
| Nova.Foundation.Tests | xUnit | Foundation |
| Nova.Core.Tests | xUnit | Core |
| Nova.Protocol.Pcp.Tests | xUnit | Protocol.Pcp |
| Nova.Network.Tests | xUnit | Network |
| Nova.Playback.Tests | xUnit | Playback |
| Nova.Cli.Tests | xUnit | CLI |

`Nova.Common` was removed by ADR-0016. Types remain with the module that owns
their behavior until a stable, genuinely shared boundary is demonstrated.

## Deferred Projects

Decoder backend, Renderer, Recorder, Plugin, API, and Windows UI projects are
deliberately deferred until their interfaces are justified by an implemented
use case. The initial bounded player-input policy now belongs to
`Nova.Playback`; decoder and renderer types remain deferred.
