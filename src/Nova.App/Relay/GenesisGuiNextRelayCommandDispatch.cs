using PeerCast.Nova.Core.Channels;

namespace PeerCast.Nova.App;

public sealed partial class GenesisMainViewModel
{
    internal GenesisRelayTreeSnapshot ReadRelayTree(GenesisChannelItem? channel) =>
        GenesisGuiNextRelayActivation.TryResolve(channel, out var id) && id is not null
            ? _relayActivation?.ReadRelayTree(id) ?? GenesisRelayTreeSnapshot.Empty(id)
            : GenesisRelayTreeSnapshot.Empty();

    internal GenesisRelayTreeSnapshot ReadRelayTreeById(ChannelId? channelId) =>
        channelId is not null
            ? _relayActivation?.ReadRelayTree(channelId)
                ?? PublicChannels?.Registry.ReadRelayTree(channelId)
                ?? GenesisRelayTreeSnapshot.Empty(channelId)
            : GenesisRelayTreeSnapshot.Empty();

    internal IReadOnlyList<ChannelId> ActiveRelayChannelIds() =>
        _relayActivation?.ActiveChannelIds()
        ?? PublicChannels?.Registry.ActiveChannelIds()
        ?? [];

    private GenesisGuiNextRelayActivation? _relayActivation;
    internal GenesisGuiNextRelayActivation? RelayActivation
    {
        get => _relayActivation;
        set
        {
            if (ReferenceEquals(_relayActivation, value)) return;
            if (_relayActivation is not null) _relayActivation.Changed -= OnRelayChanged;
            _relayActivation = value;
            if (value is not null) value.Changed += OnRelayChanged;
            OnPropertyChanged(nameof(RelaySnapshot));
        }
    }
    internal GenesisRelayActivationSnapshot RelaySnapshot => _relayActivation?.Snapshot ?? new();
    internal void RefreshRelayStatus()
    {
        if (_relayActivation is not null) OnPropertyChanged(nameof(RelaySnapshot));
    }
    private void OnRelayChanged(object? sender, EventArgs args) => OnPropertyChanged(nameof(RelaySnapshot));
}

/// <summary>Shared native-command and managed integration boundary. Never calls Play/Stop.</summary>
internal static class GenesisGuiNextRelayCommandDispatch
{
    public static bool CanStart(GenesisMainViewModel model, GenesisGuiNextNativeTabRow? row) =>
        model.RelayActivation?.CanStart == true && row?.IsInteractive == true &&
        GenesisGuiNextRelayActivation.TryResolve(row.InteractiveChannel, out _);

    public static Task<bool> StartAsync(GenesisMainViewModel model, GenesisGuiNextNativeTabRow? row,
        CancellationToken cancellationToken = default) => model.RelayActivation is { } activation
            ? activation.StartAsync(row?.IsInteractive == true ? row.InteractiveChannel : null, cancellationToken)
            : Task.FromResult(false);

    public static Task StopAsync(GenesisMainViewModel model) => model.RelayActivation?.StopAsync() ?? Task.CompletedTask;
}
