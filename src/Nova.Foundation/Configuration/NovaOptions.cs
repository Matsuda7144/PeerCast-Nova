namespace PeerCast.Nova.Foundation.Configuration;

public sealed record NovaOptions
{
    public string NodeName { get; init; } = Environment.MachineName;

    public int ListenPort { get; init; } = 7144;

    public bool EnableDetailedDiagnostics { get; init; }
}
