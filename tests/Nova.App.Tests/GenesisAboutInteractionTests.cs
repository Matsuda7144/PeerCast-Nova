namespace PeerCast.Nova.App.Tests;

public sealed class GenesisAboutInteractionTests
{
    [Fact]
    public void ShowPresentsExactBoundedLegalModel()
    {
        var model = Assert.IsType<NovaAboutLegalModel>(
            NovaAboutLegalMaterial.Load().Model);
        var dialog = new RecordingDialog();
        var interaction = new GenesisAboutInteraction(model, dialog);

        var result = interaction.Show();

        Assert.Equal(GenesisAboutInteractionResult.Presented, result);
        Assert.Same(model, dialog.Model);
    }

    [Fact]
    public void ShowConvertsDialogFailureToRejectedResult()
    {
        var model = Assert.IsType<NovaAboutLegalModel>(
            NovaAboutLegalMaterial.Load().Model);
        var interaction = new GenesisAboutInteraction(
            model,
            new RecordingDialog { Failure = new InvalidOperationException() });

        var result = interaction.Show();

        Assert.Equal(GenesisAboutInteractionResult.Rejected, result);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    public void NativeDialogResultReflectsPresentation(int result, bool expected)
    {
        Assert.Equal(expected, SystemGenesisAboutDialog.IsPresentedResult(result));
    }

    [Fact]
    public void SummaryKeepsProductLicenseAndExternalRuntimeBoundaryVisible()
    {
        var model = Assert.IsType<NovaAboutLegalModel>(
            NovaAboutLegalMaterial.Load().Model);

        var summary = GenesisAboutSummary.Create(model);

        Assert.Contains(model.ProductName, summary, StringComparison.Ordinal);
        Assert.Contains(model.ProjectLicense, summary, StringComparison.Ordinal);
        Assert.Contains(model.ExternalRuntimeName, summary, StringComparison.Ordinal);
        Assert.Contains(model.ExternalRuntimeDistribution, summary, StringComparison.Ordinal);
        Assert.Contains("Nova.App --about", summary, StringComparison.Ordinal);
        Assert.DoesNotContain(model.Notice, summary, StringComparison.Ordinal);
    }

    private sealed class RecordingDialog : IGenesisAboutDialog
    {
        public NovaAboutLegalModel? Model { get; private set; }

        public Exception? Failure { get; init; }

        public void Show(NovaAboutLegalModel model)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Model = model;
        }
    }
}
