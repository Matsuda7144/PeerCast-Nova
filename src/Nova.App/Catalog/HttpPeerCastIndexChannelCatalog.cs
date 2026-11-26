namespace PeerCast.Nova.App;

internal interface IPeerCastIndexTextSource
{
    ValueTask<string> ReadAsync(
        Uri indexUri,
        CancellationToken cancellationToken = default);
}

internal sealed record PeerCastIndexTextReadResult(
    string Text,
    int? HttpStatusCode,
    int? ResponseBodyByteCount);

internal interface IPeerCastIndexDiagnosticTextSource : IPeerCastIndexTextSource
{
    ValueTask<PeerCastIndexTextReadResult> ReadWithDiagnosticsAsync(
        Uri indexUri,
        CancellationToken cancellationToken = default);
}

internal sealed class HttpPeerCastIndexChannelCatalog : IGenesisChannelCatalog
{
    private readonly Uri _indexUri;
    private readonly Uri? _fallbackTrackerUri;
    private readonly IPeerCastIndexTextSource _textSource;

    public HttpPeerCastIndexChannelCatalog(
        Uri indexUri,
        Uri? fallbackTrackerUri,
        IPeerCastIndexTextSource textSource)
    {
        ArgumentNullException.ThrowIfNull(indexUri);
        ArgumentNullException.ThrowIfNull(textSource);
        ValidateHttpIndexUri(indexUri);
        ValidateTrackerUri(fallbackTrackerUri);

        _indexUri = indexUri;
        _fallbackTrackerUri = fallbackTrackerUri;
        _textSource = textSource;
    }

    public async ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        var context = GenesisYellowPageRefreshDiagnosticAmbient.Current;
        var trace = context?.Trace;
        var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        var detailedSource = trace is not null &&
            _textSource is IPeerCastIndexDiagnosticTextSource;
        trace?.TryRecord(
            detailedSource ? "HttpReadStarted" : "TextSourceReadStarted",
            context,
            stage: detailedSource ? "Http" : "TextSource",
            outcome: "Started");
        string indexText;
        int? statusCode = null;
        int? responseBodyByteCount = null;
        try
        {
            if (detailedSource)
            {
                var response = await ((IPeerCastIndexDiagnosticTextSource)_textSource)
                    .ReadWithDiagnosticsAsync(_indexUri, cancellationToken)
                    .ConfigureAwait(false);
                indexText = response.Text;
                statusCode = response.HttpStatusCode;
                responseBodyByteCount = response.ResponseBodyByteCount;
            }
            else
            {
                indexText = await _textSource.ReadAsync(_indexUri, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            if (!detailedSource)
            {
                trace?.TryRecord(
                    "TextSourceReadFailed",
                    context,
                    stage: "TextSource",
                    outcome: cancellationToken.IsCancellationRequested ? "Cancelled" : "Failed",
                    exceptionType: exception.GetType().Name,
                    failureStage: "Read");
            }

            throw;
        }

        trace?.TryRecord(
            detailedSource ? "HttpReadSucceeded" : "TextSourceReadCompleted",
            context,
            stage: detailedSource ? "Http" : "TextSource",
            outcome: detailedSource ? "Succeeded" : "ReturnedWithoutHttpEvidence",
            httpStatusCode: detailedSource ? statusCode : null,
            responseByteCount: detailedSource ? responseBodyByteCount : null,
            durationMilliseconds: ElapsedMilliseconds(startedAt));
        cancellationToken.ThrowIfCancellationRequested();
        var inputRows = CountNonEmptyRows(indexText);
        var parserStartedAt = System.Diagnostics.Stopwatch.GetTimestamp();
        trace?.TryRecord(
            "CatalogParseStarted",
            context,
            stage: "Parser",
            outcome: "Started",
            inputRowCount: inputRows);
        try
        {
            var parsed = PeerCastIndexChannelCatalogParser.Parse(indexText, _fallbackTrackerUri);
            var parsedRows = parsed.Channels.Count;
            trace?.TryRecord(
                "CatalogParseCompleted",
                context,
                stage: "Parser",
                outcome: "Completed",
                inputRowCount: inputRows,
                parsedRowCount: parsedRows,
                skippedRowCount: Math.Max(0, inputRows - parsedRows),
                durationMilliseconds: ElapsedMilliseconds(parserStartedAt));
            return parsed;
        }
        catch (Exception exception)
        {
            trace?.TryRecord(
                "CatalogParseFailed",
                context,
                stage: "Parser",
                outcome: "Failed",
                inputRowCount: inputRows,
                exceptionType: exception.GetType().Name,
                failureStage: "Parser");
            throw;
        }
    }

    private static int CountNonEmptyRows(string text)
    {
        var count = 0;
        var rowStart = 0;
        for (var index = 0; index <= text.Length; index++)
        {
            if (index != text.Length && text[index] != '\n')
            {
                continue;
            }

            if (!text.AsSpan(rowStart, index - rowStart).Trim().IsEmpty)
            {
                count++;
            }

            rowStart = index + 1;
        }

        return count;
    }

    private static long ElapsedMilliseconds(long startedAt) =>
        (long)System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds;

    private static void ValidateHttpIndexUri(Uri indexUri)
    {
        if (!indexUri.IsAbsoluteUri ||
            (indexUri.Scheme != Uri.UriSchemeHttp &&
                indexUri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(indexUri.Host))
        {
            throw new ArgumentException(
                "The channel index URI must be an absolute HTTP or HTTPS URI.",
                nameof(indexUri));
        }
    }

    private static void ValidateTrackerUri(Uri? trackerUri)
    {
        if (trackerUri is null)
        {
            return;
        }

        if (!trackerUri.IsAbsoluteUri ||
            !string.Equals(trackerUri.Scheme, "pcp", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(trackerUri.Host))
        {
            throw new ArgumentException(
                "The fallback tracker URI must be an absolute pcp:// URI.",
                nameof(trackerUri));
        }
    }
}

internal static class GenesisYellowPageCatalogFactory
{
    public static IGenesisChannelCatalog Create(GenesisYellowPageDefinition definition) =>
        Create(definition, SystemPeerCastIndexTextSource.Instance);

    internal static IGenesisChannelCatalog Create(
        GenesisYellowPageDefinition definition,
        IPeerCastIndexTextSource textSource)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(textSource);
        return new HttpPeerCastIndexChannelCatalog(
            definition.IndexUri,
            definition.FallbackTrackerUri,
            textSource);
    }
}

internal static class SpGenesisChannelCatalog
{
    public static Uri IndexUri { get; } = new("http://bayonet.ddo.jp/sp/index.txt");

    public static Uri TrackerUri { get; } = new("pcp://bayonet.ddo.jp:7146/");

    public static IGenesisChannelCatalog Create() =>
        new GenesisGuiNextYellowPageChannelCatalogRouter(
            new GenesisAggregatingYellowPageChannelCatalog(
                GenesisYellowPageConfiguration.CreateDefault()));

    public static IGenesisChannelCatalog Create(IPeerCastIndexTextSource textSource)
    {
        ArgumentNullException.ThrowIfNull(textSource);
        return GenesisYellowPageCatalogFactory.Create(GenesisYellowPageDefaults.Sp, textSource);
    }
}
