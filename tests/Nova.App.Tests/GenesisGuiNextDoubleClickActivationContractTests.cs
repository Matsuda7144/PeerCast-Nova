namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextDoubleClickActivationContractTests
{
    [Fact]
    public void NativeDoubleClickActivatesOnlyInteractiveRenderedRows()
    {
        var source = File.ReadAllText(FindRepositoryFile(
            "src",
            "Nova.App",
            "SystemGenesisMainWindowInteractionBridgeFactory.cs"));
        var processNotify = ExtractMethod(
            source,
            "public void ProcessNotify(");

        Assert.Contains(
            "code != NotifyDoubleClick",
            processNotify,
            StringComparison.Ordinal);
        Assert.Contains(
            "var interactiveChannel = _renderedRows[selectedIndex].InteractiveChannel;",
            processNotify,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (interactiveChannel is not null)",
            processNotify,
            StringComparison.Ordinal);
        Assert.Contains(
            "_interaction.ActivateChannelAsync(interactiveChannel)",
            processNotify,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ActivateChannelAsync(_renderedRows[selectedIndex].DisplayChannel",
            processNotify,
            StringComparison.Ordinal);
    }

    private static string ExtractMethod(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Method was not found: {signature}");

        var openingBrace = source.IndexOf('{', start);
        Assert.True(openingBrace >= 0, $"Method body was not found: {signature}");

        var depth = 0;
        for (var index = openingBrace; index < source.Length; index++)
        {
            if (source[index] == '{')
            {
                depth++;
            }
            else if (source[index] == '}' && --depth == 0)
            {
                return source[start..(index + 1)];
            }
        }

        throw new InvalidOperationException(
            $"Method body was incomplete: {signature}");
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
