using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed partial class GenesisAutomaticRecordingEvaluationControllerTests
{
    [Fact]
    public async Task SuppressionOnlyAllowRebuildsNativeRowsAndSelectsCompletionWithoutEvaluation()
    {
        var evaluations = 0;
        var sink = new UserStopSink();
        var rule = RecordRule();
        await using var fixture = new Fixture(
            new([rule]),
            evaluate: (rules, candidate) =>
            {
                evaluations++;
                return rules.Evaluate(candidate);
            },
            sink: sink);
        await fixture.Controller.ObserveAsync([Channel()]);
        var session = fixture.Runtime.GetState(ChannelId()).Session!;
        var committed = WaitForCommitted(fixture.Controller);
        await fixture.Controller.StopAutomaticAsync(ChannelId(), session.SessionId);
        sink.Finish(RecordingSinkStopResultKind.Stopped);
        await committed;

        await using var manual = new GenesisManualRecordingSessionController(
            fixture.OwnerProvider, fixture.Runtime);
        var viewModel = new GenesisMainViewModel(
            new RecordingTestBridge(manual, fixture.Controller));
        var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection())
        {
            SelectedTab = GenesisGuiNextTab.Recording,
        };
        tabs.UpdateActiveRecordings(viewModel.ActiveRecordings);
        tabs.UpdateSuppression(viewModel.SuppressionRows);
        viewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(GenesisMainViewModel.RecordingPresentationRevision))
            {
                tabs.UpdateActiveRecordings(viewModel.ActiveRecordings);
                tabs.UpdateSuppression(viewModel.SuppressionRows);
            }
        };

        var activeRows = tabs.ActiveRecordings;
        var catalogRows = tabs.Channels;
        var oldSuppression = tabs.SuppressionRows;
        Assert.Empty(activeRows);
        var identity = Identity();
        Assert.True(identity.RequiresRebuild(null));
        var rows = GenesisGuiNextNativeTabProjection.Create(tabs);
        var oldRow = Assert.Single(rows);
        Assert.NotNull(oldRow.Suppression);
        Assert.Null(GenesisGuiNextAppShellBridge.ResolveRecordingCompletionMessage(
            tabs.SelectedTab, tabs.RecordingPageMode, oldRow, "not selected"));

        var starts = sink.Starts;
        var stopAdmissions = sink.AdmissionCalls;
        var owners = fixture.OwnerProvider.AttachCount;
        var opens = fixture.SourceProvider.OpenCount;
        var passes = evaluations;
        var restoreCalls = 0;
        var replacements = 1;
        var listView = (nint)0x230A110;
        try
        {
            GenesisChannelListRenderedChannels.SetRows(listView, rows, Restore);
            GenesisChannelListRenderedChannels.CaptureSelectedRow(listView, 0);
            Assert.Equal(0, GenesisChannelListRenderedChannels.FindAnchoredRow(listView, rows));

            Assert.True(await GenesisGuiNextRecordingCommandDispatch.AllowRecordingRowAsync(
                viewModel, oldRow));

            Assert.Same(activeRows, tabs.ActiveRecordings);
            Assert.Same(catalogRows, tabs.Channels);
            Assert.NotSame(oldSuppression, tabs.SuppressionRows);
            Assert.Empty(tabs.SuppressionRows);
            Assert.Empty(viewModel.ActiveRecordings);
            var nextIdentity = Identity();
            Assert.True(nextIdentity.RequiresRebuild(identity));
            rows = GenesisGuiNextNativeTabProjection.Create(tabs);
            replacements++;
            GenesisChannelListRenderedChannels.SetRows(listView, rows, Restore);

            Assert.Empty(rows);
            Assert.Null(GenesisChannelListRenderedChannels.FindRow(listView, 0));
            Assert.Equal(-1, GenesisChannelListRenderedChannels.FindAnchoredRow(listView, rows));
            Assert.Equal(-1, GenesisChannelListRenderedChannels.FindAnchoredRow(listView, [oldRow]));
            Assert.Equal(0, restoreCalls);
            var selectedRow = GenesisChannelListRenderedChannels.FindRow(listView, 0);
            var message = GenesisGuiNextAppShellBridge.ResolveRecordingCompletionMessage(
                tabs.SelectedTab, tabs.RecordingPageMode, selectedRow,
                viewModel.AutomaticRecordingCommandMessage);
            Assert.Equal(
                "自動録画の抑止を解除しました。次回のチャンネル更新から通常の判定に戻ります。",
                message);
            Assert.False(GenesisGuiNextRecordingCommandPresentation.Resolve(
                viewModel, tabs.SelectedTab, selectedRow).IsRecordEnabled);
            Assert.False(viewModel.CanAllowAutomatic(oldRow.Suppression!));

            tabs.UpdateActiveRecordings([]);
            tabs.UpdateSuppression([]);
            Assert.Same(activeRows, tabs.ActiveRecordings);
            Assert.False(Identity().RequiresRebuild(nextIdentity));
            Assert.Equal(2, replacements);
            Assert.Equal(starts, sink.Starts);
            Assert.Equal(stopAdmissions, sink.AdmissionCalls);
            Assert.Equal(0, sink.LegacyStops);
            Assert.Equal(owners, fixture.OwnerProvider.AttachCount);
            Assert.Equal(opens, fixture.SourceProvider.OpenCount);
            Assert.Equal(passes, evaluations);
        }
        finally
        {
            GenesisChannelListRenderedChannels.Remove(listView);
        }

        GenesisGuiNextNativeRowRebuildIdentity Identity() =>
            GenesisGuiNextNativeRowRebuildIdentity.Create(
                tabs.ActiveRecordings, tabs.SelectedTab, tabs.RecordingPageMode,
                tabs.SuppressionRows);

        bool Restore(nint window, int row)
        {
            restoreCalls++;
            return true;
        }
    }
}

