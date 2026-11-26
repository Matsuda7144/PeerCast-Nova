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
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17_763))
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
        var project = RepositorySourceTestHelper.LoadProjectDocument(
            "src",
            "Nova.App",
            "Nova.App.csproj");
        var embeddedResources = project.Descendants("EmbeddedResource")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .ToArray();

        Assert.Contains(embeddedResources, path => path!.EndsWith("NOTICE.md", StringComparison.Ordinal));
        Assert.Contains(embeddedResources, path => path!.EndsWith("PROVENANCE.md", StringComparison.Ordinal));
        Assert.Contains(embeddedResources, path => path!.EndsWith("RUNTIME-REPLACEMENT.md", StringComparison.Ordinal));

        var includeValues = project.Descendants()
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => value is not null)
            .Select(value => value!)
            .ToArray();

        Assert.DoesNotContain(includeValues, value => value.Contains(@"FFmpeg\**\*", StringComparison.Ordinal));
        Assert.DoesNotContain(includeValues, value => value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(includeValues, value => value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(includeValues, value => value.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        var appProjectReferences = project.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();
        Assert.DoesNotContain(appProjectReferences, path => path.Contains("Nova.Playback", StringComparison.Ordinal));
        Assert.DoesNotContain(appProjectReferences, path => path.Contains("Nova.Cli", StringComparison.Ordinal));

        var referencedAssemblies = typeof(NovaAboutLegalProjection).Assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .ToArray();
        Assert.DoesNotContain("PeerCast.Nova.Playback", referencedAssemblies, StringComparer.Ordinal);
        Assert.DoesNotContain("PeerCast.Nova.Cli", referencedAssemblies, StringComparer.Ordinal);

        var testProject = RepositorySourceTestHelper.LoadProjectDocument(
            "tests",
            "Nova.App.Tests",
            "Nova.App.Tests.csproj");
        var testProjectReferences = testProject.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();
        Assert.DoesNotContain(
            testProjectReferences,
            path => path.EndsWith("Nova.Playback.csproj", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            testProjectReferences,
            path => path.EndsWith("Nova.Playback.Contracts.csproj", StringComparison.OrdinalIgnoreCase));
    }

    private static async ValueTask<NovaAboutLegalProjection> OpenForCurrentPlatformAsync(
        INovaAboutLegalDispatcher dispatcher,
        INovaAboutLegalProjectionTarget target)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17_763))
        {
            return await NovaAboutLegalProjection.OpenAsync(dispatcher, target);
        }

        return await NovaAboutLegalProjection.OpenAsync(
            dispatcher,
            target,
            static () => true);
    }

    private static string ReadLegalFile(string fileName) =>
        File.ReadAllText(RepositorySourceTestHelper.FindRepositoryFile("THIRD-PARTY-LICENSES", "FFmpeg", fileName));

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
