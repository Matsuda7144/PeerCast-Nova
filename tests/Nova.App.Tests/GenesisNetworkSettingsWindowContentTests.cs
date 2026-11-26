using System.Net;
using System.Net.Sockets;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisNetworkSettingsWindowContentTests
{
    [Fact]
    public void UnavailableBackendProducesTruthfulStatusOnlyContent()
    {
        var viewModel = new GenesisNetworkSettingsViewModel(
            GenesisUnavailableNetworkSettingsController.Instance);

        var content = GenesisNetworkSettingsWindowContent.Create(viewModel);
        var text = string.Join(Environment.NewLine, content.StatusLines);

        Assert.Contains("Listener: Unavailable", content.StatusLines);
        Assert.Contains("Bound endpoint: —", content.StatusLines);
        Assert.Contains("Relay connections: 0", content.StatusLines);
        Assert.Contains("Direct listeners: Unavailable", content.StatusLines);
        Assert.Contains("IPv4 reachability: Unknown", content.StatusLines);
        Assert.Contains("IPv6 reachability: Unavailable", content.StatusLines);
        Assert.DoesNotContain("7144", text, StringComparison.Ordinal);
        Assert.Contains("unavailable", content.AvailabilityNote, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RefreshedViewModelProducesLatestRuntimeStatusContent()
    {
        var capabilities = new GenesisRelayNetworkCapabilities(
            SupportsSharedListener: true,
            SupportsLocalRelayPolicy: true,
            SupportsGlobalRelayPolicy: true,
            SupportsDirectPlay: true,
            SupportsAutomaticPortMapping: false,
            SupportsIpv6: false);
        var settings = new GenesisNetworkBasicSettings(
            port: 7144,
            ipv6Enabled: false,
            automaticPortMappingEnabled: false,
            allowLocalRelay: true,
            allowGlobalRelay: true);
        var controller = new MutableNetworkSettingsController(new(
            settings,
            new GenesisRelayNetworkStatusSnapshot(
                capabilities,
                GenesisRelayListenerRuntimeState.Stopped,
                activeRelayConnections: 1,
                relayConnectionLimit: 4,
                activeDirectListeners: 0)));
        var viewModel = new GenesisNetworkSettingsViewModel(controller);

        var initialContent = GenesisNetworkSettingsWindowContent.Create(viewModel);
        Assert.Contains("Listener: Stopped", initialContent.StatusLines);
        Assert.Contains("Relay connections: 1 / 4", initialContent.StatusLines);

        controller.Snapshot = new GenesisNetworkSettingsAppliedSnapshot(
            settings,
            new GenesisRelayNetworkStatusSnapshot(
                capabilities,
                GenesisRelayListenerRuntimeState.Listening,
                activeRelayConnections: 3,
                relayConnectionLimit: 4,
                addressFamily: AddressFamily.InterNetwork,
                boundAddress: IPAddress.Loopback,
                boundPort: 7144,
                activeDirectListeners: 2));

        viewModel.RefreshStatus();
        var refreshedContent = GenesisNetworkSettingsWindowContent.Create(viewModel);

        Assert.Contains("Listener: Listening", refreshedContent.StatusLines);
        Assert.Contains("Bound endpoint: 127.0.0.1:7144", refreshedContent.StatusLines);
        Assert.Contains("Relay connections: 3 / 4", refreshedContent.StatusLines);
        Assert.Contains("Direct listeners: 2", refreshedContent.StatusLines);
    }

    [Fact]
    public void ReopeningNetworkWindowRecreatesControlsAfterRuntimeRefresh()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "GenesisNetworkSettingsWindowHost.cs"));

        var refreshIndex = source.IndexOf(
            "_viewModel.RefreshStatus();",
            StringComparison.Ordinal);
        var disposeIndex = source.IndexOf(
            "await current.DisposeAsync().ConfigureAwait(false);",
            StringComparison.Ordinal);
        var createIndex = source.IndexOf(
            "GenesisNetworkSettingsWindow.TryCreate(_owner, _viewModel)",
            StringComparison.Ordinal);

        Assert.True(refreshIndex >= 0);
        Assert.True(disposeIndex > refreshIndex);
        Assert.True(createIndex > disposeIndex);
        Assert.DoesNotContain("current.Activate();", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(params string[] relativeParts) =>
        RepositorySourceTestHelper.FindRepositoryFile(relativeParts);

    private sealed class MutableNetworkSettingsController :
        IGenesisNetworkSettingsController
    {
        public MutableNetworkSettingsController(
            GenesisNetworkSettingsAppliedSnapshot snapshot)
        {
            Snapshot = snapshot;
        }

        public GenesisNetworkSettingsAppliedSnapshot Snapshot { get; set; }

        public ValueTask<GenesisNetworkSettingsApplyOutcome> ApplyAsync(
            GenesisNetworkBasicSettings settings,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(settings);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(GenesisNetworkSettingsApplyOutcome.Rejected);
        }
    }
}
