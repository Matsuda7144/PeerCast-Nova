using System.Globalization;
using PeerCast.Nova.Network.Publishing;

namespace PeerCast.Nova.App;

internal sealed record PublishingAnnouncePreset(string Name, string Endpoint, bool UsedFallbackPort);

internal sealed record PublishingNatSuggestionResult(bool Applied, PublishingSettingsInput UpdatedInput, string Message);

internal static class PublishingAdvancedAssist
{
    internal static bool TryBuildAnnouncePreset(
        GenesisYellowPageDefinition definition,
        out PublishingAnnouncePreset? preset)
    {
        ArgumentNullException.ThrowIfNull(definition);
        preset = null;

        string? host = null;
        int port;
        bool usedFallbackPort;

        if (definition.FallbackTrackerUri is { } tracker &&
            string.Equals(tracker.Scheme, "pcp", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(tracker.Host) &&
            tracker.HostNameType != UriHostNameType.IPv6)
        {
            host = tracker.Host;
            usedFallbackPort = tracker.Port <= 0;
            port = usedFallbackPort ? 7146 : tracker.Port;
        }
        else if (definition.IndexUri is { } index &&
            !string.IsNullOrWhiteSpace(index.Host) &&
            index.HostNameType != UriHostNameType.IPv6)
        {
            host = index.Host;
            usedFallbackPort = true;
            port = 7146;
        }
        else
        {
            return false;
        }

        var endpoint = $"pcp://{host}:{port}";
        var rawName = new string(definition.Name.Trim().Where(static ch => !char.IsControl(ch)).ToArray());
        if (rawName.Length == 0)
        {
            rawName = new string(definition.Id.Trim().Where(static ch => !char.IsControl(ch)).ToArray());
        }

        if (rawName.Length > GenesisYellowPageDefinition.MaximumNameCharacters)
        {
            rawName = rawName[..GenesisYellowPageDefinition.MaximumNameCharacters];
        }

        try
        {
            new PublishingAnnounceOptions(true, rawName, endpoint).Validate();
        }
        catch (ArgumentException)
        {
            return false;
        }

        preset = new(rawName, endpoint, usedFallbackPort);
        return true;
    }

    internal static PublishingNatSuggestionResult TryApplyNatSuggestion(
        PublishingSettingsInput input,
        NovaApplicationNetworkSnapshot? network,
        IReadOnlyList<ObsPublishLanInterface> interfaces,
        int fallbackListenPort)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(interfaces);

        var externalAddress = network?.Nat?.ExternalAddress?.Trim() ?? string.Empty;
        if (!PublishingIpv4.IsPublicAddress(externalAddress))
        {
            return new(
                false,
                input,
                "NATから公開IPv4を取得できていません。通信設定で自動ポート開放をONにするか、手動で入力してください。");
        }

        var internalPort = network!.Nat!.RegisteredInternalPorts.FirstOrDefault(static port => port is > 0 and <= 65535);
        var externalPort = network.Nat.ExternalPort is > 0 and <= 65535 ? network.Nat.ExternalPort.Value : 0;
        if (internalPort == 0 && int.TryParse(input.WanPort, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedWanPort) &&
            parsedWanPort is > 0 and <= 65535)
        {
            internalPort = parsedWanPort;
        }

        if (internalPort == 0)
        {
            internalPort = externalPort > 0
                ? externalPort
                : (fallbackListenPort is > 0 and <= 65535 ? fallbackListenPort : 7144);
        }

        if (externalPort == 0)
        {
            externalPort = int.TryParse(input.AdvertisedPort, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedAdvPort) &&
                parsedAdvPort is > 0 and <= 65535
                ? parsedAdvPort
                : internalPort;
        }

        var wanInterfaceId = input.WanInterfaceId;
        var wanAddress = input.WanAddress;
        var bindCandidates = interfaces
            .Where(static nic => nic.IsUp && PublishingIpv4.IsBindAddress(nic.Address))
            .ToArray();
        var internalAddress = network.Nat.Network?.InternalAddress?.Trim();
        var matchedByNat = !string.IsNullOrWhiteSpace(internalAddress)
            ? bindCandidates.FirstOrDefault(nic => string.Equals(nic.Address, internalAddress, StringComparison.Ordinal))
            : null;
        if (matchedByNat is not null)
        {
            wanInterfaceId = matchedByNat.Id;
            wanAddress = matchedByNat.Address;
        }
        else if ((string.IsNullOrWhiteSpace(wanAddress) ||
            !bindCandidates.Any(nic => nic.Id == wanInterfaceId && nic.Address == wanAddress)) &&
            bindCandidates.Length == 1)
        {
            wanInterfaceId = bindCandidates[0].Id;
            wanAddress = bindCandidates[0].Address;
        }

        var updated = input with
        {
            WanEnabled = true,
            LanEnabled = false,
            WanInterfaceId = wanInterfaceId,
            WanAddress = wanAddress,
            WanPort = internalPort.ToString(CultureInfo.InvariantCulture),
            AdvertisedAddress = externalAddress,
            AdvertisedPort = externalPort.ToString(CultureInfo.InvariantCulture),
        };
        return new(
            true,
            updated,
            $"NAT情報からWAN設定を自動入力しました（公開 {externalAddress}:{externalPort} / 内部ポート {internalPort}）。保存して反映してください。");
    }
}
