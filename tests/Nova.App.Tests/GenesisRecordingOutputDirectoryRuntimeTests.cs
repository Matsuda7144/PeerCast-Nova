using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed partial class GenesisAutomaticRecordingEvaluationControllerTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OutputDirectorySaveOrResetCannotMutateActiveManualOrAutomaticSession(bool automatic, bool reset)
    {
        var root = Path.Combine(Path.GetTempPath(), "Nova-output-active-tests", Guid.NewGuid().ToString("N"));
        var original = Path.Combine(root, "original");
        var changed = Path.Combine(root, "changed");
        var settings = new GenesisRecordingOutputDirectorySettings(new(Path.Combine(root, "settings.json")), original);
        try
        {
            if (reset) Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Saved, settings.Save(changed));
            var rule = RecordRule();
            await using var fixture = new Fixture(new([rule]), outputDirectory: settings.CurrentRuntimeDirectory);
            await using var manual = new GenesisManualRecordingSessionController(fixture.OwnerProvider, fixture.Runtime);
            var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.SourceProvider.StreamFactory = () => new DirectoryTestStream(read);
            if (automatic)
                Assert.Equal(1, (await fixture.Controller.ObserveAsync([Channel()])).StartedCount);
            else
                Assert.Equal(GenesisManualRecordingCommandKind.Started, (await manual.StartAsync(Channel())).Kind);
            await read.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var session = fixture.Runtime.GetState(ChannelId()).Session;
            var rules = fixture.RuleSetProvider.RuleSet;
            var suppression = fixture.Controller.SuppressionRows.ToArray();
            var files = Directory.GetFiles(original);
            Assert.NotEmpty(files);
            Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Saved,
                reset ? settings.ResetToDefault() : settings.Save(changed));
            Assert.Same(session, fixture.Runtime.GetState(ChannelId()).Session);
            Assert.Same(rules, fixture.RuleSetProvider.RuleSet);
            Assert.Equal(suppression, fixture.Controller.SuppressionRows);
            Assert.Equal(1, fixture.SourceProvider.OpenCount);
            Assert.Equal(1, fixture.OwnerProvider.AttachCount);
            Assert.Equal(original, settings.CurrentRuntimeDirectory);
            Assert.Equal(reset ? original : changed, settings.Presentation.EffectiveDirectory);
            Assert.Equal(files, Directory.GetFiles(original));
            Assert.False(Directory.Exists(changed));
            Assert.Single(await fixture.Runtime.GetActiveRecordingSessionsAsync());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ManualAndAutomaticWritersUseSameResolvedDestination()
    {
        var root = Path.Combine(Path.GetTempPath(), "Nova-output-shared-tests", Guid.NewGuid().ToString("N"));
        var destination = Path.Combine(root, "custom");
        var store = new NovaSettingsStore(Path.Combine(root, "settings.json"));
        var settings = new GenesisRecordingOutputDirectorySettings(store, Path.Combine(root, "default"));
        try
        {
            Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Saved, settings.Save(destination));
            var nextStart = new GenesisRecordingOutputDirectorySettings(store, Path.Combine(root, "default"));
            await using var fixture = new Fixture(new([RecordRule()]), outputDirectory: nextStart.CurrentRuntimeDirectory);
            await using var manual = new GenesisManualRecordingSessionController(fixture.OwnerProvider, fixture.Runtime);
            var read = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.SourceProvider.StreamFactory = () => new DirectoryTestStream(read);
            Assert.Equal(GenesisManualRecordingCommandKind.Started, (await manual.StartAsync(Channel())).Kind);
            await read.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotEmpty(Directory.GetFiles(destination));
            _ = await manual.StopAsync(Channel());

            read = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.Equal(1, (await fixture.Controller.ObserveAsync([Channel()])).StartedCount);
            await read.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(RecordingSessionOrigin.AutomaticRule, fixture.Runtime.GetState(ChannelId()).Session!.Origin);
            Assert.NotEmpty(Directory.GetFiles(destination, "*.partial"));
            Assert.False(Directory.Exists(Path.Combine(root, "default")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task SettingsWritesDoNotClearCommittedSuppressionOrChangeRules()
    {
        var root = Path.Combine(Path.GetTempPath(), "Nova-output-suppression-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var settings = new GenesisRecordingOutputDirectorySettings(new(Path.Combine(root, "settings.json")),
                Path.Combine(root, "original"));
            await using var fixture = new Fixture(new([RecordRule()]), outputDirectory: settings.CurrentRuntimeDirectory);
            await fixture.Controller.ObserveAsync([Channel()]);
            await StopAndCommit(fixture);
            var suppression = fixture.Controller.SuppressionRows.ToArray();
            var rules = fixture.RuleSetProvider.RuleSet;
            Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Saved, settings.Save(Path.Combine(root, "next")));
            Assert.Equal(GenesisRecordingOutputDirectorySaveResult.Saved, settings.ResetToDefault());
            Assert.Equal(suppression, fixture.Controller.SuppressionRows);
            Assert.Same(rules, fixture.RuleSetProvider.RuleSet);
            Assert.Equal(0, (await fixture.Controller.ObserveAsync([Channel()])).StartedCount);
            Assert.Equal(1, fixture.SourceProvider.OpenCount);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class DirectoryTestStream(TaskCompletionSource read) : BlockingFlvStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var result = base.ReadAsync(buffer, cancellationToken);
            read.TrySetResult();
            return result;
        }
    }
}
