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

    [Fact]
    public void NativeMenuFailuresReportWithoutChangingCancellationContract()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "GenesisChannelContextMenuNative.cs"));

        Assert.True(
            source.Split("return FailMenuOpen(owner);", StringSplitOptions.None).Length - 1 >= 7,
            "All bounded native menu-open rejection paths should report failure.");
        Assert.Contains(
            "return Enum.IsDefined(typeof(GenesisChannelContextMenuCommand), (int)command)\n                ? (GenesisChannelContextMenuCommand)command\n                : GenesisChannelContextMenuCommand.None;",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ClipboardApiFailureReportsAfterCleanup()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "GenesisChannelContextMenuNative.cs"));

        Assert.True(
            source.Split(
                "SystemGenesisGuiNextChannelContextFeedback.ReportClipboardCopyFailure(owner);",
                StringSplitOptions.None).Length - 1 >= 2,
            "Allocation and post-allocation clipboard failures should report feedback.");
        Assert.Contains(
            "_ = GlobalFree(memory);\n                SystemGenesisGuiNextChannelContextFeedback.ReportClipboardCopyFailure(owner);",
            source,
            StringComparison.Ordinal);
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
