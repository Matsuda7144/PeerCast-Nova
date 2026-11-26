namespace PeerCast.Nova.App;

internal sealed class GenesisSourceTextProjection
{
    private string? _renderedSourceText;

    public string? Project(string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        return string.Equals(
                _renderedSourceText,
                sourceText,
                StringComparison.Ordinal)
            ? null
            : sourceText;
    }

    public void ObserveNativeEdit(string sourceText)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        _renderedSourceText = sourceText;
    }
}
