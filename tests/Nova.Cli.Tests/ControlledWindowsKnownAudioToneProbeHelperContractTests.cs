namespace PeerCast.Nova.Cli.Tests;

public sealed class ControlledWindowsKnownAudioToneProbeHelperContractTests
{
    [Fact]
    public void Helper_gates_external_unused_path_and_clean_tree_before_one_probe_command()
    {
        var source = File.ReadAllText(GetHelperPath());
        var command = source.IndexOf(
            "--controlled-windows-known-audio-tone-probe",
            StringComparison.Ordinal);

        Assert.Equal(1, Count(source, "--controlled-windows-known-audio-tone-probe"));
        Assert.True(command > source.IndexOf(
            "the known-audio tone probe record must remain repository-external",
            StringComparison.Ordinal));
        Assert.True(command > source.IndexOf(
            "the requested record already exists",
            StringComparison.Ordinal));
        Assert.True(command > source.IndexOf(
            "the repository working tree must be clean",
            StringComparison.Ordinal));
        Assert.Contains("Move-Item -LiteralPath $temporaryRecord", source);
        Assert.Contains(
            "Operator observation required: Known audio tone audible: Yes / No",
            source);
    }

    [Fact]
    public void Probe_source_has_no_media_runtime_scheduler_or_video_present_dependency()
    {
        var root = new FileInfo(GetHelperPath()).Directory?.Parent ??
            throw new DirectoryNotFoundException();
        var source = File.ReadAllText(Path.Combine(
            root.FullName,
            "src",
            "Nova.Cli",
            "PlaybackControlledWindowsKnownAudioToneProbe.cs"));

        Assert.DoesNotContain("FFmpeg", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Fixture", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Decoder", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Scheduler", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ProcessVideoAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain(".Present", source, StringComparison.Ordinal);
        Assert.Equal(1, Count(source, "ProcessAudioAsync(samples"));
    }

    private static string GetHelperPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "tools",
                "Run-ControlledWindowsKnownAudioToneProbe.ps1");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("The controlled tone helper was not found.");
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
