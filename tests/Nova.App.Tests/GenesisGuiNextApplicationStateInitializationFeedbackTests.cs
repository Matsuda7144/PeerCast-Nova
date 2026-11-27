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
}