public sealed class GenesisGuiNextRecordingNativeRefreshContractTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ActiveAndSuppressionReferencesIndependentlyControlRebuild(
        bool activeChanged, bool suppressionChanged, bool expected)
    {
        var active = new object();
        var suppression = new object();
        var before = Create(active, suppression);
        var after = Create(
            activeChanged ? new object() : active,
            suppressionChanged ? new object() : suppression);

        Assert.Equal(expected, after.RequiresRebuild(before));
    }

    [Fact]
    public void EqualSuppressionSnapshotsRetainIdentityAndDoNotRebuildOnTimer()
    {
        var tabs = new GenesisGuiNextTabsViewModel(new GenesisFavoriteChannelCollection());
        var row = new GenesisAutomaticRecordingSuppressionRow(
            new ChannelId(Guid.NewGuid()), Guid.NewGuid(),
            GenesisAutomaticRecordingSuppressionState.Committed, "抑止中");
        tabs.UpdateSuppression([row]);
        var suppression = tabs.SuppressionRows;
        var before = Create(tabs.ActiveRecordings, suppression);

        for (var index = 0; index < 3; index++)
        {
            tabs.UpdateActiveRecordings([]);
            tabs.UpdateSuppression([row with { }]);
            Assert.Same(suppression, tabs.SuppressionRows);
            Assert.False(Create(tabs.ActiveRecordings, tabs.SuppressionRows).RequiresRebuild(before));
        }
    }

    [Theory]
    [InlineData((int)GenesisGuiNextTab.Channels, (int)GenesisGuiNextRecordingPageMode.ActiveRecordings)]
    [InlineData((int)GenesisGuiNextTab.New, (int)GenesisGuiNextRecordingPageMode.ActiveRecordings)]
    [InlineData((int)GenesisGuiNextTab.Favorites, (int)GenesisGuiNextRecordingPageMode.ActiveRecordings)]
    [InlineData((int)GenesisGuiNextTab.Playing, (int)GenesisGuiNextRecordingPageMode.ActiveRecordings)]
    [InlineData((int)GenesisGuiNextTab.Recording, (int)GenesisGuiNextRecordingPageMode.Rules)]
    public void UnrelatedModesIgnoreSuppressionIdentity(
        int tabValue, int modeValue)
    {
        var tab = (GenesisGuiNextTab)tabValue;
        var mode = (GenesisGuiNextRecordingPageMode)modeValue;
        var source = new object();
        var before = GenesisGuiNextNativeRowRebuildIdentity.Create(source, tab, mode, new object());
        var after = GenesisGuiNextNativeRowRebuildIdentity.Create(source, tab, mode, new object());

        Assert.False(after.RequiresRebuild(before));
        Assert.Null(GenesisGuiNextAppShellBridge.ResolveRecordingCompletionMessage(
            tab, mode, null, "completion"));
    }

    [Fact]
    public void TabModeAndSourceChangesKeepExistingInvalidation()
    {
        var source = new object();
        var suppression = new object();
        var before = Create(source, suppression);
        Assert.True(GenesisGuiNextNativeRowRebuildIdentity.Create(
            source, GenesisGuiNextTab.Channels, GenesisGuiNextRecordingPageMode.ActiveRecordings,
            suppression).RequiresRebuild(before));
        Assert.True(GenesisGuiNextNativeRowRebuildIdentity.Create(
            source, GenesisGuiNextTab.Recording, GenesisGuiNextRecordingPageMode.Rules,
            suppression).RequiresRebuild(before));
        Assert.True(Create(new object(), suppression).RequiresRebuild(before));
    }

    private static GenesisGuiNextNativeRowRebuildIdentity Create(object source, object suppression) =>
        GenesisGuiNextNativeRowRebuildIdentity.Create(
            source, GenesisGuiNextTab.Recording, GenesisGuiNextRecordingPageMode.ActiveRecordings,
            suppression);
}
