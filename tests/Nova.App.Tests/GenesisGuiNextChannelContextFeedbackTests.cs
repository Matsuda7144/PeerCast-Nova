namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextChannelContextFeedbackTests
{
    [Fact]
    public void MenuResolverUsesSpecificChannelContextWarning()
    {
        var presentation = GenesisGuiNextChannelContextFeedback.Resolve(
            GenesisGuiNextChannelContextFailure.MenuOpen);

        Assert.Equal("Channel menu could not be opened. Try again.", presentation.Message);
        Assert.Equal("Channels", presentation.Caption);
    }

    [Fact]
    public void ClipboardResolverUsesSpecificChannelContextWarning()
    {
        var presentation = GenesisGuiNextChannelContextFeedback.Resolve(
            GenesisGuiNextChannelContextFailure.ClipboardCopy);

        Assert.Equal(
            "Channel source could not be copied to the clipboard.",
            presentation.Message);
        Assert.Equal("Channels", presentation.Caption);
    }

    [Fact]
    public void FeedbackPresentsExactlyOneWarning()
    {
        var presented = new List<GenesisGuiNextChannelContextFeedbackPresentation>();

        GenesisGuiNextChannelContextFeedback.Report(
            GenesisGuiNextChannelContextFailure.MenuOpen,
            presented.Add);

        Assert.Single(presented);
    }

    [Fact]
    public void FeedbackFailureCannotFaultGuiLifetime()
    {
        var exception = Record.Exception(() =>
            GenesisGuiNextChannelContextFeedback.Report(
                GenesisGuiNextChannelContextFailure.ClipboardCopy,
                static _ => throw new InvalidOperationException("Presentation failed.")));

        Assert.Null(exception);
    }
}
