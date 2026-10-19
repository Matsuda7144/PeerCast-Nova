namespace PeerCast.Nova.App.Tests;

public sealed class GenesisSourceTextProjectionTests
{
    [Fact]
    public void ObservedSourceIsNotProjectedAgain()
    {
        var projection = new GenesisSourceTextProjection();

        Assert.Equal("pcp://example/channel", projection.Project("pcp://example/channel"));
        projection.ObserveNativeEdit("pcp://example/channel");

        Assert.Null(projection.Project("pcp://example/channel"));
    }

    [Fact]
    public void UnobservedSourceIsProjectedAgain()
    {
        var projection = new GenesisSourceTextProjection();

        Assert.Equal("pcp://example/channel", projection.Project("pcp://example/channel"));
        Assert.Equal("pcp://example/channel", projection.Project("pcp://example/channel"));
    }

    [Fact]
    public void ChangedSourceIsProjectedAgain()
    {
        var projection = new GenesisSourceTextProjection();
        _ = projection.Project("pcp://example/first");
        projection.ObserveNativeEdit("pcp://example/first");

        Assert.Equal("pcp://example/second", projection.Project("pcp://example/second"));
        projection.ObserveNativeEdit("pcp://example/second");
        Assert.Null(projection.Project("pcp://example/second"));
    }

    [Fact]
    public void NativeEditIsNotProjectedBackUntilSourceChanges()
    {
        var projection = new GenesisSourceTextProjection();
        projection.ObserveNativeEdit("pcp://example/typed");

        Assert.Null(projection.Project("pcp://example/typed"));
        Assert.Equal("pcp://example/selected", projection.Project("pcp://example/selected"));
    }
}
