namespace PeerCast.Nova.App;

public sealed record GenesisChannelCatalogSnapshot(
    IReadOnlyList<GenesisChannelItem> Channels)
{
    public static GenesisChannelCatalogSnapshot Empty { get; } =
        new(Array.Empty<GenesisChannelItem>());
}

public interface IGenesisChannelCatalog
{
    ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
        CancellationToken cancellationToken = default);
}
