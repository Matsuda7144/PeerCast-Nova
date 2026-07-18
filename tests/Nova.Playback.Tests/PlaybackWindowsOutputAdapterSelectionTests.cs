namespace PeerCast.Nova.Playback.Tests;

public sealed class PlaybackWindowsOutputAdapterSelectionTests
{
    [Fact]
    public void DefaultSelectionIsDirect3D11AndWasapiSharedEventDriven()
    {
        var selection = PlaybackWindowsOutputAdapterSelection.Windows11Default;

        Assert.Equal(PlaybackWindowsVideoAdapter.Direct3D11, selection.Video);
        Assert.Equal(PlaybackWindowsAudioAdapter.WasapiSharedEventDriven, selection.Audio);
    }

    [Fact]
    public void SystemProbeBranchesWithoutAccessingNativeDevices()
    {
        using var probe = new PlaybackWindowsOutputAdapterProbe();

        var report = probe.Probe(
            PlaybackDecodedVideoPixelFormat.Bgra32,
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved);

        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22_000))
        {
            Assert.Contains(
                report.Result,
                new[]
                {
                    PlaybackWindowsOutputProbeResult.Available,
                    PlaybackWindowsOutputProbeResult.ApiUnavailable,
                });
        }
        else
        {
            Assert.Equal(
                PlaybackWindowsOutputProbeResult.PlatformUnsupported,
                report.Result);
        }
    }

    [Fact]
    public void WindowsVersionAloneDoesNotProduceAvailable()
    {
        var inspector = new RecordingInspector { MissingLibrary = "d3d11.dll" };
        var source = new SystemPlaybackWindowsOutputAvailabilitySource(
            inspector,
            static () => true);
        using var probe = new PlaybackWindowsOutputAdapterProbe(source);

        var report = probe.Probe(
            PlaybackDecodedVideoPixelFormat.Bgra32,
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved);

        Assert.Equal(PlaybackWindowsOutputProbeResult.ApiUnavailable, report.Result);
        Assert.Empty(inspector.OpenHandles);
    }

    [Theory]
    [InlineData("d3d11.dll", null)]
    [InlineData(null, "CreateDXGIFactory1")]
    public void MissingVideoLibraryOrExportIsApiUnavailable(
        string? missingLibrary,
        string? missingExport)
    {
        var inspector = new RecordingInspector
        {
            MissingLibrary = missingLibrary,
            MissingExport = missingExport,
        };
        using var probe = CreateSystemProbe(inspector, true);

        Assert.Equal(PlaybackWindowsOutputProbeResult.ApiUnavailable,
            Probe(probe).Result);
        Assert.Empty(inspector.OpenHandles);
    }

    [Theory]
    [InlineData("kernel32.dll", null)]
    [InlineData(null, "CoCreateInstance")]
    public void MissingAudioLibraryOrExportIsApiUnavailable(
        string? missingLibrary,
        string? missingExport)
    {
        var inspector = new RecordingInspector
        {
            MissingLibrary = missingLibrary,
            MissingExport = missingExport,
        };
        using var probe = CreateSystemProbe(inspector, true);

        Assert.Equal(PlaybackWindowsOutputProbeResult.ApiUnavailable,
            Probe(probe).Result);
        Assert.Empty(inspector.OpenHandles);
    }

    [Fact]
    public void AllRequiredLibrariesAndExportsProduceAvailableAndReleaseHandles()
    {
        var inspector = new RecordingInspector();
        using var probe = CreateSystemProbe(inspector, true);

        Assert.Equal(PlaybackWindowsOutputProbeResult.Available, Probe(probe).Result);
        Assert.Equal(4, inspector.LoadedLibraries.Count);
        Assert.Equal(4, inspector.FreeCount);
        Assert.Empty(inspector.OpenHandles);
    }

    [Fact]
    public void ExportExceptionBecomesFailureAndReleasesCurrentHandle()
    {
        var inspector = new RecordingInspector { ThrowOnExport = "D3D11CreateDevice" };
        using var probe = CreateSystemProbe(inspector, true);

        Assert.Equal(PlaybackWindowsOutputProbeResult.Failure, Probe(probe).Result);
        Assert.Equal(1, inspector.FreeCount);
        Assert.Empty(inspector.OpenHandles);
    }

    [Fact]
    public void NonWindowsSystemProbeDoesNotCallNativeInspector()
    {
        var inspector = new RecordingInspector();
        using var probe = CreateSystemProbe(inspector, false);

        Assert.Equal(
            PlaybackWindowsOutputProbeResult.PlatformUnsupported,
            Probe(probe).Result);
        Assert.Empty(inspector.LoadedLibraries);
        Assert.Empty(inspector.OpenHandles);
    }

    [Theory]
    [InlineData(PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved)]
    [InlineData(PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved)]
    public void SupportedCanonicalFormatsAreAvailable(
        PlaybackDecodedAudioSampleFormat audioFormat)
    {
        var source = new RecordingSource();
        using var probe = new PlaybackWindowsOutputAdapterProbe(source);

        var report = probe.Probe(PlaybackDecodedVideoPixelFormat.Bgra32, audioFormat);

        Assert.Equal(PlaybackWindowsOutputProbeResult.Available, report.Result);
        Assert.Equal(1, source.VideoCalls);
        Assert.Equal(1, source.AudioCalls);
        Assert.Equal(0, source.OutstandingResources);
    }

    [Fact]
    public void UnsupportedFormatsAreRejectedBeforeSourceAccess()
    {
        var source = new RecordingSource();
        using var probe = new PlaybackWindowsOutputAdapterProbe(source);

        var video = probe.Probe(
            (PlaybackDecodedVideoPixelFormat)99,
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved);
        var audio = probe.Probe(
            PlaybackDecodedVideoPixelFormat.Bgra32,
            (PlaybackDecodedAudioSampleFormat)99);

        Assert.Equal(PlaybackWindowsOutputProbeResult.FormatUnsupported, video.Result);
        Assert.Equal(PlaybackWindowsOutputProbeResult.FormatUnsupported, audio.Result);
        Assert.Equal(0, source.PlatformCalls);
    }

    [Fact]
    public void NonWindowsAndUndefinedPlatformFailClosed()
    {
        var unsupported = new RecordingSource
        {
            PlatformResult = PlaybackWindowsOutputPlatformResult.Unsupported,
        };
        var undefined = new RecordingSource
        {
            PlatformResult = (PlaybackWindowsOutputPlatformResult)99,
        };
        using var unsupportedProbe = new PlaybackWindowsOutputAdapterProbe(unsupported);
        using var undefinedProbe = new PlaybackWindowsOutputAdapterProbe(undefined);

        Assert.Equal(PlaybackWindowsOutputProbeResult.PlatformUnsupported,
            unsupportedProbe.Probe(PlaybackDecodedVideoPixelFormat.Bgra32,
                PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved).Result);
        Assert.Equal(PlaybackWindowsOutputProbeResult.Failure,
            undefinedProbe.Probe(PlaybackDecodedVideoPixelFormat.Bgra32,
                PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved).Result);
        Assert.Equal(0, unsupported.VideoCalls);
        Assert.Equal(0, undefined.VideoCalls);
    }

    [Theory]
    [InlineData(PlaybackWindowsOutputApiResult.ApiUnavailable, PlaybackWindowsOutputProbeResult.ApiUnavailable)]
    [InlineData(PlaybackWindowsOutputApiResult.DeviceUnavailable, PlaybackWindowsOutputProbeResult.DeviceUnavailable)]
    [InlineData(PlaybackWindowsOutputApiResult.FormatUnsupported, PlaybackWindowsOutputProbeResult.FormatUnsupported)]
    [InlineData(PlaybackWindowsOutputApiResult.PermissionDenied, PlaybackWindowsOutputProbeResult.PermissionDenied)]
    [InlineData(PlaybackWindowsOutputApiResult.InvalidEnvironment, PlaybackWindowsOutputProbeResult.InvalidEnvironment)]
    public void ApiResultsAreMappedToFixedProbeResults(
        PlaybackWindowsOutputApiResult apiResult,
        PlaybackWindowsOutputProbeResult expected)
    {
        using var probe = new PlaybackWindowsOutputAdapterProbe(new RecordingSource
        {
            VideoResult = apiResult,
        });

        Assert.Equal(expected,
            probe.Probe(PlaybackDecodedVideoPixelFormat.Bgra32,
                PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved).Result);
    }

    [Fact]
    public void UndefinedApiResultAndExceptionBecomeFailure()
    {
        using var undefined = new PlaybackWindowsOutputAdapterProbe(new RecordingSource
        {
            AudioResult = (PlaybackWindowsOutputApiResult)99,
        });
        using var throwing = new PlaybackWindowsOutputAdapterProbe(new RecordingSource
        {
            ThrowOnVideo = true,
        });

        Assert.Equal(PlaybackWindowsOutputProbeResult.Failure,
            undefined.Probe(PlaybackDecodedVideoPixelFormat.Bgra32,
                PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved).Result);
        Assert.Equal(PlaybackWindowsOutputProbeResult.Failure,
            throwing.Probe(PlaybackDecodedVideoPixelFormat.Bgra32,
                PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved).Result);
    }

    [Theory]
    [InlineData(0, PlaybackWindowsOutputProbeResult.Available)]
    [InlineData(unchecked((int)0x80070005), PlaybackWindowsOutputProbeResult.PermissionDenied)]
    [InlineData(unchecked((int)0x80070057), PlaybackWindowsOutputProbeResult.InvalidEnvironment)]
    [InlineData(unchecked((int)0x887A0002), PlaybackWindowsOutputProbeResult.DeviceUnavailable)]
    [InlineData(unchecked((int)0x887A0004), PlaybackWindowsOutputProbeResult.ApiUnavailable)]
    [InlineData(unchecked((int)0x80040154), PlaybackWindowsOutputProbeResult.ApiUnavailable)]
    [InlineData(unchecked((int)0x80070490), PlaybackWindowsOutputProbeResult.DeviceUnavailable)]
    [InlineData(unchecked((int)0x80010106), PlaybackWindowsOutputProbeResult.InvalidEnvironment)]
    [InlineData(unchecked((int)0x800401F0), PlaybackWindowsOutputProbeResult.InvalidEnvironment)]
    [InlineData(unchecked((int)0x88890008), PlaybackWindowsOutputProbeResult.FormatUnsupported)]
    [InlineData(unchecked((int)0x81234567), PlaybackWindowsOutputProbeResult.Failure)]
    public void HResultMappingIsFixedAndUnknownValuesFailClosed(
        int hresult,
        PlaybackWindowsOutputProbeResult expected)
    {
        Assert.Equal(expected, PlaybackWindowsOutputHResultMapper.Map(hresult));
    }

    [Fact]
    public void DisposeReturnsFixedResultWithoutSourceAccess()
    {
        var source = new RecordingSource();
        var probe = new PlaybackWindowsOutputAdapterProbe(source);
        probe.Dispose();

        var report = probe.Probe(
            PlaybackDecodedVideoPixelFormat.Bgra32,
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved);

        Assert.Equal(PlaybackWindowsOutputProbeResult.Disposed, report.Result);
        Assert.Equal(0, source.PlatformCalls);
    }

    [Fact]
    public void SameInputProducesSameSelectionAndResultWithoutRetainedResources()
    {
        var source = new RecordingSource();
        using var probe = new PlaybackWindowsOutputAdapterProbe(source);

        var first = probe.Probe(
            PlaybackDecodedVideoPixelFormat.Bgra32,
            PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved);
        var second = probe.Probe(
            PlaybackDecodedVideoPixelFormat.Bgra32,
            PlaybackDecodedAudioSampleFormat.PcmFloat32Interleaved);

        Assert.Equal(first, second);
        Assert.Equal(0, source.OutstandingResources);
    }

    [Fact]
    public void SystemSourceRejectsUndefinedAdapterValues()
    {
        var source = new SystemPlaybackWindowsOutputAvailabilitySource();

        Assert.Equal(PlaybackWindowsOutputApiResult.FormatUnsupported,
            source.ProbeVideo(
                (PlaybackWindowsVideoAdapter)99,
                PlaybackDecodedVideoPixelFormat.Bgra32));
        Assert.Equal(PlaybackWindowsOutputApiResult.FormatUnsupported,
            source.ProbeAudio(
                (PlaybackWindowsAudioAdapter)99,
                PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved));
    }

    private static PlaybackWindowsOutputAdapterProbe CreateSystemProbe(
        IPlaybackWindowsNativeLibraryInspector inspector,
        bool isWindows11) =>
        new(new SystemPlaybackWindowsOutputAvailabilitySource(
            inspector,
            () => isWindows11));

    private static PlaybackWindowsOutputProbeReport Probe(
        PlaybackWindowsOutputAdapterProbe probe) =>
        probe.Probe(
            PlaybackDecodedVideoPixelFormat.Bgra32,
            PlaybackDecodedAudioSampleFormat.PcmSigned16Interleaved);

    private sealed class RecordingInspector : IPlaybackWindowsNativeLibraryInspector
    {
        private readonly Dictionary<nint, string> _handles = [];
        private int _nextHandle;

        public string? MissingLibrary { get; init; }
        public string? MissingExport { get; init; }
        public string? ThrowOnExport { get; init; }
        public List<string> LoadedLibraries { get; } = [];
        public IReadOnlyCollection<nint> OpenHandles => _handles.Keys;
        public int FreeCount { get; private set; }

        public bool TryLoadSystemLibrary(string libraryName, out nint handle)
        {
            LoadedLibraries.Add(libraryName);
            if (string.Equals(libraryName, MissingLibrary, StringComparison.OrdinalIgnoreCase))
            {
                handle = 0;
                return false;
            }

            handle = ++_nextHandle;
            _handles.Add(handle, libraryName);
            return true;
        }

        public bool HasExport(nint handle, string exportName)
        {
            Assert.True(_handles.ContainsKey(handle));
            if (string.Equals(exportName, ThrowOnExport, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("fixed-test-exception");
            }

            return !string.Equals(exportName, MissingExport, StringComparison.Ordinal);
        }

        public void Free(nint handle)
        {
            Assert.True(_handles.Remove(handle));
            FreeCount++;
        }
    }

    private sealed class RecordingSource : IPlaybackWindowsOutputAvailabilitySource
    {
        private int _outstandingResources;

        public PlaybackWindowsOutputPlatformResult PlatformResult { get; init; } =
            PlaybackWindowsOutputPlatformResult.Windows11;
        public PlaybackWindowsOutputApiResult VideoResult { get; init; } =
            PlaybackWindowsOutputApiResult.Available;
        public PlaybackWindowsOutputApiResult AudioResult { get; init; } =
            PlaybackWindowsOutputApiResult.Available;
        public bool ThrowOnVideo { get; init; }
        public int PlatformCalls { get; private set; }
        public int VideoCalls { get; private set; }
        public int AudioCalls { get; private set; }
        public int OutstandingResources => _outstandingResources;

        public PlaybackWindowsOutputPlatformResult ProbePlatform()
        {
            PlatformCalls++;
            return PlatformResult;
        }

        public PlaybackWindowsOutputApiResult ProbeVideo(
            PlaybackWindowsVideoAdapter adapter,
            PlaybackDecodedVideoPixelFormat format)
        {
            VideoCalls++;
            _outstandingResources++;
            try
            {
                if (ThrowOnVideo)
                {
                    throw new InvalidOperationException("fixed-test-exception");
                }

                return VideoResult;
            }
            finally
            {
                _outstandingResources--;
            }
        }

        public PlaybackWindowsOutputApiResult ProbeAudio(
            PlaybackWindowsAudioAdapter adapter,
            PlaybackDecodedAudioSampleFormat format)
        {
            AudioCalls++;
            _outstandingResources++;
            try
            {
                return AudioResult;
            }
            finally
            {
                _outstandingResources--;
            }
        }
    }
}
