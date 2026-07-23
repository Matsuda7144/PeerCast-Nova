using PeerCast.Nova.Core.Channels;
using PeerCast.Nova.Network.Pcp;

namespace PeerCast.Nova.Playback;

internal sealed class PlaybackApprovedExternalRuntimeWindowsLifecycleOperations :
    IPlaybackApprovedExternalRuntimeWindowsLifecycleOperations
{
    private readonly object _sync = new();
    private readonly ChannelId _channelId;
    private readonly ChannelSessionId _channelSessionId;
    private readonly string _runtimeDirectory;
    private readonly PlaybackFfmpegExternalRuntimeManifest _runtimeManifest;
    private readonly PlaybackDecoderBackendPolicy _policy;
    private readonly IPlaybackWindowsOutputDispatcher _dispatcher;
    private readonly PlaybackApprovedExternalRuntimeWindowsLifecycleOptions _options;
    private readonly IPlaybackMonotonicClock _clock;

    private PlaybackWindowsOutputAdapterProbe? _probe;
    private PlaybackWindowsOutputActivationBoundary? _activation;
    private PlaybackWindowsOutputResourceBindingBoundary? _binding;
    private PlaybackWindowsOutputStreamInitializationBoundary? _streams;
    private PlaybackWindowsOutputBufferProcessingBoundary? _buffers;
    private PlaybackProductionPcpFlvCompositionBoundary? _production;
    private PlaybackWindowsOutputProbeResult _probeResult =
        PlaybackWindowsOutputProbeResult.Failure;
    private PlaybackWindowsOutputActivationResult _activationResult =
        PlaybackWindowsOutputActivationResult.Failure;
    private int _outputDisposed;
    private int _isDisposed;

    internal PlaybackApprovedExternalRuntimeWindowsLifecycleOperations(
        ChannelId channelId,
        ChannelSessionId channelSessionId,
        string runtimeDirectory,
        PlaybackFfmpegExternalRuntimeManifest runtimeManifest,
        PlaybackDecoderBackendPolicy policy,
        IPlaybackWindowsOutputDispatcher dispatcher,
        PlaybackApprovedExternalRuntimeWindowsLifecycleOptions options,
        IPlaybackMonotonicClock clock)
    {
        _channelId = channelId;
        _channelSessionId = channelSessionId;
        _runtimeDirectory = runtimeDirectory;
        _runtimeManifest = runtimeManifest;
        _policy = policy with { };
        _dispatcher = dispatcher;
        _options = options;
        _clock = clock;
    }

    public PlaybackApprovedExternalRuntimeWindowsOperationsSnapshot Snapshot
    {
        get
        {
            lock (_sync)
            {
                PlaybackProductionPcpFlvCompositionSnapshot? production = null;
                try
                {
                    production = _production?.Snapshot;
                }
                catch (ObjectDisposedException)
                {
                    production = null;
                }

                return new(
                    _probeResult,
                    _activationResult,
                    _binding?.Snapshot ?? default,
                    _streams?.Snapshot ?? default,
                    _buffers?.Snapshot ?? default,
                    production);
            }
        }
    }

    public PlaybackWindowsOutputProbeReport Probe(
        PlaybackDecodedVideoPixelFormat videoFormat,
        PlaybackDecodedAudioSampleFormat audioFormat)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
        PlaybackWindowsOutputAdapterProbe probe;
        lock (_sync)
        {
            probe = _probe ??= new PlaybackWindowsOutputAdapterProbe();
        }

        var report = probe.Probe(videoFormat, audioFormat);
        lock (_sync)
        {
            _probeResult = report.Result;
        }

        return report;
    }

    public async ValueTask<PlaybackWindowsOutputActivationResult> ActivateAsync(
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _isDisposed) != 0)
        {
            return PlaybackWindowsOutputActivationResult.Disposed;
        }

        PlaybackWindowsOutputDispatcherInvocation<PlaybackWindowsOutputActivationResult>
            invocation;
        try
        {
            invocation = await _dispatcher.InvokeAsync(
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    if (!_dispatcher.HasAccess)
                    {
                        return ValueTask.FromResult(
                            PlaybackWindowsOutputActivationResult.InvalidEnvironment);
                    }

                    var activation = new PlaybackWindowsOutputActivationBoundary(
                        _options.AudioNativeStageReporter);
                    var result = activation.Activate();
                    if (result == PlaybackWindowsOutputActivationResult.Activated)
                    {
                        lock (_sync)
                        {
                            _activation = activation;
                        }
                    }
                    else
                    {
                        activation.Dispose();
                    }

                    return ValueTask.FromResult(result);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            invocation = new(
                PlaybackWindowsOutputDispatcherInvocationResult.Faulted,
                PlaybackWindowsOutputActivationResult.Failure);
        }

        var result = invocation.Result switch
        {
            PlaybackWindowsOutputDispatcherInvocationResult.Completed => invocation.Value,
            PlaybackWindowsOutputDispatcherInvocationResult.Cancelled =>
                cancellationToken.IsCancellationRequested
                    ? throw new OperationCanceledException(cancellationToken)
                    : PlaybackWindowsOutputActivationResult.Failure,
            PlaybackWindowsOutputDispatcherInvocationResult.Shutdown =>
                PlaybackWindowsOutputActivationResult.Disposed,
            _ => PlaybackWindowsOutputActivationResult.Failure,
        };

        lock (_sync)
        {
            _activationResult = result;
            if (result == PlaybackWindowsOutputActivationResult.Activated)
            {
                var activation = _activation ??
                    throw new InvalidOperationException("Activation completed without an owner.");
                _binding = new PlaybackWindowsOutputResourceBindingBoundary(activation);
                _streams = new PlaybackWindowsOutputStreamInitializationBoundary(
                    activation,
                    _binding);
                _buffers = new PlaybackWindowsOutputBufferProcessingBoundary(
                    activation,
                    _binding,
                    _streams,
                    _options.AudioNativeStageReporter);
            }
        }

        return result;
    }

    public void CreateProduction()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _isDisposed) != 0, this);
        PlaybackWindowsOutputBufferProcessingBoundary buffers;
        lock (_sync)
        {
            if (_production is not null)
            {
                throw new InvalidOperationException("The production composition already exists.");
            }

            buffers = _buffers ??
                throw new InvalidOperationException("Windows output activation is required.");
        }

        var dispatcherOperations =
            new PlaybackDispatcherBackedProductionGenerationFactoryOperations(
                _clock,
                _dispatcher);
        var factory = new PlaybackFfmpegExternalRuntimeGenerationFactory(
            _policy,
            _runtimeManifest,
            _options.Generation,
            dispatcherOperations);
        var creator = new PlaybackFfmpegExternalRuntimeGenerationCreator(factory);
        PlaybackProductionPcpFlvCompositionBoundary production;
        try
        {
            production = new(
                _channelId,
                _channelSessionId,
                _runtimeDirectory,
                buffers,
                creator,
                _options.InitialPacketSequence,
                _options.Production,
                _options.Parser);
        }
        catch
        {
            creator.Dispose();
            throw;
        }

        lock (_sync)
        {
            _production = production;
        }
    }

    public async ValueTask<PlaybackProductionPcpFlvCompositionResult> ConsumeAsync(
        PcpChannelPacket packet,
        CancellationToken cancellationToken)
    {
        var production = GetProduction();
        await production.ConsumeAsync(packet, cancellationToken).ConfigureAwait(false);
        return production.Snapshot.Result;
    }

    public async ValueTask<PlaybackProductionPcpFlvCompositionResult> CompleteInputAsync(
        CancellationToken cancellationToken)
    {
        var production = GetProduction();
        await production.CompleteInputAsync(cancellationToken).ConfigureAwait(false);
        return production.Snapshot.Result;
    }

    public ValueTask<PlaybackProductionPcpFlvCompositionResult> ReceiveDecodedAsync(
        PlaybackBoundedDemuxMediaKind mediaKind,
        CancellationToken cancellationToken) =>
        GetProduction().ReceiveDecodedAsync(mediaKind, cancellationToken);

    public ValueTask<PlaybackProductionPcpFlvCompositionResult> DrainDecodedAsync(
        PlaybackBoundedDemuxMediaKind mediaKind,
        CancellationToken cancellationToken) =>
        GetProduction().DrainDecodedAsync(mediaKind, cancellationToken);

    public ValueTask<PlaybackProductionPcpFlvCompositionResult> DeliverOneAsync(
        CancellationToken cancellationToken) =>
        GetProduction().DeliverOneAsync(cancellationToken);

    public ValueTask<PlaybackProductionPcpFlvCompositionResult> ResetAsync(
        CancellationToken cancellationToken) =>
        GetProduction().ResetAsync(cancellationToken);

    public async ValueTask<PlaybackApprovedExternalRuntimeOutputConfigurationResult>
        ConfigureOutputAsync(
            PlaybackApprovedExternalRuntimeOutputConfiguration configuration,
            bool replacement,
            CancellationToken cancellationToken)
    {
        var binding = GetBinding();
        var streams = GetStreams();
        var buffers = GetBuffers();
        PlaybackVideoRendererConfiguration videoBinding;
        PlaybackAudioDeviceConfiguration audioBinding;
        PlaybackWindowsVideoStreamConfiguration videoStream;
        PlaybackWindowsAudioStreamConfiguration audioStream;
        try
        {
            videoBinding = new(
                configuration.VideoFormat,
                configuration.Width,
                configuration.Height,
                checked(configuration.Width * 4),
                configuration.Generation);
            audioBinding = new(
                configuration.AudioFormat,
                configuration.SampleRate,
                configuration.ChannelCount,
                configuration.Generation);
            videoStream = new(
                configuration.WindowToken,
                configuration.Width,
                configuration.Height,
                configuration.VideoFormat,
                configuration.Generation);
            audioStream = new(
                configuration.AudioFormat,
                configuration.SampleRate,
                configuration.ChannelCount,
                configuration.AudioBufferDuration,
                configuration.Generation);
        }
        catch
        {
            return PlaybackApprovedExternalRuntimeOutputConfigurationResult.Rejected;
        }

        PlaybackWindowsOutputDispatcherInvocation<
            PlaybackApprovedExternalRuntimeOutputConfigurationResult> invocation;
        try
        {
            invocation = await _dispatcher.InvokeAsync(
                async token =>
                {
                    if (!_dispatcher.HasAccess)
                    {
                        return PlaybackApprovedExternalRuntimeOutputConfigurationResult.Faulted;
                    }

                    if (replacement)
                    {
                        var audioBuffer = await buffers.ResetAudioAsync(
                            configuration.Generation,
                            token).ConfigureAwait(false);
                        var mapped = Map(audioBuffer);
                        if (mapped != PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured)
                        {
                            return mapped;
                        }

                        var videoBuffer = await buffers.ResetVideoAsync(
                            configuration.Generation,
                            token).ConfigureAwait(false);
                        mapped = Map(videoBuffer);
                        if (mapped != PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured)
                        {
                            return mapped;
                        }

                        var audioReset = await streams.ResetAudioAsync(
                            configuration.Generation,
                            token).ConfigureAwait(false);
                        mapped = Map(audioReset);
                        if (mapped != PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured)
                        {
                            return mapped;
                        }

                        var videoReset = await streams.ResetVideoAsync(
                            configuration.Generation,
                            token).ConfigureAwait(false);
                        mapped = Map(videoReset);
                        if (mapped != PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured)
                        {
                            return mapped;
                        }

                        var bindingReset = await binding.ResetAsync(
                            configuration.Generation,
                            token).ConfigureAwait(false);
                        mapped = Map(bindingReset);
                        if (mapped != PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured)
                        {
                            return mapped;
                        }
                    }

                    var bindingResult = await binding.ConfigureAsync(
                        videoBinding,
                        audioBinding,
                        token).ConfigureAwait(false);
                    var bindingMapped = Map(bindingResult);
                    if (bindingMapped !=
                        PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured)
                    {
                        return bindingMapped;
                    }

                    var streamResult = await streams.InitializeAsync(
                        videoStream,
                        audioStream,
                        token).ConfigureAwait(false);
                    return Map(streamResult);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PlaybackApprovedExternalRuntimeOutputConfigurationResult.Cancelled;
        }
        catch
        {
            return PlaybackApprovedExternalRuntimeOutputConfigurationResult.Faulted;
        }

        return MapInvocation(invocation);
    }

    public async ValueTask<PlaybackApprovedExternalRuntimeOutputConfigurationResult>
        ResizeVideoAsync(
            long windowToken,
            int width,
            int height,
            PlaybackDecodedVideoPixelFormat format,
            long generation,
            CancellationToken cancellationToken)
    {
        var streams = GetStreams();
        PlaybackWindowsOutputDispatcherInvocation<
            PlaybackApprovedExternalRuntimeOutputConfigurationResult> invocation;
        try
        {
            invocation = await _dispatcher.InvokeAsync(
                async token =>
                {
                    if (!_dispatcher.HasAccess)
                    {
                        return PlaybackApprovedExternalRuntimeOutputConfigurationResult.Faulted;
                    }

                    var result = await streams.ResizeVideoAsync(
                        new(windowToken, width, height, format, generation),
                        token).ConfigureAwait(false);
                    return Map(result);
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return PlaybackApprovedExternalRuntimeOutputConfigurationResult.Cancelled;
        }
        catch
        {
            return PlaybackApprovedExternalRuntimeOutputConfigurationResult.Faulted;
        }

        return MapInvocation(invocation);
    }

    public async ValueTask StopProductionAsync()
    {
        PlaybackProductionPcpFlvCompositionBoundary? production;
        lock (_sync)
        {
            production = _production;
            _production = null;
        }

        if (production is null)
        {
            return;
        }

        try
        {
            await production.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await production.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask<PlaybackApprovedExternalRuntimeOutputConfigurationResult>
        DisposeOutputAsync()
    {
        if (Interlocked.Exchange(ref _outputDisposed, 1) != 0)
        {
            return PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured;
        }

        PlaybackWindowsOutputActivationBoundary? activation;
        PlaybackWindowsOutputResourceBindingBoundary? binding;
        PlaybackWindowsOutputStreamInitializationBoundary? streams;
        PlaybackWindowsOutputBufferProcessingBoundary? buffers;
        PlaybackWindowsOutputAdapterProbe? probe;
        lock (_sync)
        {
            activation = _activation;
            binding = _binding;
            streams = _streams;
            buffers = _buffers;
            probe = _probe;
        }

        PlaybackApprovedExternalRuntimeOutputConfigurationResult result;
        try
        {
            var invocation = await _dispatcher.InvokeAsync(
                token =>
                {
                    token.ThrowIfCancellationRequested();
                    if (!_dispatcher.HasAccess)
                    {
                        return ValueTask.FromResult(
                            PlaybackApprovedExternalRuntimeOutputConfigurationResult.Faulted);
                    }

                    try
                    {
                        buffers?.Dispose();
                    }
                    finally
                    {
                        try
                        {
                            streams?.Dispose();
                        }
                        finally
                        {
                            try
                            {
                                binding?.Dispose();
                            }
                            finally
                            {
                                activation?.Dispose();
                            }
                        }
                    }

                    return ValueTask.FromResult(
                        PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured);
                }).ConfigureAwait(false);
            result = MapInvocation(invocation);
        }
        catch
        {
            result = PlaybackApprovedExternalRuntimeOutputConfigurationResult.Faulted;
        }
        finally
        {
            probe?.Dispose();
            lock (_sync)
            {
                _probe = null;
            }
        }

        return result;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
        {
            return;
        }

        try
        {
            await StopProductionAsync().ConfigureAwait(false);
        }
        finally
        {
            await DisposeOutputAsync().ConfigureAwait(false);
        }
    }

    private PlaybackProductionPcpFlvCompositionBoundary GetProduction()
    {
        lock (_sync)
        {
            return _production ??
                throw new InvalidOperationException("The production composition is unavailable.");
        }
    }

    private PlaybackWindowsOutputResourceBindingBoundary GetBinding()
    {
        lock (_sync)
        {
            return _binding ??
                throw new InvalidOperationException("The output binding is unavailable.");
        }
    }

    private PlaybackWindowsOutputStreamInitializationBoundary GetStreams()
    {
        lock (_sync)
        {
            return _streams ??
                throw new InvalidOperationException("The output streams are unavailable.");
        }
    }

    private PlaybackWindowsOutputBufferProcessingBoundary GetBuffers()
    {
        lock (_sync)
        {
            return _buffers ??
                throw new InvalidOperationException("The output buffers are unavailable.");
        }
    }

    private static PlaybackApprovedExternalRuntimeOutputConfigurationResult MapInvocation<T>(
        PlaybackWindowsOutputDispatcherInvocation<T> invocation)
        where T : struct, Enum
    {
        if (typeof(T) != typeof(PlaybackApprovedExternalRuntimeOutputConfigurationResult))
        {
            return PlaybackApprovedExternalRuntimeOutputConfigurationResult.Faulted;
        }

        return invocation.Result switch
        {
            PlaybackWindowsOutputDispatcherInvocationResult.Completed =>
                (PlaybackApprovedExternalRuntimeOutputConfigurationResult)(object)invocation.Value,
            PlaybackWindowsOutputDispatcherInvocationResult.Cancelled =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.Cancelled,
            PlaybackWindowsOutputDispatcherInvocationResult.Shutdown =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.DispatcherShutdown,
            _ => PlaybackApprovedExternalRuntimeOutputConfigurationResult.Faulted,
        };
    }

    private static PlaybackApprovedExternalRuntimeOutputConfigurationResult Map(
        PlaybackWindowsOutputBindingResult result) => result switch
        {
            PlaybackWindowsOutputBindingResult.Bound =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured,
            PlaybackWindowsOutputBindingResult.DeviceLost =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.DeviceLost,
            PlaybackWindowsOutputBindingResult.Cancelled =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.Cancelled,
            PlaybackWindowsOutputBindingResult.Disposed =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.DispatcherShutdown,
            PlaybackWindowsOutputBindingResult.ActivationRequired or
            PlaybackWindowsOutputBindingResult.PlatformUnsupported or
            PlaybackWindowsOutputBindingResult.FormatUnsupported or
            PlaybackWindowsOutputBindingResult.AlreadyBound or
            PlaybackWindowsOutputBindingResult.InvalidEnvironment =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.Rejected,
            _ => PlaybackApprovedExternalRuntimeOutputConfigurationResult.Faulted,
        };

    private static PlaybackApprovedExternalRuntimeOutputConfigurationResult Map(
        PlaybackWindowsOutputBindingResetResult result) => result switch
        {
            PlaybackWindowsOutputBindingResetResult.Reset =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured,
            PlaybackWindowsOutputBindingResetResult.DeviceLost =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.DeviceLost,
            PlaybackWindowsOutputBindingResetResult.Cancelled =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.Cancelled,
            PlaybackWindowsOutputBindingResetResult.Disposed =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.DispatcherShutdown,
            PlaybackWindowsOutputBindingResetResult.NotBound =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.Rejected,
            _ => PlaybackApprovedExternalRuntimeOutputConfigurationResult.Faulted,
        };

    private static PlaybackApprovedExternalRuntimeOutputConfigurationResult Map(
        PlaybackWindowsOutputStreamInitializationResult result) => result switch
        {
            PlaybackWindowsOutputStreamInitializationResult.Initialized =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured,
            PlaybackWindowsOutputStreamInitializationResult.DeviceLost =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.DeviceLost,
            PlaybackWindowsOutputStreamInitializationResult.Cancelled =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.Cancelled,
            PlaybackWindowsOutputStreamInitializationResult.Disposed =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.DispatcherShutdown,
            PlaybackWindowsOutputStreamInitializationResult.ActivationRequired or
            PlaybackWindowsOutputStreamInitializationResult.BindingRequired or
            PlaybackWindowsOutputStreamInitializationResult.PlatformUnsupported or
            PlaybackWindowsOutputStreamInitializationResult.InvalidConfiguration or
            PlaybackWindowsOutputStreamInitializationResult.FormatUnsupported or
            PlaybackWindowsOutputStreamInitializationResult.AlreadyInitialized or
            PlaybackWindowsOutputStreamInitializationResult.ResizeRequired or
            PlaybackWindowsOutputStreamInitializationResult.InvalidEnvironment =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.Rejected,
            _ => PlaybackApprovedExternalRuntimeOutputConfigurationResult.Faulted,
        };

    private static PlaybackApprovedExternalRuntimeOutputConfigurationResult Map(
        PlaybackWindowsOutputStreamResetResult result) => result switch
        {
            PlaybackWindowsOutputStreamResetResult.Reset =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured,
            PlaybackWindowsOutputStreamResetResult.DeviceLost =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.DeviceLost,
            PlaybackWindowsOutputStreamResetResult.Cancelled =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.Cancelled,
            PlaybackWindowsOutputStreamResetResult.Disposed =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.DispatcherShutdown,
            PlaybackWindowsOutputStreamResetResult.NotInitialized =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.Rejected,
            _ => PlaybackApprovedExternalRuntimeOutputConfigurationResult.Faulted,
        };

    private static PlaybackApprovedExternalRuntimeOutputConfigurationResult Map(
        PlaybackWindowsOutputBufferResetResult result) => result switch
        {
            PlaybackWindowsOutputBufferResetResult.Reset =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.Configured,
            PlaybackWindowsOutputBufferResetResult.DeviceLost =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.DeviceLost,
            PlaybackWindowsOutputBufferResetResult.Cancelled =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.Cancelled,
            PlaybackWindowsOutputBufferResetResult.Disposed =>
                PlaybackApprovedExternalRuntimeOutputConfigurationResult.DispatcherShutdown,
            _ => PlaybackApprovedExternalRuntimeOutputConfigurationResult.Faulted,
        };
}
