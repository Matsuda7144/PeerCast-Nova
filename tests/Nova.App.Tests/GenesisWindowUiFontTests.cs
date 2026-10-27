using System.Runtime.InteropServices;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisWindowUiFontTests
{
    private static readonly string[] JapaneseCoverageLabels =
    [
        "配信開始",
        "配信終了",
        "配信中断",
        "更新",
        "情報をコピー",
        "保存",
        "参照...",
    ];

    [Fact]
    public void SystemMessageFontWithJapaneseCoverageIsPreferred()
    {
        var api = new FontApi("System UI");
        api.Supported.Add("System UI");
        using var font = GenesisUiFontLease.TryCreate(api, 96);
        Assert.NotNull(font);
        Assert.Single(api.Created);
        Assert.Equal("System UI", api.Created[0].FaceName);
        Assert.Empty(api.Deleted);
    }

    [Theory]
    [InlineData("Yu Gothic UI", 2)]
    [InlineData("Meiryo", 3)]
    [InlineData("MS UI Gothic", 4)]
    public void MissingJapaneseGlyphsUsesFirstVerifiedFallback(string supportedFamily, int attempts)
    {
        var api = new FontApi("Arial");
        api.Supported.Add(supportedFamily);
        using var font = GenesisUiFontLease.TryCreate(api, 144);
        Assert.NotNull(font);
        Assert.Equal(attempts, api.Created.Count);
        Assert.Equal(supportedFamily, api.Created[^1].FaceName);
        Assert.Equal(attempts - 1, api.Deleted.Count);
        Assert.DoesNotContain(font.Handle, api.Deleted);
        Assert.All(api.Created, spec =>
        {
            Assert.Equal(-18, spec.Height);
            Assert.Equal(400, spec.Weight);
            Assert.Equal((byte)0, spec.Italic);
        });
        Assert.Equal(144u, api.LastDpi);
    }

    [Fact]
    public void FamilyNameAloneIsNotAcceptedWithoutGlyphCoverage()
    {
        var api = new FontApi("Yu Gothic UI");
        using var font = GenesisUiFontLease.TryCreate(api, 96);
        Assert.Null(font);
        Assert.Equal(4, api.Created.Count);
        Assert.Equal(4, api.Deleted.Count);
    }

    [Fact]
    public void UnsupportedCandidatesAreReleasedAndNoInvalidHandleIsReturned()
    {
        var api = new FontApi("Arial");
        var font = GenesisUiFontLease.TryCreate(api, 96);
        Assert.Null(font);
        Assert.Equal(new nint[] { 1, 2, 3, 4 }, api.Deleted);
    }

    [Fact]
    public void FontCreationFailureDoesNotProbeOrDeleteNullHandle()
    {
        var api = new FontApi("System UI") { FailCreation = true };
        using var font = GenesisUiFontLease.TryCreate(api, 96);
        Assert.Null(font);
        Assert.Equal(4, api.Created.Count);
        Assert.Equal(0, api.Probes);
        Assert.Empty(api.Deleted);
    }

    [Fact]
    public void FailedSystemFontCreationCanStillUseVerifiedFallback()
    {
        var api = new FontApi("System UI");
        api.FailFamilies.Add("System UI");
        api.Supported.Add("Yu Gothic UI");
        using var font = GenesisUiFontLease.TryCreate(api, 96);
        Assert.NotNull(font);
        Assert.Equal("Yu Gothic UI", api.Created[^1].FaceName);
        Assert.Empty(api.Deleted);
    }

    [Theory]
    [InlineData(96u, -12)]
    [InlineData(144u, -18)]
    [InlineData(192u, -24)]
    public void MetricsQueryFailureUsesDpiScaledFallback(uint dpi, int height)
    {
        var api = new FontApi("Unused") { MetricsAvailable = false };
        api.Supported.Add("Yu Gothic UI");
        using var font = GenesisUiFontLease.TryCreate(api, dpi);
        Assert.NotNull(font);
        var requested = Assert.Single(api.Created);
        Assert.Equal("Yu Gothic UI", requested.FaceName);
        Assert.Equal(height, requested.Height);
    }

    [Fact]
    public void OwnedFontIsReleasedOnceAndHandleIsCleared()
    {
        var api = SupportedApi();
        var font = GenesisUiFontLease.TryCreate(api, 96)!;
        var handle = font.Handle;
        font.Dispose();
        font.Dispose();
        Assert.Equal(0, font.Handle);
        Assert.Equal(handle, Assert.Single(api.Deleted));
    }

    [Fact]
    public void OwnerDrawRestoresPreviousGdiObjectEvenWhenDrawingThrows()
    {
        var api = new FontApi("Unused") { PreviousObject = 19 };
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var selected = new GenesisUiFontSelection(api, 71, 29);
            throw new InvalidOperationException("Simulated draw failure");
        }));
        Assert.Equal(new (nint, nint)[] { (71, 29), (71, 19) }, api.Selections);
        Assert.Empty(api.Deleted); // Neither the selected nor the previous font is owned here.
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void FailedSelectionDoesNotRestoreAnInvalidObject(int previous)
    {
        var api = new FontApi("Unused") { PreviousObject = previous };
        using (new GenesisUiFontSelection(api, 71, 29)) { }
        Assert.Single(api.Selections);
        Assert.Empty(api.Deleted);
    }

    [Theory]
    [InlineData(0, 29)]
    [InlineData(71, 0)]
    public void MissingDcOrFontDoesNotSelectAnyObject(int dc, int font)
    {
        var api = new FontApi("Unused");
        using (new GenesisUiFontSelection(api, dc, font)) { }
        Assert.Empty(api.Selections);
    }

    [Fact]
    public void ControlsShareOneOwnedFontUntilWindowTeardown()
    {
        var api = SupportedApi();
        var window = GenesisUiWindowFont.TryCreate(api, 96)!;
        var applied = new List<(nint Control, nint Font)>();
        // Native main list/header, navigation and the three publishing commands.
        for (var control = 1; control <= 6; control++)
        {
            Assert.True(window.Apply(control, (hwnd, font) => applied.Add((hwnd, font))));
        }
        Assert.Single(api.Created);
        Assert.All(applied, entry => Assert.Equal((nint)1, entry.Font));
        Assert.Empty(api.Deleted);
        window.Dispose();
        window.Dispose();
        Assert.Equal((nint)1, Assert.Single(api.Deleted));
        Assert.False(window.Apply(7, (_, _) => throw new InvalidOperationException()));
        Assert.False(window.Refresh(144, (_, _) => throw new InvalidOperationException(), _ => true));
    }

    [Fact]
    public void RecreatedWindowGetsFreshFontOwnershipAfterPreviousTeardown()
    {
        var api = SupportedApi();
        var first = GenesisUiWindowFont.TryCreate(api, 96)!;
        nint firstFont = 0;
        first.Apply(11, (_, font) => firstFont = font);
        first.Dispose();
        Assert.Equal(firstFont, Assert.Single(api.Deleted));

        var reopened = GenesisUiWindowFont.TryCreate(api, 144)!;
        nint reopenedFont = 0;
        reopened.Apply(11, (_, font) => reopenedFont = font);
        Assert.NotEqual(firstFont, reopenedFont);
        first.Dispose();
        Assert.DoesNotContain(reopenedFont, api.Deleted);
        reopened.Dispose();
        reopened.Dispose();
        Assert.Equal(new[] { firstFont, reopenedFont }, api.Deleted);
    }

    [Fact]
    public void RootFontTeardownRunsAfterNativeNonClientDestruction()
    {
        var source = ReadSource("GenesisWindowUiFont.cs");
        var start = source.IndexOf("if (message == NonClientDestroyMessage)", StringComparison.Ordinal);
        var end = source.IndexOf("var result = DefSubclassProc", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var destruction = source[start..end];
        var dispatch = destruction.IndexOf("return DefSubclassProc(", StringComparison.Ordinal);
        var cleanup = destruction.IndexOf("font.Dispose();", StringComparison.Ordinal);
        Assert.True(dispatch >= 0 && cleanup > dispatch);
        Assert.Contains("finally", destruction, StringComparison.Ordinal);
        Assert.Contains("Fonts.Remove(window, out var font)", destruction, StringComparison.Ordinal);
    }

    [Fact]
    public void DpiRotationUpdatesControlsBeforeReleasingOldFont()
    {
        var api = SupportedApi();
        using var window = GenesisUiWindowFont.TryCreate(api, 96)!;
        window.Apply(11, (_, _) => { });
        window.Apply(12, (_, _) => { });
        var rebound = new List<nint>();
        Assert.True(window.Refresh(144, (control, font) =>
        {
            Assert.Equal((nint)2, font);
            Assert.Empty(api.Deleted);
            rebound.Add(control);
        }, _ => true));
        Assert.Equal(new nint[] { 11, 12 }, rebound);
        Assert.Equal((nint)1, Assert.Single(api.Deleted));
        Assert.Equal(-18, api.Created[^1].Height);
    }

    [Fact]
    public void FailedDpiRotationKeepsExistingFont()
    {
        var api = SupportedApi();
        using var window = GenesisUiWindowFont.TryCreate(api, 96)!;
        window.Apply(11, (_, _) => { });
        api.FailCreation = true;
        Assert.False(window.Refresh(144, (_, _) => throw new InvalidOperationException(), _ => true));
        Assert.Empty(api.Deleted);
        Assert.True(window.Apply(12, (_, font) => Assert.Equal((nint)1, font)));
    }

    [Fact]
    public void DpiRotationDoesNotTouchDestroyedOrReusedForeignHwnd()
    {
        var api = SupportedApi();
        using var window = GenesisUiWindowFont.TryCreate(api, 96)!;
        window.Apply(11, (_, _) => { });
        window.Apply(12, (_, _) => { });
        var rebound = new List<nint>();
        Assert.True(window.Refresh(144, (control, _) => rebound.Add(control), control => control == 12));
        Assert.Equal((nint)12, Assert.Single(rebound));
        rebound.Clear();
        Assert.True(window.Refresh(192, (control, _) => rebound.Add(control), _ => true));
        Assert.Equal((nint)12, Assert.Single(rebound));
    }

    [Fact]
    public void ReentrantWindowTeardownDuringRotationReleasesBothGenerationsOnce()
    {
        var api = SupportedApi();
        using var window = GenesisUiWindowFont.TryCreate(api, 96)!;
        window.Apply(11, (_, _) => { });
        window.Apply(12, (_, _) => { });
        var count = 0;
        Assert.True(window.Refresh(144, (_, _) =>
        {
            count++;
            window.Dispose();
        }, _ => true));
        Assert.Equal(1, count);
        Assert.Equal(new nint[] { 2, 1 }, api.Deleted);
    }

    [Fact]
    public void UnicodeNativeFontStructuresHaveExpectedWindowsSizes()
    {
        Assert.Equal(92, Marshal.SizeOf<GenesisUiLogFont>());
        Assert.Equal(504, SystemUiFontNativeApi.MetricsSize);
    }

    [Fact]
    public void CoverageProbeIncludesNavigationAndPublishingLabels()
    {
        var labels = string.Concat(GenesisGuiNextNavigationPresentation.Items.Concat(JapaneseCoverageLabels));
        var runtimeSources = new[]
        {
            "GenesisChannelSearchBoxBridge.cs",
            "GenesisGuiNextAppShellBridge.cs",
            "SystemGenesisMainWindowSurfaceTarget.cs",
            "GenesisGuiNextPlaybackSettingsWindow.cs",
            "GenesisGuiNextConnectionSettingsWindow.cs",
            "GenesisGuiNextConnectionSettings.cs",
            "GenesisGuiNextRelayActivation.cs",
            "GenesisGuiNextPublishingPresentation.cs",
            "GenesisGuiNextPublishingRefresh.cs",
            "GenesisGuiNextPublishingConnection.cs",
            "GenesisGuiNextPublishingNativeGuide.cs",
            "NovaObsPublishingConnectionInfo.cs",
            "NovaGuiNextPublishingCommands.cs",
            "NovaGuiNextPublishingComposition.cs",
        };
        var sourceCharacters = string.Concat(runtimeSources.Select(ReadSource))
            .Where(static character => character > 127);
        foreach (var character in labels.Concat(sourceCharacters).Where(static character => character > 127).Distinct())
        {
            Assert.Contains(character, SystemUiFontNativeApi.JapaneseSample);
        }
    }

    [Theory]
    [InlineData(96u, 96u, -12, 4, -12, 4)]
    [InlineData(144u, 96u, -12, 4, -18, 6)]
    [InlineData(192u, 96u, -12, 0, -24, 0)]
    public void LegacySystemMetricsScaleToWindowDpi(
        uint targetDpi,
        uint systemDpi,
        int height,
        int width,
        int expectedHeight,
        int expectedWidth)
    {
        var scaled = SystemUiFontNativeApi.ScaleFontForDpi(
            new GenesisUiLogFont { Height = height, Width = width },
            targetDpi,
            systemDpi);
        Assert.Equal(expectedHeight, scaled.Height);
        Assert.Equal(expectedWidth, scaled.Width);
    }

    [Fact]
    public void WindowsGdiAppliesOneJapaneseCapableFontToNativeControls()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const uint WindowStylePopup = 0x80000000;
        const uint WindowStyleChild = 0x40000000;
        var instance = GetModuleHandleW(null);
        var root = CreateWindowExW(
            0,
            "STATIC",
            "Japanese font test",
            WindowStylePopup,
            0,
            0,
            1,
            1,
            0,
            0,
            instance,
            0);
        Assert.NotEqual(0, root);

        var controls = new[]
        {
            CreateWindowExW(0, "STATIC", "配信設定", WindowStyleChild, 0, 0, 200, 24, root, 0, instance, 0),
            CreateWindowExW(0, "EDIT", "日本語", WindowStyleChild, 0, 24, 200, 24, root, 0, instance, 0),
            CreateWindowExW(0, "BUTTON", "保存", WindowStyleChild, 0, 48, 100, 24, root, 0, instance, 0),
        };

        nint appliedFont = 0;
        try
        {
            Assert.All(controls, control => Assert.NotEqual(0, control));
            Assert.All(controls, control => Assert.True(GenesisWindowUiFont.Apply(control)));
            var fonts = controls
                .Select(control => SendMessageW(control, 0x0031, 0, 0))
                .ToArray();
            Assert.All(fonts, font => Assert.NotEqual(0, font));
            Assert.Single(fonts.Distinct());
            Assert.True(new SystemUiFontNativeApi().HasJapaneseGlyphs(fonts[0]));
            appliedFont = fonts[0];
            Assert.Equal(6u, GetObjectType(appliedFont)); // OBJ_FONT
        }
        finally
        {
            _ = DestroyWindow(root);
        }

        Assert.Equal(0, GetObjectW(appliedFont, Marshal.SizeOf<GenesisUiLogFont>(), out _));
    }

    private static FontApi SupportedApi()
    {
        var api = new FontApi("System UI");
        api.Supported.Add("System UI");
        return api;
    }

    private static string ReadSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var sourcePath = Path.Combine(directory.FullName, "src", "Nova.App", fileName);
            if (File.Exists(sourcePath))
            {
                return File.ReadAllText(sourcePath);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Repository source was not found: {fileName}");
    }

    private sealed class FontApi(string family) : IGenesisUiFontNativeApi
    {
        public HashSet<string> Supported { get; } = new();
        public HashSet<string> FailFamilies { get; } = new();
        public List<GenesisUiLogFont> Created { get; } = new();
        public List<nint> Deleted { get; } = new();
        public List<(nint, nint)> Selections { get; } = new();
        private readonly Dictionary<nint, string> _families = new();
        public bool MetricsAvailable { get; set; } = true;
        public bool FailCreation { get; set; }
        public uint LastDpi { get; private set; }
        public int Probes { get; private set; }
        public nint PreviousObject { get; set; } = 19;

        public bool TryGetMessageFont(uint dpi, out GenesisUiLogFont font)
        {
            LastDpi = dpi;
            font = new GenesisUiLogFont { FaceName = family, Height = -(int)(12 * dpi / 96), Weight = 400 };
            return MetricsAvailable;
        }

        public nint CreateFont(GenesisUiLogFont font)
        {
            Created.Add(font);
            if (FailCreation || FailFamilies.Contains(font.FaceName)) return 0;
            nint handle = Created.Count;
            _families.Add(handle, font.FaceName);
            return handle;
        }

        public bool HasJapaneseGlyphs(nint font)
        {
            Probes++;
            return Supported.Contains(_families[font]);
        }

        public void DeleteFont(nint font) => Deleted.Add(font);
        public nint SelectFont(nint dc, nint font)
        {
            Selections.Add((dc, font));
            return PreviousObject;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandleW(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowExW(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint SendMessageW(nint window, uint message, nuint wParam, nint lParam);

    [DllImport("gdi32.dll")]
    private static extern uint GetObjectType(nint value);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetObjectW(nint value, int size, out GenesisUiLogFont font);
}
