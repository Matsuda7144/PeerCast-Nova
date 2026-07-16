namespace PeerCast.Nova.Core;

public sealed record NovaApplicationOptions
{
    public TimeSpan ModuleStopTimeout { get; init; } = TimeSpan.FromSeconds(10);

    internal void Validate()
    {
        if (ModuleStopTimeout <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                $"{nameof(ModuleStopTimeout)} must be positive.");
        }
    }
}
