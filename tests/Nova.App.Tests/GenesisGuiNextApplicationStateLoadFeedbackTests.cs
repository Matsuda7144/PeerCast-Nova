namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextApplicationStateLoadFeedbackTests
{
    [Fact]
    public void InteractiveSurfaceReportsLoadedDomainFailuresWithMainWindowOwner()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "SystemGenesisInteractiveMainWindowSurfaceTarget.cs"));

        Assert.Contains(
            "if (guiNextState is not null)\n" +
            "        {\n" +
            "            SystemGenesisGuiNextApplicationStateLoadFeedback.Report(\n" +
            "                windowToken,\n" +
            "                guiNextState.LoadFailures);\n" +
            "        }",
            source,
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            source.Split(
                "SystemGenesisGuiNextApplicationStateLoadFeedback.Report(",
                StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void SystemStateLoadDoesNotPresentBeforeMainWindowOwnerIsAvailable()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "GenesisGuiNextApplicationState.cs"));

        Assert.DoesNotContain(
            "SystemGenesisGuiNextApplicationStateLoadFeedback.Report",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SystemFeedbackUsesExplicitMainWindowOwner()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "GenesisGuiNextApplicationStateLoadFeedback.cs"));

        Assert.Contains("var owner = (nint)windowToken;", source, StringComparison.Ordinal);
        Assert.Contains("!IsWindow(owner)", source, StringComparison.Ordinal);
        Assert.Contains(
            "_ = MessageBoxW(\n                    owner,",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("GetForegroundWindow", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(params string[] relativeParts) =>
        RepositorySourceTestHelper.FindRepositoryFile(relativeParts);
}
