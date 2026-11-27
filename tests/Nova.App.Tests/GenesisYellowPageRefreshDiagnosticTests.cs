using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisYellowPageRefreshDiagnosticTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] ExpectedTriggers = ["Initial", "Manual", "Automatic"];
    private static readonly string[] ExpectedProviderIds = ["provider-1", "provider-2"];

    [Fact]
    public async Task LifetimeMappingLimitDropsNewProvidersButPreservesExistingCorrelation()
    {
        using var output = new MemoryStream();
        var limit = GenesisYellowPageRefreshDiagnosticTrace.MaximumProviderMappings;
        Assert.Equal(4096, limit);
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(
            output, maximumRecords: limit + 8, queueCapacity: limit + 8);
        var context = new GenesisYellowPageRefreshDiagnosticContext(
            trace, Guid.NewGuid(), GenesisYellowPageRefreshTrigger.Manual);
        for (var index = 0; index < limit; index++)
        {
            Assert.True(trace.TryRecord("ProviderReadStarted", context.ForProvider(PrivateProviderId(index))));
        }

        Assert.Equal(limit, trace.ProviderMappingCount);
        Assert.False(trace.TryRecord("ProviderReadStarted", context.ForProvider("secret-marker-overflow")));
        Assert.Equal(limit, trace.ProviderMappingCount);
        Assert.True(trace.TryRecord("ProviderReadSucceeded", context.ForProvider(PrivateProviderId(0))));
        Assert.True(trace.TryRecord("ProviderReadSucceeded", context.ForProvider(PrivateProviderId(limit - 1))));
        await trace.DisposeAsync();
        Assert.Equal(0, trace.ProviderMappingCount);
        var events = ReadEvents(output);
        var successes = events.Where(item => item.EventType == "ProviderReadSucceeded").ToArray();
        Assert.Equal("provider-1", successes[0].ProviderId);
        Assert.Equal("provider-4096", successes[1].ProviderId);
        Assert.Equal(1, trace.DroppedEvents);
        var closing = Assert.Single(events, item => item.EventType == "DiagnosticsClosed");
        Assert.Equal(1, closing.DroppedEvents);
        Assert.Equal("FlushedWithDrops", closing.Outcome);
        var json = Encoding.UTF8.GetString(output.ToArray());
        Assert.DoesNotContain("private-source", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-marker", json, StringComparison.Ordinal);

        using var nextOutput = new MemoryStream();
        var next = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(nextOutput);
        Assert.True(next.TryRecord("ProviderReadStarted",
            new GenesisYellowPageRefreshDiagnosticContext(next, Guid.NewGuid(), GenesisYellowPageRefreshTrigger.Initial)
                .ForProvider("secret-marker-overflow")));
        await next.DisposeAsync();
        Assert.Equal("provider-1", Assert.Single(ReadEvents(nextOutput), item => item.ProviderId is not null).ProviderId);
    }

    [Fact]
    public async Task LifetimeConcurrentMappingRegistrationCannotExceedLimit()
    {
        using var output = new MemoryStream();
        var limit = GenesisYellowPageRefreshDiagnosticTrace.MaximumProviderMappings;
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(
            output, maximumRecords: limit + 2, queueCapacity: limit + 2);
        var context = new GenesisYellowPageRefreshDiagnosticContext(
            trace, Guid.NewGuid(), GenesisYellowPageRefreshTrigger.Automatic);
        var accepted = 0;
        Parallel.For(0, limit * 2, index =>
        {
            if (trace.TryRecord("ProviderReadStarted", context.ForProvider(PrivateProviderId(index))))
            {
                Interlocked.Increment(ref accepted);
            }

            Assert.InRange(trace.ProviderMappingCount, 0, limit);
        });
        Assert.Equal(limit, accepted);
        Assert.Equal(limit, trace.ProviderMappingCount);
        await trace.DisposeAsync();
        var events = ReadEvents(output);
        Assert.Equal(limit, events.Where(item => item.ProviderId is not null).Select(item => item.ProviderId).Distinct().Count());
        Assert.Equal(limit, trace.DroppedEvents);
        Assert.Equal(limit, Assert.Single(events, item => item.EventType == "DiagnosticsClosed").DroppedEvents);
        Assert.Equal(0, trace.ProviderMappingCount);
    }

    [Fact]
    public async Task LifetimeRecordCapDoesNotAllowUnboundedProviderMappingGrowth()
    {
        using var output = new MemoryStream();
        var limit = GenesisYellowPageRefreshDiagnosticTrace.MaximumProviderMappings;
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(
            output, maximumRecords: 2, queueCapacity: limit + 32);
        var context = new GenesisYellowPageRefreshDiagnosticContext(
            trace, Guid.NewGuid(), GenesisYellowPageRefreshTrigger.Manual);
        var attempts = limit + 16;
        for (var index = 0; index < attempts; index++)
        {
            _ = trace.TryRecord("ProviderReadStarted", context.ForProvider(PrivateProviderId(index)));
        }

        Assert.Equal(limit, trace.ProviderMappingCount);
        await trace.DisposeAsync();
        var events = ReadEvents(output);
        Assert.Equal(2, events.Count);
        var closing = Assert.Single(events, item => item.EventType == "DiagnosticsClosed");
        Assert.Equal(attempts - 1, trace.DroppedEvents);
        Assert.Equal(trace.DroppedEvents, closing.DroppedEvents);
        Assert.Equal("FlushedWithDrops", closing.Outcome);
        Assert.Equal(0, trace.ProviderMappingCount);
    }

    [Fact]
    public async Task LifetimeMappingExhaustionDoesNotChangeYellowPageResults()
    {
        using var output = new MemoryStream();
        var limit = GenesisYellowPageRefreshDiagnosticTrace.MaximumProviderMappings;
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(
            output, maximumRecords: limit + 32, queueCapacity: limit + 32);
        var context = new GenesisYellowPageRefreshDiagnosticContext(
            trace, Guid.NewGuid(), GenesisYellowPageRefreshTrigger.Initial);
        for (var index = 0; index < limit; index++)
        {
            Assert.True(trace.TryRecord("ProviderReadStarted", context.ForProvider(PrivateProviderId(index))));
        }

        var configuration = new GenesisYellowPageConfiguration([
            Definition("secret-marker-new-provider", "http://127.0.0.1/index.txt"),
        ]);
        var aggregate = new GenesisAggregatingYellowPageChannelCatalog(configuration,
            new DelegateCatalogFactory(item => GenesisYellowPageCatalogFactory.Create(item,
                new FixedTextSource(BuildIndexRow("0123456789ABCDEF0123456789ABCDEF")))));
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var session = GenesisChannelCatalogSession.Create(
            aggregate, viewModel, EmptyGenesisChannelPresenceNotificationPublisher.Instance, trace);
        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, await session.RefreshAsync());
        Assert.Single(viewModel.Channels);
        Assert.Equal(limit, trace.ProviderMappingCount);
        await session.DisposeAsync();
        Assert.True(trace.DroppedEvents > 0);
        Assert.DoesNotContain(ReadEvents(output), item => item.EventType == "ProviderReadSucceeded");
        Assert.DoesNotContain("secret-marker", Encoding.UTF8.GetString(output.ToArray()), StringComparison.Ordinal);
        Assert.Equal(0, trace.ProviderMappingCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LifetimeWriterFailureRejectsLateEventsAndCountsEveryLoss(bool failOnFlush)
    {
        using var output = new GatedDiagnosticStream(failWrites: !failOnFlush, failFlush: failOnFlush);
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(output, queueCapacity: 64);
        var context = new GenesisYellowPageRefreshDiagnosticContext(
            trace, Guid.NewGuid(), GenesisYellowPageRefreshTrigger.Manual).ForProvider("secret-marker");
        Assert.True(trace.TryRecord("ProviderReadStarted", context));
        await output.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        for (var index = 0; index < 32; index++)
        {
            Assert.True(trace.TryRecord("ProviderReadStarted", context));
        }

        output.ReleaseWrite.TrySetResult();
        await trace.WriterCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(trace.WriterFailed);
        Assert.Equal(33, trace.DroppedEvents);
        Assert.Equal(0, trace.ProviderMappingCount);
        for (var index = 0; index < 64; index++)
        {
            Assert.False(trace.TryRecord("ProviderReadStarted", context));
        }

        Assert.Equal(97, trace.DroppedEvents);
        Assert.Equal(failOnFlush ? 2 : 1, output.WriteCalls);
        Assert.DoesNotContain("DiagnosticsClosed", Encoding.UTF8.GetString(output.ToArray()), StringComparison.Ordinal);
        await trace.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, trace.ProviderMappingCount);
    }

    [Fact]
    public async Task LifetimeWriterFailureRacingProducersAccountsForAcceptedAndRejectedEvents()
    {
        using var output = new GatedDiagnosticStream(failWrites: true);
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(output, queueCapacity: 32);
        Assert.True(trace.TryRecord("ProviderReadStarted"));
        await output.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        const int attempts = 1000;
        var producers = Task.Run(() => Parallel.For(0, attempts, _ => trace.TryRecord("ProviderReadStarted")));
        output.ReleaseWrite.TrySetResult();
        await producers.WaitAsync(TimeSpan.FromSeconds(5));
        await trace.WriterCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(trace.WriterFailed);
        Assert.Equal(attempts + 1, trace.DroppedEvents);
        Assert.False(trace.TryRecord("ProviderReadStarted"));
        Assert.Equal(attempts + 2, trace.DroppedEvents);
        Assert.Equal(1, output.WriteCalls);
        await trace.DisposeAsync();
    }

    [Fact]
    public async Task LifetimeDisposeClearsMappingEvenWhileIoIsBlocked()
    {
        using var output = new GatedDiagnosticStream(failWrites: false);
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(output);
        var context = new GenesisYellowPageRefreshDiagnosticContext(
            trace, Guid.NewGuid(), GenesisYellowPageRefreshTrigger.Manual).ForProvider("secret-marker");
        Assert.True(trace.TryRecord("ProviderReadStarted", context));
        await output.WriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            await trace.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(trace.WriterCompletion.IsCompleted);
            Assert.Equal(0, trace.ProviderMappingCount);
            Assert.False(trace.TryRecord("ProviderReadStarted", context));
        }
        finally
        {
            output.ReleaseWrite.TrySetResult();
            await trace.WriterCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal("Flushed", Assert.Single(ReadEvents(output), item => item.EventType == "DiagnosticsClosed").Outcome);
    }

    [Fact]
    public async Task LifetimeDisposeRacingProducersCannotRepopulateMapping()
    {
        using var output = new MemoryStream();
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(output);
        var context = new GenesisYellowPageRefreshDiagnosticContext(
            trace, Guid.NewGuid(), GenesisYellowPageRefreshTrigger.Manual);
        var producers = Task.Run(() => Parallel.For(0, 1000, index =>
            trace.TryRecord("ProviderReadStarted", context.ForProvider(PrivateProviderId(index)))));
        await trace.DisposeAsync();
        await producers.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, trace.ProviderMappingCount);
        Assert.False(trace.TryRecord("ProviderReadStarted", context.ForProvider("private-late")));
        Assert.Equal(0, trace.ProviderMappingCount);
    }

    private static string PrivateProviderId(int index) =>
        "private-source-" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("https://example.invalid/?token=secret-marker", true)]
    [InlineData("api-key=secret-marker", true)]
    [InlineData("https://example.invalid/?token=secret-marker", false)]
    [InlineData("api-key=secret-marker", false)]
    public async Task PrivacyConfiguredIdsNeverEnterJsonl(string providerId, bool enabled)
    {
        var definition = Definition(providerId, "https://example.invalid/?token=secret-marker");
        var configuration = new GenesisYellowPageConfiguration([definition]);
        var body = BuildIndexRow("0123456789ABCDEF0123456789ABCDEF")
            .Replace("DiagnosticFixture", "Credential-secret-marker", StringComparison.Ordinal);
        var factory = new DelegateCatalogFactory(item => item.Id == providerId
            ? GenesisYellowPageCatalogFactory.Create(item, new FixedTextSource(body))
            : new ThrowingCatalog(new InvalidOperationException("Exception message secret-marker")));
        var aggregate = new GenesisAggregatingYellowPageChannelCatalog(configuration, factory);
        using var output = new MemoryStream();
        var trace = enabled ? GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(output) : null;
        var requests = new List<Guid>();
        foreach (var trigger in Enum.GetValues<GenesisYellowPageRefreshTrigger>())
        {
            if (trigger == GenesisYellowPageRefreshTrigger.Manual)
            {
                Assert.True(configuration.Add(Definition("Credential=secret-marker", "https://example.invalid/")));
            }

            var requestId = Guid.NewGuid();
            requests.Add(requestId);
            using var scope = GenesisYellowPageRefreshDiagnosticAmbient.Push(trace is null
                ? null
                : new GenesisYellowPageRefreshDiagnosticContext(trace, requestId, trigger));
            var result = await aggregate.ReadDetailedAsync();
            Assert.Single(result.Catalog.Channels);
        }

        if (trace is not null)
        {
            await trace.DisposeAsync();
            var events = ReadEvents(output);
            var successes = events.Where(item => item.EventType == "ProviderReadSucceeded").ToArray();
            Assert.Equal(3, successes.Length);
            Assert.All(successes, item => Assert.Equal("provider-1", item.ProviderId));
            Assert.Equal(requests, successes.Select(item => item.RefreshRequestId!.Value));
            Assert.Equal(ExpectedTriggers, successes.Select(item => item.Trigger));
            Assert.Equal(new long?[] { 0, 1, 1 }, successes.Select(item => item.ConfigurationRevision));
            Assert.Equal(3, successes.Select(item => item.RefreshAttemptId).Distinct().Count());
            Assert.All(successes, item => Assert.NotNull(item.RefreshAttemptId));
            var failures = events.Where(item => item.EventType == "ProviderReadFailed").ToArray();
            Assert.Equal(2, failures.Length);
            Assert.All(failures, item =>
            {
                Assert.Equal("provider-2", item.ProviderId);
                Assert.Equal(nameof(InvalidOperationException), item.ExceptionType);
            });
            foreach (var success in successes)
            {
                var correlated = events.Where(item => item.RefreshAttemptId == success.RefreshAttemptId &&
                    item.ProviderId == success.ProviderId).ToArray();
                Assert.Contains(correlated, item => item.EventType == "HttpReadSucceeded" && item.HttpStatusCode == 200);
                Assert.Contains(correlated, item => item.EventType == "CatalogParseCompleted");
                Assert.All(correlated, item => Assert.Equal(success.RefreshRequestId, item.RefreshRequestId));
            }
        }
        else
        {
            Assert.Empty(output.ToArray());
        }

        var json = Encoding.UTF8.GetString(output.ToArray());
        foreach (var sensitive in new[]
                 {
                     providerId, "https://", "example.invalid", "token", "secret-marker",
                     "Credential", "Query", body, "Exception message",
                 })
        {
            Assert.DoesNotContain(sensitive, json, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task PrivacyHttpFailureDoesNotSerializeQueryOrResponseBody()
    {
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        const string body = "Credential response body secret-marker";
        var server = ServeHttpOnceAsync(listener, "HTTP/1.0 404 Not Found", body, cancellation.Token);
        try
        {
            var events = await ReadHttpFailureAsync(
                new Uri($"http://127.0.0.1:{endpoint.Port}/?token=secret-marker"), server);
            var failure = Assert.Single(events, item => item.EventType == "HttpReadFailed");
            Assert.Equal(404, failure.HttpStatusCode);
            Assert.Equal("DecodeResponse", failure.FailureStage);
            Assert.Equal("provider-1", failure.ProviderId);
            Assert.NotNull(failure.RefreshAttemptId);
            Assert.NotNull(failure.RefreshRequestId);
            var json = JsonSerializer.Serialize(events, JsonOptions);
            Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret-marker", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Credential", json, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(body, json, StringComparison.Ordinal);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task PrivacyConcurrentProvidersKeepStableDistinctOpaqueIds()
    {
        using var output = new MemoryStream();
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(output);
        var context = new GenesisYellowPageRefreshDiagnosticContext(
            trace, Guid.NewGuid(), GenesisYellowPageRefreshTrigger.Manual);
        var requests = new[] { Guid.NewGuid(), Guid.NewGuid() };
        Parallel.For(0, 64, index =>
        {
            // A configured ID that looks like an opaque ID must not collide.
            var provider = (context with { RefreshRequestId = requests[index % 2] })
                .ForProvider(index % 2 == 0 ? "provider-1" : "api-key=secret-marker");
            Assert.True(trace.TryRecord("ProviderReadStarted", provider, stage: "Provider"));
            Assert.True(trace.TryRecord("ProviderReadSucceeded", provider, stage: "Provider"));
        });
        await trace.DisposeAsync();

        var events = ReadEvents(output).Where(item => item.ProviderId is not null).ToArray();
        Assert.Equal(128, events.Length);
        Assert.Equal(ExpectedProviderIds, events.Select(item => item.ProviderId).Distinct().Order());
        Assert.All(events.GroupBy(item => item.RefreshRequestId), group =>
            Assert.Single(group.Select(item => item.ProviderId).Distinct()));
        foreach (var provider in events.GroupBy(item => item.ProviderId))
        {
            Assert.Equal(32, provider.Count(item => item.EventType == "ProviderReadStarted"));
            Assert.Equal(32, provider.Count(item => item.EventType == "ProviderReadSucceeded"));
        }

        Assert.DoesNotContain("secret-marker", Encoding.UTF8.GetString(output.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task PrivacyProviderOrdinalsBelongOnlyToTheirRun()
    {
        using var firstOutput = new MemoryStream();
        using var secondOutput = new MemoryStream();
        var first = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(firstOutput);
        var second = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(secondOutput);
        var firstContext = new GenesisYellowPageRefreshDiagnosticContext(
            first, Guid.NewGuid(), GenesisYellowPageRefreshTrigger.Initial);
        var secondContext = new GenesisYellowPageRefreshDiagnosticContext(
            second, Guid.NewGuid(), GenesisYellowPageRefreshTrigger.Initial);
        Assert.True(first.TryRecord("ProviderReadStarted", firstContext.ForProvider("private-first")));
        Assert.True(first.TryRecord("ProviderReadStarted", firstContext.ForProvider("private-second")));
        Assert.True(second.TryRecord("ProviderReadStarted", secondContext.ForProvider("private-second")));
        await first.DisposeAsync();
        await second.DisposeAsync();

        Assert.NotEqual(first.RunId, second.RunId);
        Assert.Equal(ExpectedProviderIds,
            ReadEvents(firstOutput).Where(item => item.ProviderId is not null).Select(item => item.ProviderId));
        Assert.Equal("provider-1", Assert.Single(ReadEvents(secondOutput), item => item.ProviderId is not null).ProviderId);
    }

    [Fact]
    public async Task InitialManualAndAutomaticRequestsHaveDistinctCorrelation()
    {
        var output = new MemoryStream();
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(output);
        var catalog = new CountingCatalog();
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var session = GenesisChannelCatalogSession.Create(
            catalog,
            viewModel,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance,
            trace);

        session.StartInitialRefresh();
        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await session.RefreshAsync());
        session.StartAutoRefresh(TimeSpan.FromMilliseconds(20));
        await catalog.WaitForReadsAsync(3);

        await session.DisposeAsync();
        var events = ReadEvents(output);
        var requests = events
            .Where(static item => item.EventType == "RefreshRequested")
            .ToArray();

        Assert.Contains(requests, static item => item.Trigger == nameof(GenesisYellowPageRefreshTrigger.Initial));
        Assert.Contains(requests, static item => item.Trigger == nameof(GenesisYellowPageRefreshTrigger.Manual));
        Assert.Contains(requests, static item => item.Trigger == nameof(GenesisYellowPageRefreshTrigger.Automatic));
        Assert.Equal(requests.Length, requests.Select(static item => item.RefreshRequestId).Distinct().Count());
        Assert.All(
            events.Where(static item => item.EventType == "RefreshStarted"),
            static item => Assert.NotNull(item.RefreshAttemptId));
        Assert.Contains(events, static item => item.EventType == "RefreshCompleted");
        Assert.Equal(0, Assert.Single(events, static item => item.EventType == "DiagnosticsClosed").DroppedEvents);
    }

    [Fact]
    public async Task CancelledBeforeExecutionIsSuppressedWithoutInventingAttempt()
    {
        var output = new MemoryStream();
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(output);
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var session = GenesisChannelCatalogSession.Create(
            EmptyGenesisChannelCatalog.Instance,
            viewModel,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance,
            trace);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Cancelled,
            await session.RefreshAsync(cancellation.Token));
        await session.DisposeAsync();

        var events = ReadEvents(output);
        var request = Assert.Single(events, static item => item.EventType == "RefreshRequested");
        Assert.Contains(
            events,
            item => item.EventType == "RefreshSuppressed" &&
                item.RefreshRequestId == request.RefreshRequestId);
        Assert.DoesNotContain(
            events,
            item => item.EventType == "RefreshStarted" &&
                item.RefreshRequestId == request.RefreshRequestId);
    }

    [Fact]
    public async Task QueuedRequestGetsItsOwnAttemptOnlyAfterPredecessorCompletes()
    {
        var output = new MemoryStream();
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(output);
        var catalog = new BlockingCatalog();
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var session = GenesisChannelCatalogSession.Create(
            catalog,
            viewModel,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance,
            trace);

        var first = session.RefreshAsync().AsTask();
        await catalog.FirstReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        var second = session.RefreshAsync().AsTask();
        catalog.ReleaseFirstRead.TrySetResult();
        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, await first);
        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, await second);
        await session.DisposeAsync();

        var events = ReadEvents(output);
        var queued = events.Where(static item => item.EventType == "RefreshQueued").ToArray();
        var started = events.Where(static item => item.EventType == "RefreshStarted").ToArray();
        Assert.Equal(2, queued.Length);
        Assert.Equal(2, started.Length);
        Assert.All(started, static item => Assert.NotNull(item.RefreshAttemptId));
        Assert.Equal(queued.Select(static item => item.RefreshRequestId).Order(),
            started.Select(static item => item.RefreshRequestId).Order());
        var firstCompleted = Assert.Single(events, item =>
            item.EventType == "RefreshCompleted" && item.RefreshRequestId == queued[0].RefreshRequestId);
        var secondStarted = Assert.Single(started, item => item.RefreshRequestId == queued[1].RefreshRequestId);
        Assert.True(queued[1].Sequence < firstCompleted.Sequence);
        Assert.True(firstCompleted.Sequence < secondStarted.Sequence);
    }

    [Fact]
    public async Task UiDuplicateSuppressionIsRecordedWithoutAReadAttempt()
    {
        var output = new MemoryStream();
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(output);
        var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        var publisher = new GenesisGuiNextChannelPresencePublisher(
            tabs,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance);
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var session = GenesisChannelCatalogSession.Create(
            EmptyGenesisChannelCatalog.Instance,
            viewModel,
            publisher,
            trace);
        var interaction = new GenesisMainWindowInteraction(viewModel, session);
        var requestId = Guid.NewGuid();

        interaction.RecordRefreshSuppressed(requestId, "RefreshAlreadyPending");
        await session.DisposeAsync();

        var events = ReadEvents(output);
        Assert.Contains(events, item => item.EventType == "RefreshRequested" &&
            item.RefreshRequestId == requestId && item.Stage == "UiCommand");
        Assert.Contains(events, item => item.EventType == "RefreshSuppressed" &&
            item.RefreshRequestId == requestId && item.SuppressionReason == "RefreshAlreadyPending");
        Assert.DoesNotContain(events, item => item.EventType == "RefreshStarted" &&
            item.RefreshRequestId == requestId);
    }

    [Fact]
    public async Task LogsRefreshDoesNotInventYellowPageRequestsOrAttempts()
    {
        var output = new MemoryStream();
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(output);
        var catalog = new CountingCatalog();
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        await using var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        var publisher = new GenesisGuiNextChannelPresencePublisher(
            tabs, EmptyGenesisChannelPresenceNotificationPublisher.Instance);
        var session = GenesisChannelCatalogSession.Create(catalog, viewModel, publisher, trace);
        tabs.SelectedTab = GenesisGuiNextTab.Logs;
        var interaction = new GenesisMainWindowInteraction(viewModel, session);

        Assert.False(interaction.IsYellowPageCatalogView);
        interaction.RecordRefreshSuppressed(Guid.NewGuid(), "RefreshAlreadyPending");
        await interaction.RefreshCurrentViewAsync(Guid.NewGuid());
        Assert.Equal(0, catalog.ReadCount);
        await session.DisposeAsync();

        Assert.All(ReadEvents(output), item => Assert.Equal("DiagnosticsClosed", item.EventType));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AggregateCorrelationPreservesDisplayOnlyNoticesAndGuiProjection(bool enabled)
    {
        var output = new MemoryStream();
        var trace = enabled ? GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(output) : null;
        var definition = Definition("notice-provider", "http://127.0.0.1:7144/index.txt");
        var configuration = new GenesisYellowPageConfiguration([definition]);
        var notice = new GenesisYellowPageNotice("Service status", "synthetic notice", "RAW");
        var factory = new DelegateCatalogFactory(_ =>
            new FixedCatalog(new GenesisChannelCatalogSnapshot([], [notice])));
        var aggregate = new GenesisAggregatingYellowPageChannelCatalog(configuration, factory);
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        await using var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        var publisher = new GenesisGuiNextChannelPresencePublisher(
            tabs, EmptyGenesisChannelPresenceNotificationPublisher.Instance);
        var session = GenesisChannelCatalogSession.Create(aggregate, viewModel, publisher, trace);

        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, await session.RefreshAsync());
        Assert.Empty(viewModel.Channels);
        Assert.Equal(1, tabs.NoticeCount);
        var row = Assert.Single(GenesisGuiNextNativeTabProjection.Create(tabs));
        Assert.False(row.IsInteractive);
        Assert.Equal("Service status", row.DisplayChannel.Name);
        await session.DisposeAsync();

        if (enabled)
        {
            var events = ReadEvents(output);
            var started = Assert.Single(events, item => item.EventType == "RefreshStarted");
            var projected = Assert.Single(events, item => item.EventType == "CatalogProjectionCompleted");
            Assert.NotNull(started.RefreshAttemptId);
            Assert.NotNull(started.ConfigurationRevision);
            Assert.Equal(started.RefreshAttemptId, projected.RefreshAttemptId);
            Assert.Equal(started.ConfigurationRevision, projected.ConfigurationRevision);
        }
        else
        {
            Assert.Empty(output.ToArray());
        }
    }

    [Fact]
    public async Task AggregatedHttpParseProviderAndProjectionShareRequestAttempt()
    {
        const string providerId = "fixture-local";
        var text = BuildIndexRow("0123456789ABCDEF0123456789ABCDEF");
        var definition = Definition(providerId, "http://127.0.0.1:7144/index.txt?private=must-not-log");
        var configuration = new GenesisYellowPageConfiguration([definition]);
        var textSource = new FixedTextSource(text);
        var factory = new DelegateCatalogFactory(
            item => GenesisYellowPageCatalogFactory.Create(item, textSource));
        var aggregate = new GenesisAggregatingYellowPageChannelCatalog(configuration, factory);
        var output = new MemoryStream();
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(output);
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var session = GenesisChannelCatalogSession.Create(
            aggregate,
            viewModel,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance,
            trace);

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await session.RefreshAsync());
        Assert.Single(viewModel.Channels);
        await session.DisposeAsync();

        var events = ReadEvents(output);
        var request = Assert.Single(events, static item => item.EventType == "RefreshRequested");
        var attempt = Assert.Single(events, static item => item.EventType == "RefreshStarted");
        Assert.Equal(request.RefreshRequestId, attempt.RefreshRequestId);
        Assert.NotNull(attempt.RefreshAttemptId);

        foreach (var eventType in new[]
                 {
                     "ProviderSnapshotCaptured",
                     "ProviderReadStarted",
                     "ProviderReadSucceeded",
                     "HttpReadStarted",
                     "HttpReadSucceeded",
                     "CatalogParseCompleted",
                     "CatalogProjectionCompleted",
                     "RefreshCompleted",
                 })
        {
            var item = Assert.Single(events, entry => entry.EventType == eventType);
            Assert.Equal(request.RefreshRequestId, item.RefreshRequestId);
            Assert.Equal(attempt.RefreshAttemptId, item.RefreshAttemptId);
            if (eventType is "ProviderReadStarted" or "ProviderReadSucceeded" or
                "HttpReadStarted" or "HttpReadSucceeded" or "CatalogParseCompleted")
            {
                Assert.Equal("provider-1", item.ProviderId);
            }
        }

        var providerSuccess = Assert.Single(events, static item => item.EventType == "ProviderReadSucceeded");
        Assert.Equal(1, providerSuccess.ParsedRowCount);
        var parse = Assert.Single(events, static item => item.EventType == "CatalogParseCompleted");
        Assert.Equal(1, parse.InputRowCount);
        Assert.Equal(1, parse.ParsedRowCount);
        Assert.Equal(0, parse.SkippedRowCount);
        var projected = Assert.Single(events, static item => item.EventType == "CatalogProjectionCompleted");
        Assert.Equal(1, projected.ProjectedRowCount);
        var json = Encoding.UTF8.GetString(output.ToArray());
        Assert.DoesNotContain("private=must-not-log", json, StringComparison.Ordinal);
        Assert.DoesNotContain(text, json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PartialProviderFailureIsRecordedWithoutChangingAggregateSuccess()
    {
        var first = Definition("works", "http://127.0.0.1:7144/index.txt");
        var second = Definition("fails", "http://127.0.0.1:7145/index.txt");
        var configuration = new GenesisYellowPageConfiguration([first, second]);
        var factory = new DelegateCatalogFactory(definition =>
            string.Equals(definition.Id, "works", StringComparison.Ordinal)
                ? new FixedCatalog(new([new GenesisChannelItem("Visible", "pcp://127.0.0.1:7144/channel")]))
                : new ThrowingCatalog(new InvalidOperationException("secret message must not be logged")));
        var aggregate = new GenesisAggregatingYellowPageChannelCatalog(configuration, factory);
        var output = new MemoryStream();
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(output);
        var requestId = Guid.NewGuid();
        using (GenesisYellowPageRefreshDiagnosticAmbient.Push(
                   new GenesisYellowPageRefreshDiagnosticContext(
                       trace,
                       requestId,
                       GenesisYellowPageRefreshTrigger.Manual)))
        {
            var result = await aggregate.ReadDetailedAsync();
            Assert.Single(result.Catalog.Channels);
        }

        await trace.DisposeAsync();
        var events = ReadEvents(output);
        var failed = Assert.Single(events, static item => item.EventType == "ProviderReadFailed");
        Assert.Equal("provider-2", failed.ProviderId);
        Assert.Equal(nameof(InvalidOperationException), failed.ExceptionType);
        Assert.Equal(requestId, failed.RefreshRequestId);
        Assert.Contains(
            events,
            static item => item.EventType == "ProviderReadSucceeded" &&
                item.ProviderId == "provider-1");
        var json = Encoding.UTF8.GetString(output.ToArray());
        Assert.DoesNotContain("secret message must not be logged", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AllProviderFailureIsRecordedAndPreservesFaultedRefreshResult()
    {
        var configuration = new GenesisYellowPageConfiguration([
            Definition("first", "http://127.0.0.1:7144/index.txt"),
            Definition("second", "http://127.0.0.1:7145/index.txt"),
        ]);
        var factory = new DelegateCatalogFactory(_ =>
            new ThrowingCatalog(new HttpRequestException("private detail")));
        var aggregate = new GenesisAggregatingYellowPageChannelCatalog(configuration, factory);
        var output = new MemoryStream();
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(output);
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var session = GenesisChannelCatalogSession.Create(
            aggregate,
            viewModel,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance,
            trace);

        Assert.Equal(GenesisChannelCatalogRefreshResult.Faulted, await session.RefreshAsync());
        await session.DisposeAsync();

        var events = ReadEvents(output);
        Assert.Equal(2, events.Count(static item => item.EventType == "ProviderReadStarted"));
        Assert.Equal(2, events.Count(static item => item.EventType == "ProviderReadFailed"));
        Assert.Contains(events, static item => item.EventType == "RefreshFailed" &&
            item.FailureStage == "AggregateRead");
        Assert.Contains(events, static item => item.EventType == "RefreshFailed" &&
            item.FailureStage == "CatalogOrProjection");
        Assert.DoesNotContain(events, static item => item.EventType == "CatalogProjectionCompleted");
        Assert.DoesNotContain("private detail", Encoding.UTF8.GetString(output.ToArray()), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ParserAndProjectionZeroRowsAreReportedSeparately()
    {
        const string providerId = "empty-parser";
        var configuration = new GenesisYellowPageConfiguration([
            Definition(providerId, "http://127.0.0.1:7144/index.txt"),
        ]);
        var factory = new DelegateCatalogFactory(item =>
            GenesisYellowPageCatalogFactory.Create(item, new FixedTextSource("not-a-valid-index-row\n")));
        var aggregate = new GenesisAggregatingYellowPageChannelCatalog(configuration, factory);
        var output = new MemoryStream();
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(output);
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var session = GenesisChannelCatalogSession.Create(
            aggregate,
            viewModel,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance,
            trace);

        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, await session.RefreshAsync());
        Assert.Empty(viewModel.Channels);
        await session.DisposeAsync();

        var events = ReadEvents(output);
        var parse = Assert.Single(events, static item => item.EventType == "CatalogParseCompleted");
        Assert.Equal(1, parse.InputRowCount);
        Assert.Equal(0, parse.ParsedRowCount);
        Assert.Equal(1, parse.SkippedRowCount);
        var projection = Assert.Single(events, static item => item.EventType == "CatalogProjectionCompleted");
        Assert.Equal(0, projection.ProjectedRowCount);
        Assert.Contains(events, static item => item.EventType == "RefreshCompleted");
    }

    [Fact]
    public async Task NonAggregatedCatalogKeepsUnavailableConfigurationRevisionNull()
    {
        var output = new MemoryStream();
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(output);
        var session = GenesisChannelCatalogSession.Create(
            new FixedCatalog(GenesisChannelCatalogSnapshot.Empty),
            new GenesisMainViewModel(new FakePlaybackSessionController()),
            EmptyGenesisChannelPresenceNotificationPublisher.Instance,
            trace);

        Assert.Equal(
            GenesisChannelCatalogRefreshResult.Completed,
            await session.RefreshAsync(Guid.NewGuid()));
        await session.DisposeAsync();

        var events = ReadEvents(output);
        var started = Assert.Single(events, static item => item.EventType == "RefreshStarted");
        var projection = Assert.Single(events, static item => item.EventType == "CatalogProjectionCompleted");
        Assert.Equal(started.RefreshAttemptId, projection.RefreshAttemptId);
        Assert.Null(started.ConfigurationRevision);
        Assert.Null(projection.ConfigurationRevision);
    }

    [Fact]
    public async Task DiagnosticSinkFailureDoesNotChangeRefreshResult()
    {
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(new FailingStream());
        var catalog = new CountingCatalog();
        var viewModel = new GenesisMainViewModel(new FakePlaybackSessionController());
        var session = GenesisChannelCatalogSession.Create(
            catalog,
            viewModel,
            EmptyGenesisChannelPresenceNotificationPublisher.Instance,
            trace);

        Assert.Equal(GenesisChannelCatalogRefreshResult.Completed, await session.RefreshAsync());
        await session.DisposeAsync();

        Assert.Equal(1, catalog.ReadCount);
        Assert.True(trace.WriterFailed);
    }

    [Fact]
    public async Task ConfigurationRevisionRetryUsesAnotherAttemptForSameRequest()
    {
        var configuration = new GenesisYellowPageConfiguration([
            Definition("first", "http://127.0.0.1:7144/index.txt"),
        ]);
        var mutation = 0;
        var factory = new DelegateCatalogFactory(definition =>
            new MutatingCatalog(
                configuration,
                () => Interlocked.Exchange(ref mutation, 1) == 0));
        var aggregate = new GenesisAggregatingYellowPageChannelCatalog(configuration, factory);
        var output = new MemoryStream();
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(output);
        var requestId = Guid.NewGuid();
        using (GenesisYellowPageRefreshDiagnosticAmbient.Push(
                   new GenesisYellowPageRefreshDiagnosticContext(
                       trace,
                       requestId,
                       GenesisYellowPageRefreshTrigger.Manual)))
        {
            _ = await aggregate.ReadDetailedAsync();
        }

        await trace.DisposeAsync();
        var events = ReadEvents(output);
        var attempts = events
            .Where(static item => item.EventType == "RefreshStarted")
            .ToArray();
        Assert.Equal(2, attempts.Length);
        Assert.Equal(requestId, attempts[0].RefreshRequestId);
        Assert.Equal(requestId, attempts[1].RefreshRequestId);
        Assert.NotEqual(attempts[0].RefreshAttemptId, attempts[1].RefreshAttemptId);
        Assert.Equal(0, attempts[0].ConfigurationRevision);
        Assert.Equal(1, attempts[1].ConfigurationRevision);
        Assert.Contains(events, static item => item.EventType == "CatalogAttemptDiscarded");
        var providerStarts = events.Where(item => item.EventType == "ProviderReadStarted").ToArray();
        Assert.Equal(3, providerStarts.Length);
        Assert.Equal("provider-1", providerStarts[0].ProviderId);
        Assert.Equal("provider-1", providerStarts[1].ProviderId);
        Assert.Equal("provider-2", providerStarts[2].ProviderId);
    }

    [Fact]
    public async Task SystemHttpDiagnosticsCaptureSuccessAnd404StatusOnLoopback()
    {
        var body = BuildIndexRow("ABCDEF0123456789ABCDEF0123456789");
        var success = await ReadLocalHttpAsync("HTTP/1.0 200 OK", body);
        var successEvent = Assert.Single(success.Events, static item => item.EventType == "HttpReadSucceeded");
        Assert.Equal(200, successEvent.HttpStatusCode);
        Assert.Equal(Encoding.UTF8.GetByteCount(body), successEvent.ResponseByteCount);

        var notFound = await ReadLocalHttpFailureAsync("HTTP/1.0 404 Not Found");
        var failure = Assert.Single(notFound, static item => item.EventType == "HttpReadFailed");
        Assert.Equal(404, failure.HttpStatusCode);
        Assert.Equal("DecodeResponse", failure.FailureStage);
    }

    [Fact]
    public async Task TextOnlySourceDoesNotClaimAnUnobservedHttpResponse()
    {
        var output = new MemoryStream();
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(output);
        var requestId = Guid.NewGuid();
        using (GenesisYellowPageRefreshDiagnosticAmbient.Push(
                   new GenesisYellowPageRefreshDiagnosticContext(
                       trace,
                       requestId,
                       GenesisYellowPageRefreshTrigger.Manual)))
        {
            var catalog = new HttpPeerCastIndexChannelCatalog(
                new Uri("http://127.0.0.1:7144/index.txt"),
                null,
                new LegacyFixedTextSource("provided text, no HTTP response metadata"));
            _ = await catalog.ReadAsync();
        }

        await trace.DisposeAsync();
        var events = ReadEvents(output);
        Assert.Contains(events, item => item.EventType == "TextSourceReadCompleted" &&
            item.RefreshRequestId == requestId &&
            item.Outcome == "ReturnedWithoutHttpEvidence");
        Assert.DoesNotContain(events, static item => item.EventType == "HttpReadSucceeded");
    }

    [Fact]
    public async Task SystemHttpDiagnosticsClassifyConnectionFailureAndCancellation()
    {
        using var closedPort = new TcpListener(IPAddress.Loopback, 0);
        closedPort.Start();
        var port = ((IPEndPoint)closedPort.LocalEndpoint).Port;
        closedPort.Stop();
        var connectionFailure = await ReadHttpFailureAsync(new Uri($"http://127.0.0.1:{port}/index.txt"));
        var connect = Assert.Single(connectionFailure, static item => item.EventType == "HttpReadFailed");
        Assert.Equal("Connect", connect.FailureStage);

        using var serverCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var clientCancellation = new CancellationTokenSource();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync(serverCancellation.Token);
            await using var stream = client.GetStream();
            _ = await ReadHeadersAsync(stream, serverCancellation.Token);
            accepted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, serverCancellation.Token);
        }, serverCancellation.Token);
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var output = new MemoryStream();
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(output);
        var context = new GenesisYellowPageRefreshDiagnosticContext(
            trace,
            Guid.NewGuid(),
            GenesisYellowPageRefreshTrigger.Manual);
        using var scope = GenesisYellowPageRefreshDiagnosticAmbient.Push(context);
        var catalog = new HttpPeerCastIndexChannelCatalog(
            new Uri($"http://127.0.0.1:{endpoint.Port}/index.txt"),
            null,
            SystemPeerCastIndexTextSource.Instance);
        try
        {
            var read = catalog.ReadAsync(clientCancellation.Token).AsTask();
            await accepted.Task.WaitAsync(serverCancellation.Token);
            clientCancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await read);
        }
        finally
        {
            listener.Stop();
            serverCancellation.Cancel();
            try { await server; } catch (OperationCanceledException) { }
        }

        await trace.DisposeAsync();
        var cancelled = Assert.Single(ReadEvents(output), static item => item.EventType == "HttpReadFailed");
        Assert.Equal("Cancelled", cancelled.Outcome);
        Assert.Equal("ReadResponse", cancelled.FailureStage);
    }

    [Fact]
    public async Task TraceBoundsRecordsAndAccountsForDrops()
    {
        var output = new MemoryStream();
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(
            output,
            maximumRecords: 4,
            maximumFileBytes: 8192,
            queueCapacity: 64);
        for (var index = 0; index < 12; index++)
        {
            _ = trace.TryRecord(
                "BoundedEvent",
                stage: "Test",
                outcome: "Observed",
                responseByteCount: index);
        }

        await trace.DisposeAsync();
        var events = ReadEvents(output);
        Assert.True(events.Count <= 4);
        var closing = Assert.Single(events, static item => item.EventType == "DiagnosticsClosed");
        Assert.True(closing.DroppedEvents >= 9);
        Assert.Equal("FlushedWithDrops", closing.Outcome);
        Assert.All(events, static item => Assert.NotEqual(Guid.Empty, item.RunId));
    }

    [Fact]
    public void DiagnosticsRemainOffWithoutExplicitDirectoryAndRejectLocalAppData()
    {
        Assert.Null(GenesisYellowPageRefreshDiagnosticTrace.FromEnvironment(static () => null));
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            var rejected = Path.Combine(localAppData, $"yp-diag-test-{Guid.NewGuid():N}");
            Assert.Null(GenesisYellowPageRefreshDiagnosticTrace.TryCreate(rejected));
            Assert.False(Directory.Exists(rejected));
        }
    }

    private static async Task<(List<GenesisYellowPageRefreshDiagnosticEvent> Events, Guid RunId)>
        ReadLocalHttpAsync(string statusLine, string body)
    {
        using var serverCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var server = ServeHttpOnceAsync(
            listener,
            statusLine,
            body,
            serverCancellation.Token);
        var output = new MemoryStream();
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(output);
        var requestId = Guid.NewGuid();
        var context = new GenesisYellowPageRefreshDiagnosticContext(
            trace,
            requestId,
            GenesisYellowPageRefreshTrigger.Manual,
            Guid.NewGuid(),
            "loopback-yp",
            7);
        using var scope = GenesisYellowPageRefreshDiagnosticAmbient.Push(context);
        var catalog = new HttpPeerCastIndexChannelCatalog(
            new Uri($"http://127.0.0.1:{endpoint.Port}/index.txt"),
            null,
            SystemPeerCastIndexTextSource.Instance);
        try
        {
            var snapshot = await catalog.ReadAsync(serverCancellation.Token);
            Assert.Single(snapshot.Channels);
            await server;
        }
        finally
        {
            listener.Stop();
        }

        var runId = trace.RunId;
        await trace.DisposeAsync();
        return (ReadEvents(output), runId);
    }

    private static async Task<List<GenesisYellowPageRefreshDiagnosticEvent>> ReadLocalHttpFailureAsync(
        string statusLine)
    {
        using var serverCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        var server = ServeHttpOnceAsync(listener, statusLine, string.Empty, serverCancellation.Token);
        var events = await ReadHttpFailureAsync(
            new Uri($"http://127.0.0.1:{endpoint.Port}/index.txt"),
            server);
        listener.Stop();
        return events;
    }

    private static async Task<List<GenesisYellowPageRefreshDiagnosticEvent>> ReadHttpFailureAsync(
        Uri uri,
        Task? server = null)
    {
        var output = new MemoryStream();
        var trace = GenesisYellowPageRefreshDiagnosticTrace.CreateForTesting(output);
        var context = new GenesisYellowPageRefreshDiagnosticContext(
            trace,
            Guid.NewGuid(),
            GenesisYellowPageRefreshTrigger.Manual,
            Guid.NewGuid(),
            "loopback-yp",
            0);
        using var scope = GenesisYellowPageRefreshDiagnosticAmbient.Push(context);
        var catalog = new HttpPeerCastIndexChannelCatalog(uri, null, SystemPeerCastIndexTextSource.Instance);
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(async () => await catalog.ReadAsync());
            if (server is not null)
            {
                await server;
            }
        }
        finally
        {
            await trace.DisposeAsync();
        }

        return ReadEvents(output);
    }

    private static async Task ServeHttpOnceAsync(
        TcpListener listener,
        string statusLine,
        string body,
        CancellationToken cancellationToken)
    {
        using var client = await listener.AcceptTcpClientAsync(cancellationToken);
        await using var stream = client.GetStream();
        _ = await ReadHeadersAsync(stream, cancellationToken);
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var response = Encoding.ASCII.GetBytes(
            $"{statusLine}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {bodyBytes.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(response.AsMemory(), cancellationToken);
        if (bodyBytes.Length > 0)
        {
            await stream.WriteAsync(bodyBytes.AsMemory(), cancellationToken);
        }

        await stream.FlushAsync(cancellationToken);
    }

    private static async Task<byte[]> ReadHeadersAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var bytes = new byte[512];
        while (buffer.Length < 8192)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(), cancellationToken);
            if (read == 0)
            {
                break;
            }

            buffer.Write(bytes, 0, read);
            if (buffer.GetBuffer().AsSpan(0, checked((int)buffer.Length)).IndexOf("\r\n\r\n"u8) >= 0)
            {
                break;
            }
        }

        return buffer.ToArray();
    }

    private static List<GenesisYellowPageRefreshDiagnosticEvent> ReadEvents(MemoryStream output)
    {
        var json = Encoding.UTF8.GetString(output.ToArray());
        var result = new List<GenesisYellowPageRefreshDiagnosticEvent>();
        foreach (var line in json.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var item = JsonSerializer.Deserialize<GenesisYellowPageRefreshDiagnosticEvent>(
                line,
                JsonOptions);
            if (item is not null)
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static GenesisYellowPageDefinition Definition(string id, string indexUrl) =>
        new(id, id, new Uri(indexUrl));

    private static string BuildIndexRow(string channelId)
    {
        var fields = Enumerable.Repeat(string.Empty, 18).ToArray();
        fields[0] = "Diagnostic Fixture";
        fields[1] = channelId;
        fields[2] = "127.0.0.1:7144";
        fields[9] = "FLV";
        return string.Join("<>", fields) + "\n";
    }

    private sealed class FixedTextSource(string text) : IPeerCastIndexDiagnosticTextSource
    {
        public ValueTask<string> ReadAsync(Uri indexUri, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(text);
        }

        public ValueTask<PeerCastIndexTextReadResult> ReadWithDiagnosticsAsync(
            Uri indexUri,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new PeerCastIndexTextReadResult(
                text,
                HttpStatusCode: 200,
                ResponseBodyByteCount: Encoding.UTF8.GetByteCount(text)));
        }
    }

    private sealed class LegacyFixedTextSource(string text) : IPeerCastIndexTextSource
    {
        public ValueTask<string> ReadAsync(Uri indexUri, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(text);
        }
    }

    private sealed class DelegateCatalogFactory(
        Func<GenesisYellowPageDefinition, IGenesisChannelCatalog> create) :
        IGenesisYellowPageChannelCatalogFactory
    {
        public IGenesisChannelCatalog Create(GenesisYellowPageDefinition definition) =>
            create(definition);
    }

    private sealed class FixedCatalog(GenesisChannelCatalogSnapshot snapshot) : IGenesisChannelCatalog
    {
        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(snapshot);
        }
    }

    private sealed class ThrowingCatalog(Exception exception) : IGenesisChannelCatalog
    {
        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromException<GenesisChannelCatalogSnapshot>(exception);
    }

    private sealed class MutatingCatalog(
        GenesisYellowPageConfiguration configuration,
        Func<bool> shouldMutate) : IGenesisChannelCatalog
    {
        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (shouldMutate())
            {
                _ = configuration.Add(Definition("second", "http://127.0.0.1:7145/index.txt"));
            }

            return ValueTask.FromResult(GenesisChannelCatalogSnapshot.Empty);
        }
    }

    private sealed class CountingCatalog : IGenesisChannelCatalog
    {
        private int _readCount;
        private TaskCompletionSource _changed = NewSignal();

        public int ReadCount => Volatile.Read(ref _readCount);

        public ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Interlocked.Increment(ref _readCount);
            var changed = Interlocked.Exchange(ref _changed, NewSignal());
            changed.TrySetResult();
            return ValueTask.FromResult(GenesisChannelCatalogSnapshot.Empty);
        }

        public async Task WaitForReadsAsync(int count)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (Volatile.Read(ref _readCount) < count)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException($"Expected {count} catalog reads.");
                }

                var changed = Volatile.Read(ref _changed).Task;
                await changed.WaitAsync(deadline - DateTime.UtcNow);
            }
        }

        private static TaskCompletionSource NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class BlockingCatalog : IGenesisChannelCatalog
    {
        private int _readCount;

        public TaskCompletionSource FirstReadStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseFirstRead { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<GenesisChannelCatalogSnapshot> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _readCount) == 1)
            {
                FirstReadStarted.TrySetResult();
                await ReleaseFirstRead.Task.WaitAsync(cancellationToken);
            }

            return GenesisChannelCatalogSnapshot.Empty;
        }
    }

    private sealed class GatedDiagnosticStream(bool failWrites, bool failFlush = false) : MemoryStream
    {
        private int _writeCalls;
        public int WriteCalls => Volatile.Read(ref _writeCalls);
        public TaskCompletionSource WriteStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _writeCalls) == 1)
            {
                WriteStarted.TrySetResult();
                await ReleaseWrite.Task.WaitAsync(cancellationToken);
            }

            if (failWrites)
            {
                throw new IOException("test diagnostic sink failure");
            }

            await base.WriteAsync(buffer, cancellationToken);
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => failFlush
            ? Task.FromException(new IOException("test diagnostic flush failure"))
            : base.FlushAsync(cancellationToken);
    }

    private sealed class FailingStream : MemoryStream
    {
        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromException(new IOException("test diagnostic sink failure"));

        public override Task FlushAsync(CancellationToken cancellationToken) =>
            Task.FromException(new IOException("test diagnostic sink failure"));
    }
}
