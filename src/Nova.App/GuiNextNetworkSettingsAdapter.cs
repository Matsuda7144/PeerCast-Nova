namespace PeerCast.Nova.App;

/// <summary>
/// Borrowed typed settings boundary. No initialization, disposal, listener,
/// manager mutation or backend capability is exposed to a window.
/// </summary>
internal interface IGuiNextNetworkSettingsAdapter
{
    NovaApplicationNetworkSnapshot ReadSnapshot();
    Task<NovaApplicationNetworkPersistenceResult> SetAutoPortMappingAsync(bool enabled);
}

internal sealed class GuiNextNetworkSettingsAdapter : IGuiNextNetworkSettingsAdapter
{
    private readonly INovaApplicationNetworkOwner owner;

    public GuiNextNetworkSettingsAdapter(INovaApplicationNetworkOwner owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        this.owner = owner;
    }

    public NovaApplicationNetworkSnapshot ReadSnapshot() => owner.ReadSnapshot();

    public Task<NovaApplicationNetworkPersistenceResult> SetAutoPortMappingAsync(bool enabled) =>
        owner.SetAutoPortMappingAsync(enabled);
}
