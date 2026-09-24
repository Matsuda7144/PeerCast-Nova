# PCN-0005 Solution and Project Structure

## Target

- .NET 10 (`net10.0`)
- C# language version selected by the pinned .NET SDK
- Central package version management
- Nullable reference types and warnings-as-errors policy

## Application and library projects

| Project | Type | Direct Nova references |
| --- | --- | --- |
| `Nova.Foundation` | Class library | none |
| `Nova.Core` | Class library | Foundation |
| `Nova.Protocol.Pcp` | Class library | none |
| `Nova.Network` | Class library | Core, Foundation, Protocol.Pcp |
| `Nova.Playback` | Class library | Network |
| `Nova.Windows` | Class library | Playback |
| `Nova.App` | Windows application host | Core, Network, Windows |
| `Nova.Cli` | Console and controlled-command host | Core, Network, Playback, Windows |
| `Nova.ControlledWindowsRunner` | Controlled Windows runner | Cli |

`Nova.App` is the small user-facing Windows entry point. `Nova.Cli` remains the development and controlled-command entry point. The controlled runner is not a second daily application host.

## Test projects

The solution includes `Nova.Foundation.Tests`, `Nova.Core.Tests`, `Nova.Protocol.Pcp.Tests`, `Nova.Network.Tests`, `Nova.Playback.Tests`, `Nova.Cli.Tests`, and `Nova.App.Tests`. Tests target the module boundary they exercise; routine solution tests do not perform physical Windows audio/video acceptance.

## Dependency direction

`Nova.Core` depends only on Foundation. `Nova.Network` implements Core contracts and owns peer/data-plane behavior. Playback consumes the Network packet boundary. `Nova.Windows` owns the shared Windows implementation and exposes it to the hosts. `Nova.App` and `Nova.Cli` are composition roots; UI code is not a dependency of Core or Network.

`Nova.Common` was removed by ADR-0016. Types stay with the module that owns their behavior until a stable, genuinely shared boundary is demonstrated.
