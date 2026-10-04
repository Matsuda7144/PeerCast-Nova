using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using PeerCast.Nova.Network.PortMapping;

namespace PeerCast.Nova.App;

internal static class NovaAcceptanceRelayHarness
{
    private static readonly TimeSpan StartupBudget = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ShutdownBudget = TimeSpan.FromSeconds(10);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    internal static async Task<int> RunAsync(NovaAcceptanceRelayOptions options, TextWriter output,
        CancellationToken cancellationToken, Func<NovaAcceptanceRelayOptions, INovaAcceptanceRelaySession>? factory = null,
        TimeProvider? timeProvider = null)
    {
        var clock = timeProvider ?? TimeProvider.System;
        INovaAcceptanceRelaySession? session = null;
        IPEndPoint? endpoint = null;
        Task<IPEndPoint>? startup = null;
        var exitCode = 0;
        var terminalCode = "DurationElapsed";
        var applicationTasksDrained = false;
        // This deadline is created before startup and remains active throughout execution.
        using var lifetime = new CancellationTokenSource(options.Duration, clock);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        try
        {
            await WriteAsync("ProcessStarted").ConfigureAwait(false);
            session = (factory ?? (static value => new NovaAcceptanceRelaySession(value)))(options);
            startup = session.StartAsync(stop.Token);
            endpoint = await startup.WaitAsync(StartupBudget, clock, stop.Token).ConfigureAwait(false);
            await WriteAsync("ListenerStarted").ConfigureAwait(false);
            while (!stop.IsCancellationRequested)
            {
                using var tick = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                var delay = Task.Delay(TimeSpan.FromSeconds(1), clock, tick.Token);
                var first = await Task.WhenAny(delay, session.Completion).ConfigureAwait(false);
                stop.Token.ThrowIfCancellationRequested();
                if (first == session.Completion)
                {
                    tick.Cancel();
                    await ObserveCancellationAsync(delay).ConfigureAwait(false);
                    await session.Completion.ConfigureAwait(false);
                    terminalCode = "ListenerEndedUnexpectedly";
                    exitCode = 1;
                    break;
                }
                await delay.ConfigureAwait(false);
                await WriteAsync("Status").ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            terminalCode = cancellationToken.IsCancellationRequested ? "CallerCanceled"
                : endpoint is null ? "ListenerStartupTimeout" : "DurationElapsed";
            if (terminalCode == "ListenerStartupTimeout") { exitCode = 1; }
        }
        catch (TimeoutException)
        {
            terminalCode = "ListenerStartupTimeout";
            exitCode = 1;
        }
        catch (SocketException)
        {
            terminalCode = "ListenerBindFailure";
            exitCode = 1;
        }
        catch (Exception exception)
        {
            terminalCode = "HarnessStartupFailure";
            exitCode = 1;
            // Stable type only; arbitrary exception text is not a diagnostic contract.
            await WriteAsync(terminalCode, exception.GetType().Name).ConfigureAwait(false);
        }
        finally
        {
            stop.Cancel();
            if (session is not null)
            {
                var cleanup = "CleanupUnconfirmed";
                var cleanupTask = CleanupAndDrainAsync();
                try
                {
                    var outcome = await cleanupTask.WaitAsync(ShutdownBudget, clock, CancellationToken.None)
                        .ConfigureAwait(false);
                    cleanup = outcome.ToString();
                    // Unconfirmed cleanup can still have an already submitted backend
                    // operation retaining a late-observation task beyond the host budget.
                    applicationTasksDrained = outcome == NatShutdownOutcome.Completed && session.Completion.IsCompleted;
                    if (outcome == NatShutdownOutcome.CleanupUnconfirmed)
                    {
                        exitCode = 2;
                    }
                }
                catch (TimeoutException)
                {
                    _ = cleanupTask.ContinueWith(static task => _ = task.Exception,
                        CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                    terminalCode = "ApplicationShutdownTimeout";
                    exitCode = 2;
                }
                catch (Exception)
                {
                    cleanup = "ApplicationCleanupIncomplete";
                    exitCode = 2;
                }
                await WriteAsync(terminalCode, cleanup).ConfigureAwait(false);
            }
            await WriteAsync("ProcessCompleted", exitCode.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .ConfigureAwait(false);
        }
        return exitCode;

        async Task<NatShutdownOutcome> CleanupAndDrainAsync()
        {
            var outcome = await session!.CleanupAsync().ConfigureAwait(false);
            // Include delayed startup in the same outer shutdown budget. Observe its
            // terminal exception without confusing an already reported bind failure with cleanup.
            if (startup is not null)
            {
                try { await startup.ConfigureAwait(false); }
                catch (Exception) { }
            }
            return outcome;
        }

        async Task WriteAsync(string code, string? summary = null)
        {
            var capture = session?.ReadMappingState();
            var display = capture is null ? null : NatPortMappingStatusProjection.Create(capture);
            var status = new
            {
                Schema = "Nova.HeadlessRelayAcceptance.v1",
                options.RunId,
                ProcessId = Environment.ProcessId,
                Timestamp = clock.GetUtcNow(),
                Code = code,
                Summary = summary ?? code,
                ProcessStarted = true,
                ListenerStarted = endpoint is not null,
                ListenerState = session?.ListenerState ?? "NotCreated",
                Protocol = "TCP",
                BoundInternalAddress = endpoint?.Address.ToString(),
                BoundPort = endpoint?.Port,
                PublicWanEligible = endpoint is not null && !IPAddress.IsLoopback(endpoint.Address),
                EffectiveAutoPortMapping = options.AutoPortMapping,
                PortMapping = display,
                // Preserve outstanding observations even when they belong to a retired network.
                MappingObservations = capture?.Mappings,
                MappingDiagnosticSummary = display?.Diagnostics.LastOrDefault(static item => !item.Historical)?.Code,
                OperationDispatchCount = capture?.Diagnostics.Count(static item => item.Event == "OperationDispatched") ?? 0,
                OwnedListenerTaskCompleted = session?.Completion.IsCompleted ?? true,
                ApplicationTasksDrained = applicationTasksDrained,
            };
            await output.WriteLineAsync(JsonSerializer.Serialize(status, JsonOptions)).ConfigureAwait(false);
            await output.FlushAsync().ConfigureAwait(false);
        }
    }

    private static async Task ObserveCancellationAsync(Task task)
    {
        try { await task.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }
}
