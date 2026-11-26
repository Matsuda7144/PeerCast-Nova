namespace PeerCast.Nova.App;

/// <summary>
/// Persistent application-facing definition for one channel Yellow Page.
/// Network reading remains behind the channel-catalog boundary.
/// </summary>
public sealed record GenesisYellowPageDefinition
{
    internal const int MaximumIdCharacters = 64;
    internal const int MaximumNameCharacters = 128;
    internal const int MaximumNamespaceCharacters = 128;

    public GenesisYellowPageDefinition(
        string id,
        string name,
        Uri indexUri,
        Uri? fallbackTrackerUri = null,
        bool isEnabled = true,
        string? yellowPageNamespace = null)
    {
        Id = NormalizeRequiredText(
            id,
            nameof(id),
            MaximumIdCharacters,
            "Yellow Page id");
        Name = NormalizeRequiredText(
            name,
            nameof(name),
            MaximumNameCharacters,
            "Yellow Page name");
        ArgumentNullException.ThrowIfNull(indexUri);
        ValidateIndexUri(indexUri);
        ValidateFallbackTrackerUri(fallbackTrackerUri);

        IndexUri = indexUri;
        FallbackTrackerUri = fallbackTrackerUri;
        IsEnabled = isEnabled;
        Namespace = NormalizeOptionalText(
            yellowPageNamespace,
            nameof(yellowPageNamespace),
            MaximumNamespaceCharacters,
            "Yellow Page namespace");
    }

    public string Id { get; }

    public string Name { get; }

    public Uri IndexUri { get; }

    public Uri? FallbackTrackerUri { get; }

    public bool IsEnabled { get; init; }

    public string? Namespace { get; }

    private static string NormalizeRequiredText(
        string value,
        string parameterName,
        int maximumCharacters,
        string displayName)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException($"{displayName} is required.", parameterName);
        }

        if (normalized.Length > maximumCharacters)
        {
            throw new ArgumentException($"{displayName} is too long.", parameterName);
        }

        return normalized;
    }

    private static string? NormalizeOptionalText(
        string? value,
        string parameterName,
        int maximumCharacters,
        string displayName)
    {
        if (value is null)
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        if (normalized.Length > maximumCharacters)
        {
            throw new ArgumentException($"{displayName} is too long.", parameterName);
        }

        return normalized;
    }

    private static void ValidateIndexUri(Uri indexUri)
    {
        if (!indexUri.IsAbsoluteUri ||
            (indexUri.Scheme != Uri.UriSchemeHttp &&
                indexUri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(indexUri.Host))
        {
            throw new ArgumentException(
                "The Yellow Page index URI must be an absolute HTTP or HTTPS URI.",
                nameof(indexUri));
        }
    }

    private static void ValidateFallbackTrackerUri(Uri? fallbackTrackerUri)
    {
        if (fallbackTrackerUri is null)
        {
            return;
        }

        if (!fallbackTrackerUri.IsAbsoluteUri ||
            !string.Equals(
                fallbackTrackerUri.Scheme,
                "pcp",
                StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(fallbackTrackerUri.Host))
        {
            throw new ArgumentException(
                "The Yellow Page fallback tracker URI must be an absolute pcp:// URI.",
                nameof(fallbackTrackerUri));
        }
    }
}

internal static class GenesisYellowPageDefaults
{
    public static GenesisYellowPageDefinition Sp { get; } = new(
        id: "sp",
        name: "SP",
        indexUri: SpGenesisChannelCatalog.IndexUri,
        fallbackTrackerUri: SpGenesisChannelCatalog.TrackerUri,
        isEnabled: true,
        yellowPageNamespace: "sp");
}
