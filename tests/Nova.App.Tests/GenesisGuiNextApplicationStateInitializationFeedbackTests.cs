namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextApplicationStateInitializationFeedbackTests
{
    [Fact]
    public void ResolverExplainsStateBackedFeaturesUnavailableForSession()
    {
        var presentation = GenesisGuiNextApplicationStateInitializationFeedback.Resolve();

        Assert.Equal(
            "Saved GUI settings could not be initialized. Favorites, notifications, and editable Yellow Page settings are unavailable for this session.",
            presentation.Message);
        Assert.Equal("PeerCast Nova", presentation.Caption);
    }

    [Fact]
    public void FeedbackPresentsExactlyOneWarning()
    {
        var presented =
            new List<GenesisGuiNextApplicationStateInitializationFeedbackPresentation>();

        GenesisGuiNextApplicationStateInitializationFeedback.Report(presented.Add);

        Assert.Single(presented);
    }

    [Fact]
    public void FeedbackFailureCannotFaultMainGuiLifetime()
    {
        var exception = Record.Exception(() =>
            GenesisGuiNextApplicationStateInitializationFeedback.Report(
                static _ => throw new InvalidOperationException("Presentation failed.")));

        Assert.Null(exception);
    }

    [Fact]
    public void StateFactoryFailureReportsAndRetainsNullFallback()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "SystemGenesisInteractiveMainWindowSurfaceTarget.cs"));

        Assert.Contains(
            "catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "SystemGenesisGuiNextApplicationStateInitializationFeedback.Report(windowToken);\n            guiNextState = null;",
            source,
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            source.Split(
                "SystemGenesisGuiNextApplicationStateInitializationFeedback.Report(windowToken);",
                StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void SystemFeedbackUsesExplicitMainWindowOwner()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "GenesisGuiNextApplicationStateInitializationFeedback.cs"));

        Assert.Contains("var owner = (nint)windowToken;", source, StringComparison.Ordinal);
        Assert.Contains("!IsWindow(owner)", source, StringComparison.Ordinal);
        Assert.Contains(
            "_ = MessageBoxW(\n                    owner,",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("GetForegroundWindow", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(path))
            {
                return path;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Repository file was not found: {Path.Combine(relativeParts)}");
    }
}
