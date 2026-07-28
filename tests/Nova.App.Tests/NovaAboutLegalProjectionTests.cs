namespace PeerCast.Nova.App.Tests;

public sealed class NovaAboutLegalProjectionTests
{
    [Fact]
    public void MaterialMatchesExactRetainedLegalSources()
    {
        var material = NovaAboutLegalMaterial.Load();

        Assert.Equal(NovaAboutLegalMaterialResult.Loaded, material.Result);
        var model = Assert.IsType<NovaAboutLegalModel>(material.Model);
        Assert.Equal("PeerCast Nova", model.ProductName);
        Assert.Equal("Apache License 2.0", model.ProjectLicense);
        Assert.Equal("FFmpeg", model.ExternalRuntimeName);
        Assert.Equal(
            "Optional external shared runtime; not bundled by PeerCast Nova",
            model.ExternalRuntimeDistribution);
        Assert.Equal(ReadLegalFile("NOTICE.md"), model.Notice);
        Assert.Equal(ReadLegalFile("PROVENANCE.md"), model.Provenance);
        Assert.Equal(ReadLegalFile("RUNTIME-REPLACEMENT.md"), model.RuntimeReplacement);
    }

    [Fact]
    public async Task ProjectionUsesOneDispatcherAndReturnsFiniteLifetime()
    {
        var lifetime = new RecordingLifetime();
        var target = new RecordingTarget(lifetime);
        var dispatcher = new RecordingDispatcher();

        var projection = await OpenForCurrentPlatformAsync(dispatcher, target);

        Assert.Equal(NovaAboutLegalProjectionResult.Projected, projection.Result);
        Assert.Same(lifetime, projection.Lifetime);
        Assert.Equal(1, dispatcher.InvocationCount);
        Assert.Equal(1, target.OpenCount);
        await Assert.IsAssignableFrom<IAsyncDisposable>(projection.Lifetime)
            .DisposeAsync();
        Assert.Equal(1, lifetime.DisposeCount);
    }

    [Fact]
    public async Task TargetRejectionDoesNotCreateProjectionLifetime()
    {
        var projection = await OpenForCurrentPlatformAsync(
            new RecordingDispatcher(),
            new RecordingTarget(lifetime: null));

        Assert.Equal(NovaAboutLegalProjectionResult.TargetRejected, projection.Result);
        Assert.Null(projection.Lifetime);
    }

    [Fact]
    public async Task DispatcherRejectionDoesNotCallTarget()
    {
        var target = new RecordingTarget(new RecordingLifetime());
        var projection = await OpenForCurrentPlatformAsync(
            new RecordingDispatcher(NovaAboutLegalDispatcherResult.Rejected),
            target);

        Assert.Equal(NovaAboutLegalProjectionResult.DispatcherRejected, projection.Result);
        Assert.Null(projection.Lifetime);
        Assert.Equal(0, target.OpenCount);
    }

    [Fact]
    public async Task NonWindowsFailsClosedBeforeDispatcherOrTarget()
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000))
        {
            return;
        }

        var dispatcher = new RecordingDispatcher();
        var target = new RecordingTarget(new RecordingLifetime());

        var projection = await NovaAboutLegalProjection.OpenAsync(dispatcher, target);

        Assert.Equal(
            NovaAboutLegalProjectionResult.PlatformUnsupported,
            projection.Result);
        Assert.Equal(0, dispatcher.InvocationCount);
        Assert.Equal(0, target.OpenCount);
    }

    [Fact]
    public void DefaultGraphEmbedsOnlyBoundedLegalText()
    {
        var project = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "Nova.App.csproj"));

        Assert.Contains("NOTICE.md", project, StringComparison.Ordinal);
        Assert.Contains("PROVENANCE.md", project, StringComparison.Ordinal);
        Assert.Contains("RUNTIME-REPLACEMENT.md", project, StringComparison.Ordinal);
        Assert.DoesNotContain("FFmpeg\\**\\*", project, StringComparison.Ordinal);
        Assert.DoesNotContain(".dll", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".exe", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".zip", project, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Nova.Playback", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Nova.Cli", project, StringComparison.Ordinal);

        var sourceDirectory = Path.GetDirectoryName(FindRepositoryFile(
            "src",
            "Nova.App",
            "Nova.App.csproj"));
        Assert.NotNull(sourceDirectory);
        var source = string.Join(
            Environment.NewLine,
            Directory.GetFiles(sourceDirectory, "*.cs")
                .Select(File.ReadAllText));
        Assert.DoesNotContain("System.Diagnostics.Process", source, StringComparison.Ordinal);
        Assert.DoesNotContain("NativeLibrary.Load", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadLibrary", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HttpClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Download", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Install", source, StringComparison.Ordinal);
    }

    private static async ValueTask<NovaAboutLegalProjection> OpenForCurrentPlatformAsync(
        INovaAboutLegalDispatcher dispatcher,
        INovaAboutLegalProjectionTarget target)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000))
        {
            return await NovaAboutLegalProjection.OpenAsync(dispatcher, target);
        }

        return await NovaAboutLegalProjection.OpenAsync(
            dispatcher,
            target,
            static () => true);
    }

    private static string ReadLegalFile(string fileName) =>
        File.ReadAllText(FindRepositoryFile("THIRD-PARTY-LICENSES", "FFmpeg", fileName));

    private static string FindRepositoryFile(params string[] parts)
    {
        var current = AppContext.BaseDirectory;
        while (current is not null)
        {
            var candidate = Path.Combine([current, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }

    private sealed class RecordingDispatcher(
        NovaAboutLegalDispatcherResult result = NovaAboutLegalDispatcherResult.Completed) :
        INovaAboutLegalDispatcher
    {
        public int InvocationCount { get; private set; }

        public async ValueTask<NovaAboutLegalDispatcherInvocation> InvokeAsync(
            Func<CancellationToken, ValueTask<IAsyncDisposable?>> operation,
            CancellationToken cancellationToken)
        {
            InvocationCount++;
            if (result != NovaAboutLegalDispatcherResult.Completed)
            {
                return new(result, null);
            }

            return new(result, await operation(cancellationToken));
        }
    }

    private sealed class RecordingTarget(IAsyncDisposable? lifetime) :
        INovaAboutLegalProjectionTarget
    {
        public int OpenCount { get; private set; }

        public ValueTask<IAsyncDisposable?> OpenAsync(
            NovaAboutLegalModel model,
            CancellationToken cancellationToken)
        {
            OpenCount++;
            Assert.Equal("PeerCast Nova", model.ProductName);
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(lifetime);
        }
    }

    private sealed class RecordingLifetime : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
