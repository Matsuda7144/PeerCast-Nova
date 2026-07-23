using System.Diagnostics;

namespace PeerCast.Nova.Cli.Tests;

public sealed class ControlledWindowsDecodedAudioSliceProbeHelperContractTests
{
    [Fact]
    public void Helper_gates_clean_exact_main_runtime_fixture_and_unused_external_record()
    {
        var source = File.ReadAllText(GetHelperPath());
        var command = source.IndexOf(
            "--controlled-windows-decoded-audio-slice-probe",
            StringComparison.Ordinal);

        Assert.Equal(
            1,
            Count(source, "--controlled-windows-decoded-audio-slice-probe"));
        Assert.True(command > source.IndexOf(
            "HEAD and origin/main must match the approved commit",
            StringComparison.Ordinal));
        Assert.True(command > source.IndexOf(
            "the repository working tree must be clean",
            StringComparison.Ordinal));
        Assert.True(command > source.IndexOf(
            "an approved runtime load-role DLL SHA-256 does not match",
            StringComparison.Ordinal));
        Assert.True(command > source.IndexOf(
            "the controlled fixture SHA-256 does not match",
            StringComparison.Ordinal));
        Assert.True(command > source.IndexOf(
            "the requested record already exists",
            StringComparison.Ordinal));
        Assert.Contains("Move-Item -LiteralPath $temporaryRecord", source);
        Assert.Contains("postStatus", source);
    }

    [Fact]
    public void Operator_question_is_emitted_only_for_zero_exit_after_atomic_record()
    {
        var source = File.ReadAllText(GetHelperPath());
        var promote = source.IndexOf(
            "Move-Item -LiteralPath $temporaryRecord",
            StringComparison.Ordinal);
        var acceptedGate = source.IndexOf(
            "if ($runExitCode -eq 0)",
            promote,
            StringComparison.Ordinal);
        var question = source.IndexOf(
            "Decoded fixture audio recognizable: Yes / No",
            StringComparison.Ordinal);

        Assert.True(promote >= 0);
        Assert.True(acceptedGate > promote);
        Assert.True(question > acceptedGate);
    }

    [Fact]
    public void Helper_is_valid_Windows_PowerShell_syntax_without_policy_mutation()
    {
        var path = GetHelperPath();
        var source = File.ReadAllText(path);
        Assert.DoesNotContain("Set-ExecutionPolicy", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Start-Process", source, StringComparison.OrdinalIgnoreCase);

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            Arguments =
                "-NoProfile -Command \"$tokens=$null;$errors=$null;" +
                "[System.Management.Automation.Language.Parser]::ParseFile(" +
                "$env:PCN_SLICE_HELPER,[ref]$tokens,[ref]$errors)|Out-Null;" +
                "if($errors.Count -ne 0){exit 1}\"",
            Environment =
            {
                ["PCN_SLICE_HELPER"] = path,
            },
        });
        Assert.NotNull(process);
        Assert.True(process.WaitForExit(10_000));
        Assert.Equal(0, process.ExitCode);
    }

    private static string GetHelperPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "tools",
                "Run-ControlledWindowsDecodedAudioSliceProbe.ps1");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException("The decoded-audio slice helper was not found.");
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
