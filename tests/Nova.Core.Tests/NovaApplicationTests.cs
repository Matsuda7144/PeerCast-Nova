using PeerCast.Nova.Foundation.Logging;

namespace PeerCast.Nova.Core.Tests;

public sealed class NovaApplicationTests
{
    [Fact]
    public async Task StopAsync_TimesOutOneModuleAndContinuesInReverseOrder()
    {
        var hangingCompletion = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var stoppedModule = new TestModule("First");
        var hangingModule = new TestModule(
            "Second",
            _ => new ValueTask(hangingCompletion.Task));

        var logger = new CollectingLogger();
        var options = new NovaApplicationOptions
        {
            ModuleStopTimeout = TimeSpan.FromMilliseconds(50),
        };

        await using var application = new NovaApplication(
            [stoppedModule, hangingModule],
            logger,
            options);

        await application.StartAsync();
        await application.StopAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(1, hangingModule.StopCount);
        Assert.Equal(1, stoppedModule.StopCount);
        Assert.Contains(
            logger.Entries,
            entry => entry.Level == LogLevel.Warning &&
                entry.Message.Contains("did not stop", StringComparison.Ordinal));

        hangingCompletion.TrySetResult();
    }

    [Fact]
    public async Task StopAsync_CallerCancellationStillAttemptsEveryModule()
    {
        var stopEntered = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        var firstModule = new TestModule("First");
        var secondModule = new TestModule(
            "Second",
            async cancellationToken =>
            {
                stopEntered.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            });

        await using var application = new NovaApplication(
            [firstModule, secondModule],
            new CollectingLogger());

        await application.StartAsync();

        using var cancellationSource = new CancellationTokenSource();
        var stopTask = application.StopAsync(cancellationSource.Token);

        await stopEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await cancellationSource.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => stopTask);

        Assert.Equal(1, firstModule.StopCount);
        Assert.Equal(1, secondModule.StopCount);
        Assert.False(application.IsRunning);
    }

    [Fact]
    public void Constructor_RejectsNonPositiveModuleStopTimeout()
    {
        var options = new NovaApplicationOptions
        {
            ModuleStopTimeout = TimeSpan.Zero,
        };

        Assert.Throws<InvalidOperationException>(
            () => new NovaApplication([], new CollectingLogger(), options));
    }

    private sealed class TestModule : INovaModule
    {
        private readonly Func<CancellationToken, ValueTask> _stop;

        public TestModule(
            string name,
            Func<CancellationToken, ValueTask>? stop = null)
        {
            Name = name;
            _stop = stop ?? (_ => ValueTask.CompletedTask);
        }

        public string Name { get; }

        public int StopCount { get; private set; }

        public ValueTask StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask StopAsync(CancellationToken cancellationToken)
        {
            StopCount++;
            return _stop(cancellationToken);
        }
    }

    private sealed class CollectingLogger : INovaLogger
    {
        public List<LogEntry> Entries { get; } = [];

        public void Log(
            LogLevel level,
            string message,
            Exception? exception = null)
        {
            Entries.Add(new LogEntry(level, message, exception));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        Exception? Exception);
}
