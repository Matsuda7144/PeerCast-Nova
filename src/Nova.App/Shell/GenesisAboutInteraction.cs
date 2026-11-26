using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal enum GenesisAboutInteractionResult
{
    None,
    Presented,
    Rejected,
}

internal interface IGenesisAboutDialog
{
    void Show(NovaAboutLegalModel model);
}

internal sealed class GenesisAboutInteraction
{
    private readonly NovaAboutLegalModel _model;
    private readonly IGenesisAboutDialog _dialog;

    public GenesisAboutInteraction(
        NovaAboutLegalModel model,
        IGenesisAboutDialog dialog)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(dialog);
        _model = model;
        _dialog = dialog;
    }

    public GenesisAboutInteractionResult Show()
    {
        try
        {
            _dialog.Show(_model);
            return GenesisAboutInteractionResult.Presented;
        }
        catch
        {
            return GenesisAboutInteractionResult.Rejected;
        }
    }
}

internal static class GenesisAboutSummary
{
    public static string Create(NovaAboutLegalModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        return string.Join(
            Environment.NewLine,
            model.ProductName,
            $"Version: {NovaReleaseUpdateChecker.GetCurrentVersion().ToTagString()}",
            string.Empty,
            $"Project license: {model.ProjectLicense}",
            $"External runtime: {model.ExternalRuntimeName}",
            model.ExternalRuntimeDistribution,
            string.Empty,
            "FFmpeg is distributed separately under its own license terms.",
            "Use Nova.App --about for the complete notice, provenance, and runtime replacement information.");
    }
}

internal sealed class SystemGenesisAboutDialog : IGenesisAboutDialog
{
    private const uint MessageBoxOk = 0x00000000;
    private const uint MessageBoxIconInformation = 0x00000040;
    private readonly nint _owner;

    public SystemGenesisAboutDialog(nint owner)
    {
        ArgumentOutOfRangeException.ThrowIfZero(owner);
        _owner = owner;
    }

    public void Show(NovaAboutLegalModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException();
        }

        var result = MessageBoxW(
            _owner,
            GenesisAboutSummary.Create(model),
            $"About {model.ProductName}",
            MessageBoxOk | MessageBoxIconInformation);
        if (!IsPresentedResult(result))
        {
            throw new InvalidOperationException("Unable to show the Genesis About dialog.");
        }
    }

    internal static bool IsPresentedResult(int result) => result != 0;

}
