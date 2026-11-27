using PeerCast.Nova.Core.Recording;

namespace PeerCast.Nova.App.Tests;

public sealed class GenesisRecordingRuleQuickMutationNativeContractTests
{
    [Fact]
    public async Task MutationControllerEnforcesSingleReservationAndPreservesPersistedOrder()
    {
        Assert.Contains("同じ優先度", GenesisRecordingRuleMutationController.PersistedOrderHelp, StringComparison.Ordinal);
        Assert.Contains("次回", GenesisRecordingRuleMutationController.PersistedOrderHelp, StringComparison.Ordinal);

        var directory = Path.Combine(Path.GetTempPath(), "NovaRuleMutationContractTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "rules.json");
            var first = new RecordingRule(Guid.NewGuid(), "First", RecordingRuleAction.Record, new(name: new("a")), 1, true);
            var second = new RecordingRule(Guid.NewGuid(), "Second", RecordingRuleAction.Record, new(name: new("b")), 10, false);
            var third = new RecordingRule(Guid.NewGuid(), "Third", RecordingRuleAction.Record, new(name: new("c")), -5, false);
            await File.WriteAllTextAsync(path, RecordingRuleJsonCodec.Serialize(new([first, second, third])));

            await using var repository = new GenesisRecordingRuleRepository(path, RecordingRuleFileStore.SaveAsync);
            GenesisRecordingRuleRepositorySnapshot? publishedSnapshot = null;
            Guid? publishedSelection = null;
            await using var controller = new GenesisRecordingRuleMutationController(
                repository,
                (snapshot, selection) =>
                {
                    publishedSnapshot = snapshot;
                    publishedSelection = selection;
                });

            var initial = await repository.GetSnapshotAsync();

            // 予約の排他制御が機能することを確認
            using (var reservation = controller.TryReserve())
            {
                Assert.NotNull(reservation);
                Assert.False(controller.IsAvailable);
                Assert.Null(controller.TryReserve());

                var blocked = await controller.ExecuteAsync(
                    initial,
                    first.Id,
                    GenesisRecordingRuleQuickAction.MoveDown,
                    (_, _) => ValueTask.FromResult(true));
                Assert.Null(blocked.Status);
            }

            Assert.True(controller.IsAvailable);

            // 優先度で勝手に再ソートせず、移動順だけが維持されて保存されること
            var moved = await controller.ExecuteAsync(
                initial,
                first.Id,
                GenesisRecordingRuleQuickAction.MoveDown,
                (_, _) => ValueTask.FromResult(true));
            Assert.True(moved.Applied);
            Assert.NotNull(publishedSnapshot);
            Assert.Equal(first.Id, publishedSelection);
            Assert.Equal([second.Id, first.Id, third.Id], publishedSnapshot!.Rules!.Rules.Select(rule => rule.Id));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
