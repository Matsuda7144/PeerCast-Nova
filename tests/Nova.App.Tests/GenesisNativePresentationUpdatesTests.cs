namespace PeerCast.Nova.App.Tests;

public sealed class GenesisNativePresentationUpdatesTests
{
    [Fact]
    public void StableRefreshSuppressesWritesAndInvalidationsButChangesAndResetRender()
    {
        var updates = new GenesisNativePresentationUpdates();
        var writes = new List<object>();
        var invalidations = new List<nint>();
        void Render(string text, int width, bool enabled, string paint)
        {
            using var batch = updates.Begin(paint, invalidations.Add);
            updates.Set(1, "text", text, () => { writes.Add(text); return true; });
            updates.Set(1, "bounds", width, () => { writes.Add(width); return true; });
            updates.Set(1, "enabled", enabled, () => { writes.Add(enabled); return true; });
            updates.Invalidate(2);
        }
        Render("日本語", 400, false, "idle");
        Assert.Equal(3, writes.Count); Assert.Single(invalidations);
        Render("日本語", 400, false, "idle");
        Assert.Equal(3, writes.Count); Assert.Single(invalidations);
        Render("変更", 500, true, "active");
        Assert.Equal(6, writes.Count); Assert.Equal(2, invalidations.Count);
        Render("変更", 500, true, "warning"); // Owner-draw tone changes without native text changes.
        Assert.Equal(6, writes.Count); Assert.Equal(3, invalidations.Count);
        updates.Reset(); // DPI/resize or an external write invalidates applied state.
        Render("変更", 500, true, "warning");
        Assert.Equal(9, writes.Count); Assert.Equal(4, invalidations.Count);
        var recreated = new GenesisNativePresentationUpdates();
        using (recreated.Begin("warning", invalidations.Add))
            recreated.Set(1, "text", "変更", () => { writes.Add("new lifetime"); return true; });
        Assert.Equal(10, writes.Count);
    }

    [Fact]
    public void CloseBeforeFlushDoesNotWriteOrInvalidate()
    {
        var updates = new GenesisNativePresentationUpdates();
        using var batch = updates.Begin(0, _ => throw new InvalidOperationException());
        updates.Set(1, "text", "old", () => throw new InvalidOperationException());
        updates.Invalidate(1);
        updates.Close();
    }

    [Fact]
    public void FinalVisibilityAndBoundsWinAndFailedWritesAreNotCached()
    {
        var updates = new GenesisNativePresentationUpdates();
        var writes = new List<int>();
        void Render(bool success)
        {
            using var batch = updates.Begin(0, _ => { });
            updates.Set(1, "visible", 1, () => throw new InvalidOperationException("intermediate show"));
            updates.Set(1, "visible", 0, () => { writes.Add(0); return true; });
            updates.Set(1, "bounds", 100, () => throw new InvalidOperationException("intermediate layout"));
            updates.Set(1, "bounds", 200, () => { writes.Add(200); return success; });
        }
        Render(false); Render(true); Render(true);
        Assert.Collection(writes, value => Assert.Equal(0, value), value => Assert.Equal(200, value), value => Assert.Equal(200, value));
    }
}
