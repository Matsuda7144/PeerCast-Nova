namespace PeerCast.Nova.Cli.Tests;

public sealed class PlaybackControlledWindowsAudioRenderEvidenceHelperContractTests
{
    private static readonly string[] RequiredFragments =
    [
        "[string]$ExpectedCommit",
        "[string]$RuntimeDirectory",
        "[string]$FixturePath",
        "[string]$VideoRecordPath",
        "[string]$RenderEvidenceRecordPath",
        "[string]$FailureRecordPath",
        "git branch --show-current",
        "git rev-parse HEAD",
        "git status --porcelain --untracked-files=all",
        "--verify-controlled-windows-run-record",
        "--capture-controlled-windows-audio-render-evidence",
        "--render-evidence-record-path",
        "PEERCAST-NOVA-CONTROLLED-WINDOWS-AUDIO-RENDER-EVIDENCE 1",
        "Delivery-Attempts: 4",
        "Delivered: 4",
        "Retained-WouldBlock: 0",
        "Physical-Runs-Executed-By-This-Command: True",
        "Audio-Acceptance-Claimed: False",
        "External-Binary-Or-Payload-Published: False",
        "Operator observation required: Audio audible: Yes / No",
        "Do not rerun this command.",
    ];

    [Fact]
    public void Helper_retains_clean_main_video_gate_and_single_run_contract()
    {
        var script = File.ReadAllText(FindScript());

        foreach (var fragment in RequiredFragments)
        {
            Assert.Contains(fragment, script, StringComparison.Ordinal);
        }
        Assert.DoesNotContain(
            "--capture-controlled-windows-delivery-diagnostic",
            script,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            PlaybackControlledWindowsPhysicalRunCommand.DiagnoseAudioNativeStagesArgument,
            script,
            StringComparison.Ordinal);
        Assert.Equal(
            1,
            Count(
                script,
                "--capture-controlled-windows-audio-render-evidence"));
    }

    private static string FindScript()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "tools",
                "Run-ControlledWindowsPhysicalAudioRenderEvidence.ps1");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "The controlled audio render evidence helper was not found.");
    }

    private static int Count(string value, string fragment)
    {
        var count = 0;
        var index = 0;
        while ((index = value.IndexOf(fragment, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += fragment.Length;
        }
        return count;
    }
}
