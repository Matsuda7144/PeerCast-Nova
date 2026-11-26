namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextConnectionSettingsFontTests
{
    [Theory]
    [InlineData(0x0100u, 9, false, false, 1)]
    [InlineData(0x0100u, 9, true, false, 2)]
    [InlineData(0x0100u, 27, false, false, 3)]
    [InlineData(0x0100u, 27, false, true, 3)]
    [InlineData(0x0100u, 13, false, true, 4)]
    [InlineData(0x0100u, 13, false, false, 0)]
    [InlineData(0x0101u, 9, false, false, 0)]
    [InlineData(0x0100u, 32, false, true, 0)]
    public void KeyboardRoutingPreservesEditInputAndRequiresButtonForEnter(uint message, int key,
        bool shift, bool button, int expected) =>
        Assert.Equal((GenesisConnectionSettingsKeyAction)expected, GenesisGuiNextConnectionSettingsWindow.ResolveKey(message, (nuint)key, shift, button));

    [Fact]
    public void JapaneseCoverageFallbackAndOwnedWindowLifetimeArePreserved()
    {
        var api = new FontApi();
        var window = GenesisUiWindowFont.TryCreate(api, 144);
        Assert.NotNull(window);
        Assert.Equal("System UI,Yu Gothic UI,Meiryo", string.Join(',', api.Faces));
        Assert.Equal(new nint[] { 1, 2 }, api.Deleted);
        var applied = new List<nint>();
        Assert.True(window.Apply(11, (_, font) => applied.Add(font)));
        Assert.True(window.Apply(12, (_, font) => applied.Add(font)));
        Assert.Equal(new nint[] { 3, 3 }, applied);
        Assert.DoesNotContain((nint)3, api.Deleted);
        Assert.True(window.Refresh(192, (_, font) => applied.Add(font), _ => true));
        Assert.Equal(192u, api.Dpi);
        Assert.Contains((nint)3, api.Deleted);
        window.Dispose();
        window.Dispose();
        Assert.Equal(api.Faces.Count, api.Deleted.Distinct().Count());
    }

    [Theory]
    [InlineData(96u, 700)]
    [InlineData(144u, 1050)]
    [InlineData(192u, 1400)]
    public void DialogLayoutUsesTheSameDpiScaleAsTheOwnedFont(uint dpi, int width) =>
        Assert.Equal(width, GenesisGuiNextConnectionSettingsWindow.Scale(700, dpi));

    [Fact]
    public void NativeWiringUsesSharedFontAndBackgroundSaveWithoutRuntimeApply()
    {
        var source = RepositorySourceTestHelper.ReadAppSource("GenesisGuiNextConnectionSettingsWindow.cs");
        Assert.Contains("GenesisWindowUiFont.Apply(control)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetStockObject", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DEFAULT_GUI_FONT", source, StringComparison.Ordinal);
        Assert.Contains("Task.Run", source, StringComparison.Ordinal);
        Assert.Contains("PostMessageW(window, SaveCompleted", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyAsync", source, StringComparison.Ordinal);
        var stateSource = RepositorySourceTestHelper.ReadAppSource("GenesisGuiNextApplicationState.cs");
        Assert.Contains("new JsonGenesisGuiNextConnectionPreferencesStore(", stateSource, StringComparison.Ordinal);
        Assert.Contains("connection-preferences.json", stateSource, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(applicationDirectory, ConnectionPreferencesFileName)", stateSource, StringComparison.Ordinal);
    }

    private sealed class FontApi : IGenesisUiFontNativeApi
    {
        public List<string> Faces { get; } = [];
        public List<nint> Deleted { get; } = [];
        public uint Dpi { get; private set; }
        public bool TryGetMessageFont(uint dpi, out GenesisUiLogFont font)
        { Dpi = dpi; font = new() { FaceName = "System UI", Height = -(int)(12 * dpi / 96), Weight = 400 }; return true; }
        public nint CreateFont(GenesisUiLogFont font) { Faces.Add(font.FaceName); return Faces.Count; }
        public bool HasJapaneseGlyphs(nint font) => Faces[(int)font - 1] == "Meiryo";
        public void DeleteFont(nint font) => Deleted.Add(font);
        public nint SelectFont(nint dc, nint font) => 0;
    }
}
