namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextTopStatusPresentationTests
{
    [Fact]
    public void UnavailableMeasurementsRemainExplicitlyUnknown()
    {
        Assert.Equal(
            "接続 — | P2Pノード数 — | 上り — | 下り — | ポート開放 —",
            GenesisGuiNextTopStatusPresentation.FormatUnavailable());
    }
}
