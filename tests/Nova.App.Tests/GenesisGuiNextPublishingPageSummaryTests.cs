using PeerCast.Nova.Windows;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisGuiNextPublishingPageSummaryTests
{
    private static NovaObsPublishingReadOnlySnapshot Status() => new(NovaObsPublishingAvailability.NotStarted,
        Guid.Parse("11111111-2222-3333-4444-555555555555"), Guid.Parse("AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE"),
        "未起動", "", NovaObsPublishingTone.Normal, null, null, null);

    [Fact]
    public void SummaryUsesSavedValuesWithoutApplyingOrEditingThem()
    {
        var saved = PublishingSettingsSnapshot.Default with { Values = new("検証チャンネル", "トーク", "概要", "コメント", "https://example.invalid/contact", 1800) };
        var page = GenesisGuiNextPublishingPageSummary.Create(saved, null, false, Status(), "", "");
        Assert.Contains("検証チャンネル", page.Basic); Assert.Contains("トーク", page.Basic);
        Assert.Contains("概要", page.Basic); Assert.Contains("コメント", page.Basic);
        Assert.Contains("https://example.invalid/contact", page.Basic);
        Assert.Contains("1800 kbps", page.Options); Assert.Contains("このPCのみ", page.Options);
        Assert.Contains("未適用", page.Options); Assert.Contains("次回", page.Options);
        Assert.Contains("YP掲載: 無効", page.Options);
        Assert.Equal("検証チャンネル", saved.Values.ChannelName);
        Assert.DoesNotContain("検証チャンネル", page.ToString());
    }

    [Theory]
    [InlineData("https://user:password@example.invalid/contact?token=secret#fragment")]
    [InlineData("https://example.invalid/contact?token=secret")]
    [InlineData("https://example.invalid/contact#fragment")]
    public void RoutineSummaryOmitsUrlCredentialsQueryAndFragment(string url)
    {
        var saved = PublishingSettingsSnapshot.Default with { Values = PublishingChannelPreferences.Default with { ContactUrl = url } };
        var page = GenesisGuiNextPublishingPageSummary.Create(saved, null, false, Status(), "", "");
        Assert.DoesNotContain("password", page.Basic); Assert.DoesNotContain("user:", page.Basic);
        Assert.DoesNotContain("secret", page.Basic); Assert.DoesNotContain("fragment", page.Basic);
        Assert.Contains("example.invalid/contact", page.Basic);
        Assert.Contains("一部省略", page.Basic);
        Assert.Equal(url, saved.Values.ContactUrl);
    }

    [Fact]
    public void UnavailableAndFailedSettingsNeverBecomeUsableDefaults()
    {
        var unavailable = GenesisGuiNextPublishingPageSummary.Create(null, null, false, null, "", "");
        Assert.Contains("確認できません", unavailable.Basic); Assert.Contains("確認できません", unavailable.Diagnostic);
        var failed = GenesisGuiNextPublishingPageSummary.Create(PublishingSettingsSnapshot.Default, null, true, null, "", "");
        Assert.Equal(PublishingSettingsController.LoadWarning, failed.Basic);
        Assert.Equal(failed.Basic, failed.Options);
    }

    [Fact]
    public void SavedAndPreviouslyAppliedSnapshotsStayDistinctAndLongTextIsNotTruncated()
    {
        var text = new string('長', 5000);
        var saved = PublishingSettingsSnapshot.Default with { Values = PublishingChannelPreferences.Default with { Description = text } };
        var changed = GenesisGuiNextPublishingPageSummary.Create(saved, PublishingSettingsSnapshot.Default, false, Status(), "", "");
        Assert.Contains(text, changed.Basic); Assert.Contains("保存値と異なります", changed.Options);
        var same = GenesisGuiNextPublishingPageSummary.Create(saved, saved, false, Status(), "", "");
        Assert.Contains("保存値と一致", same.Options);
    }

    [Fact]
    public void DiagnosticRetainsIdentityCompletionAndUnprovenCleanupWithoutClaimingSuccess()
    {
        var state = Status() with { CompletionKind = NovaObsPublishingCompletionKind.Cancelled, CleanupCompleted = false };
        var page = GenesisGuiNextPublishingPageSummary.Create(null, null, false, state, "操作結果未確認", "接続情報を利用できません");
        Assert.Contains("11111111222233334444555555555555", page.Diagnostic);
        Assert.Contains("AAAAAAAABBBBCCCCDDDDEEEEEEEEEEEE", page.Diagnostic);
        Assert.Contains(GenesisGuiNextPublishingPresentation.ErrorText(state), page.Diagnostic);
        Assert.Contains("未証明", page.Diagnostic); Assert.Contains("操作結果未確認", page.Diagnostic);
        Assert.Contains("接続情報を利用できません", page.Diagnostic);
    }

    [Fact]
    public void SavedWanAndYpRemainUnverifiedAndSeparateFromPreviouslyAppliedLoopback()
    {
        var saved = PublishingSettingsSnapshot.Default with
        {
            Values = PublishingChannelPreferences.Default with
            {
                Wan = new(true, "saved-nic", "192.168.1.20", 7144, "8.8.8.8", 7145),
                Announce = new(true, "saved YP", "pcp://tracker.invalid:7144"),
            },
        };
        var page = GenesisGuiNextPublishingPageSummary.Create(saved, PublishingSettingsSnapshot.Default, false, Status(), "", "");
        Assert.Contains("saved-nic / 192.168.1.20:7144", page.Options);
        Assert.Contains("8.8.8.8:7145（到達性未確認）", page.Options);
        Assert.Contains("ON設定（掲載未確認） / saved YP / pcp://tracker.invalid:7144", page.Options);
        Assert.Contains("保存値と異なります / 適用モード: このPCのみ", page.Options);
        Assert.Contains("保存だけでは通信しません", page.Options);
    }

    [Fact]
    public void LargeLayoutCapsInformationWidthAndHeightRatherThanStretchingCards()
    {
        var medium = new GenesisGuiNextPublishingModernLayout(1440, 960);
        var large = new GenesisGuiNextPublishingModernLayout(2400, 1300);
        Assert.Equal(medium.ObsCard.Width, large.ObsCard.Width);
        Assert.Equal(medium.BasicCard.Height, large.BasicCard.Height);
        Assert.Equal(medium.Start.Top, large.Start.Top);
        Assert.Equal((2400 - 1440) / 2, large.Title.Left - medium.Title.Left);
    }
}
