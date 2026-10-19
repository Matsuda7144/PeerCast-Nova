namespace PeerCast.Nova.Foundation.Configuration;

public sealed record NovaOptions
{
    public string NodeName { get; init; } = Environment.MachineName;

    public int ListenPort { get; init; } = 7144;

    public bool EnableDetailedDiagnostics { get; init; }

    public bool AutoPortMapping { get; init; }

    public string? RecordingOutputDirectory { get; init; }
}
