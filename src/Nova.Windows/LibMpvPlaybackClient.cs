using System.Globalization;
using System.Runtime.InteropServices;
using PeerCast.Nova.Playback;
using PeerCast.Nova.Playback.Bbs;

namespace PeerCast.Nova.Windows;

internal enum LibMpvPlaybackEvent
{
    Other,
    FileLoaded,
    Ended,
}

internal interface ILibMpvPlaybackClient : IDisposable
{
    void LoadFile(string streamUrl, PlaybackMediaContainer container, uint windowId, LibMpvPlaybackAudioState initialAudio, bool lowLatency);
    void SetVolume(int volume);
    void SetMuted(bool muted);
    void SetPaused(bool paused);
    void SetSpeed(double speed);
    void Reload() { }
    void SeekRelative(double seconds) { }
    void ShowOsdText(string text, int durationMs);
    bool SaveScreenshot(string filePath);
    void SetVideoAdjustment(LibMpvVideoAdjustment adjustment);
    LibMpvPlaybackStats? TryReadStats();
    LibMpvPlaybackEvent WaitForEvent();
    void Wakeup();
}

internal sealed class LibMpvPlaybackException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

internal sealed class LibMpvPlaybackClient : ILibMpvPlaybackClient
{
    internal const double MinSpeed = 0.5;
    internal const double MaxSpeed = 4.0;
    internal const double MaxRelativeSeekSeconds = 300.0;
    internal static readonly double[] SpeedPresets = [0.5, 0.75, 1.0, 1.25, 1.5, 2.0];
    private const uint ClientApiMajor = 2;
    private const ulong LoadFileReply = 1;
    private const ulong ReloadFileReply = 2;
    internal const int EndFileStop = 2;
    internal const int EndFileQuit = 3;
    internal const int EndFileError = 4;
    private const int FormatFlag = 3;
    private const int FormatInt64 = 4;
    private const int FormatDouble = 5;
    private readonly object _clientGate = new();
    private readonly nint _library;
    private readonly nint _client;
    private readonly ClientAction _destroy;
    private readonly InitializeClient _initialize;
    private readonly SetOptionString _setOption;
    private readonly SetPropertyDoubleAsync _setPropertyDoubleAsync;
    private readonly SetPropertyFlagAsync _setPropertyFlagAsync;
    private readonly GetPropertyDouble _getPropertyDouble;
    private readonly GetPropertyInt64 _getPropertyInt64;
    private readonly CommandAsync _command;
    private readonly ReadEvent _waitForEvent;
    private readonly ClientAction _wakeup;
    private bool _initialized;
    private bool _disposed;
    private LibMpvPlaybackAudioState _desiredAudio = LibMpvPlaybackAudioState.Default;
    private string? _currentStreamUrl;
    private int _pendingReloadEndFiles;

    private LibMpvPlaybackClient(nint library)
    {
        _library = library;
        var version = Bind<ClientApiVersion>(library, "mpv_client_api_version")();
        if (version >> 16 != ClientApiMajor)
        {
            throw new LibMpvPlaybackException("The configured libmpv DLL does not implement client API 2.");
        }

        _destroy = Bind<ClientAction>(library, "mpv_terminate_destroy");
        _initialize = Bind<InitializeClient>(library, "mpv_initialize");
        _setOption = Bind<SetOptionString>(library, "mpv_set_option_string");
        _setPropertyDoubleAsync = Bind<SetPropertyDoubleAsync>(library, "mpv_set_property_async");
        _setPropertyFlagAsync = Bind<SetPropertyFlagAsync>(library, "mpv_set_property_async");
        _getPropertyDouble = Bind<GetPropertyDouble>(library, "mpv_get_property");
        _getPropertyInt64 = Bind<GetPropertyInt64>(library, "mpv_get_property");
        _command = Bind<CommandAsync>(library, "mpv_command_async");
        _waitForEvent = Bind<ReadEvent>(library, "mpv_wait_event");
        _wakeup = Bind<ClientAction>(library, "mpv_wakeup");
        _client = Bind<CreateClient>(library, "mpv_create")();
        if (_client == 0)
        {
            throw new LibMpvPlaybackException("libmpv could not create a playback client.");
        }
    }

    public static LibMpvPlaybackClient Open(string libraryPath)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new LibMpvPlaybackException("Embedded libmpv playback requires Windows.");
        }

        nint library = 0;
        try
        {
            library = NativeLibrary.Load(libraryPath, typeof(LibMpvPlaybackClient).Assembly,
                DllImportSearchPath.UseDllDirectoryForDependencies | DllImportSearchPath.System32);
            var player = new LibMpvPlaybackClient(library);
            library = 0;
            return player;
        }
        catch (Exception exception) when (
            exception is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
        {
            throw new LibMpvPlaybackException(
                "The configured libmpv DLL could not be loaded. Check the DLL, its architecture, and its dependencies.");
        }
        finally
        {
            if (library != 0)
            {
                NativeLibrary.Free(library);
            }
        }
    }

    public void LoadFile(
        string streamUrl, PlaybackMediaContainer container, uint windowId, LibMpvPlaybackAudioState initialAudio, bool lowLatency)
    {
        var normalized = initialAudio.Normalized();
        lock (_clientGate)
        {
            if (_disposed)
            {
                return;
            }

            _desiredAudio = normalized;
            _currentStreamUrl = streamUrl;
            _pendingReloadEndFiles = 0;
            SetStartupOptions(SetOption, container, windowId, normalized, lowLatency);
            Check(_initialize(_client), "initialize playback");
            _initialized = true;

            // libmpv copies the null-terminated UTF-8 command vector during the call.
            var arguments = new nint[3];
            try
            {
                arguments[0] = Marshal.StringToCoTaskMemUTF8("loadfile");
                arguments[1] = Marshal.StringToCoTaskMemUTF8(streamUrl);
                Check(_command(_client, LoadFileReply, arguments), "request the loopback HTTP stream");
            }
            finally
            {
                Marshal.FreeCoTaskMem(arguments[1]);
                Marshal.FreeCoTaskMem(arguments[0]);
            }
        }
    }

    internal static void SetStartupOptions(
        Action<string, string> setOption, PlaybackMediaContainer container, uint windowId, LibMpvPlaybackAudioState audio, bool lowLatency)
    {
        setOption("config", "no");
        setOption("load-scripts", "no");
        setOption("input-default-bindings", "no");
        setOption("wid", windowId.ToString(CultureInfo.InvariantCulture));
        setOption("hwdec", "auto-safe");
        setOption("demuxer", "lavf");
        setOption("demuxer-lavf-format", DemuxerFormatFor(container));
        setOption("volume", audio.Volume.ToString(CultureInfo.InvariantCulture));
        setOption("mute", audio.Muted ? "yes" : "no");
        if (!lowLatency)
        {
            return;
        }

        try
        {
            // Network caching can use the larger of the two readahead durations.
            setOption("demuxer-readahead-secs", "0.3");
            setOption("cache-secs", "0.3");
            setOption("cache-pause-wait", "0.15");
            setOption("cache-pause-initial", "no");
            // Keep lavf, stream, audio, and decoder buffering small so readahead caps lower display latency.
            setOption("demuxer-lavf-o", "fflags=+nobuffer");
            setOption("demuxer-lavf-analyzeduration", "0.25");
            setOption("stream-buffer-size", "4k");
            setOption("audio-buffer", "0.05");
            setOption("video-latency-hacks", "yes");
            setOption("vd-lavc-threads", "1");
        }
        catch (LibMpvPlaybackException exception)
        {
            throw new LibMpvPlaybackException(
                "このlibmpvでは低遅延設定を適用できません。標準に切り替えて再生してください。", exception);
        }
    }

    internal static string DemuxerFormatFor(PlaybackMediaContainer container) => container switch
    {
        PlaybackMediaContainer.FlashVideo => "flv",
        PlaybackMediaContainer.FragmentedMp4 => "mov",
        _ => throw new ArgumentOutOfRangeException(nameof(container)),
    };

    public void SetVolume(int volume)
    {
        var clamped = LibMpvPlaybackAudioState.ClampVolume(volume);
        lock (_clientGate)
        {
            _desiredAudio = _desiredAudio with { Volume = clamped };
            if (_disposed || !_initialized)
            {
                return;
            }

            double value = clamped;
            _ = _setPropertyDoubleAsync(_client, 0, "volume", FormatDouble, ref value);
        }
    }

    public void SetMuted(bool muted)
    {
        lock (_clientGate)
        {
            _desiredAudio = _desiredAudio with { Muted = muted };
            if (_disposed || !_initialized)
            {
                return;
            }

            int value = muted ? 1 : 0;
            _ = _setPropertyFlagAsync(_client, 0, "mute", FormatFlag, ref value);
        }
    }

    internal static double ClampSpeed(double speed) =>
        double.IsFinite(speed) ? Math.Clamp(speed, MinSpeed, MaxSpeed) : 1.0;

    internal static double StepSpeed(double currentSpeed, int direction)
    {
        var current = ClampSpeed(currentSpeed);
        if (direction > 0)
        {
            foreach (var preset in SpeedPresets)
            {
                if (preset > current + 0.001)
                {
                    return preset;
                }
            }

            return SpeedPresets[^1];
        }

        if (direction < 0)
        {
            for (var i = SpeedPresets.Length - 1; i >= 0; i--)
            {
                if (SpeedPresets[i] < current - 0.001)
                {
                    return SpeedPresets[i];
                }
            }

            return SpeedPresets[0];
        }

        return current;
    }

    internal static string FormatSpeedLabel(double speed) =>
        string.Create(CultureInfo.InvariantCulture, $"{ClampSpeed(speed):0.##}x");

    public void SetPaused(bool paused)
    {
        lock (_clientGate)
        {
            if (_disposed || !_initialized)
            {
                return;
            }

            int value = paused ? 1 : 0;
            _ = _setPropertyFlagAsync(_client, 0, "pause", FormatFlag, ref value);
        }
    }

    public void SetSpeed(double speed)
    {
        var clamped = ClampSpeed(speed);
        lock (_clientGate)
        {
            if (_disposed || !_initialized)
            {
                return;
            }

            double value = clamped;
            _ = _setPropertyDoubleAsync(_client, 0, "speed", FormatDouble, ref value);
        }
    }

    /// <summary>現在視聴中のストリームを再度読み込み直し、再生位置を最新付近へ復帰させる。</summary>
    public void Reload()
    {
        lock (_clientGate)
        {
            if (_disposed || !_initialized || string.IsNullOrEmpty(_currentStreamUrl))
            {
                return;
            }

            var arguments = new nint[4];
            try
            {
                arguments[0] = Marshal.StringToCoTaskMemUTF8("loadfile");
                arguments[1] = Marshal.StringToCoTaskMemUTF8(_currentStreamUrl);
                arguments[2] = Marshal.StringToCoTaskMemUTF8("replace");
                _pendingReloadEndFiles++;
                if (_command(_client, ReloadFileReply, arguments) < 0)
                {
                    _pendingReloadEndFiles--;
                }
            }
            finally
            {
                Marshal.FreeCoTaskMem(arguments[2]);
                Marshal.FreeCoTaskMem(arguments[1]);
                Marshal.FreeCoTaskMem(arguments[0]);
            }
        }
    }

    internal static bool ShouldSuppressEndFileForReload(int reason, ref int pendingReloadEndFiles)
    {
        if (pendingReloadEndFiles <= 0 || reason is EndFileError or EndFileQuit)
        {
            return false;
        }

        pendingReloadEndFiles--;
        return true;
    }

    internal static double ClampSeekDelta(double seconds) =>
        double.IsFinite(seconds) ? Math.Clamp(seconds, -MaxRelativeSeekSeconds, MaxRelativeSeekSeconds) : 0.0;

    internal static string FormatSeekOsd(double seconds)
    {
        var clamped = ClampSeekDelta(seconds);
        return clamped >= 0
            ? string.Create(CultureInfo.InvariantCulture, $"+{clamped:0.##}秒")
            : string.Create(CultureInfo.InvariantCulture, $"{clamped:0.##}秒");
    }

    /// <summary>再生位置を現在の位置から指定秒数だけ相対シークする。</summary>
    public void SeekRelative(double seconds)
    {
        var clamped = ClampSeekDelta(seconds);
        if (Math.Abs(clamped) < 0.01)
        {
            return;
        }

        lock (_clientGate)
        {
            if (_disposed || !_initialized)
            {
                return;
            }

            var arguments = new nint[4];
            try
            {
                arguments[0] = Marshal.StringToCoTaskMemUTF8("seek");
                arguments[1] = Marshal.StringToCoTaskMemUTF8(clamped.ToString("0.##", CultureInfo.InvariantCulture));
                arguments[2] = Marshal.StringToCoTaskMemUTF8("relative");
                _ = _command(_client, 0, arguments);
            }
            finally
            {
                Marshal.FreeCoTaskMem(arguments[2]);
                Marshal.FreeCoTaskMem(arguments[1]);
                Marshal.FreeCoTaskMem(arguments[0]);
            }
        }
    }

    public void ShowOsdText(string text, int durationMs)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var clampedDuration = Math.Clamp(durationMs, 500, 30_000);
        lock (_clientGate)
        {
            if (_disposed || !_initialized)
            {
                return;
            }

            var arguments = new nint[4];
            try
            {
                arguments[0] = Marshal.StringToCoTaskMemUTF8("show-text");
                arguments[1] = Marshal.StringToCoTaskMemUTF8(text);
                arguments[2] = Marshal.StringToCoTaskMemUTF8(clampedDuration.ToString(CultureInfo.InvariantCulture));
                _ = _command(_client, 0, arguments);
            }
            finally
            {
                Marshal.FreeCoTaskMem(arguments[2]);
                Marshal.FreeCoTaskMem(arguments[1]);
                Marshal.FreeCoTaskMem(arguments[0]);
            }
        }
    }

    public bool SaveScreenshot(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        lock (_clientGate)
        {
            if (_disposed || !_initialized)
            {
                return false;
            }

            var arguments = new nint[4];
            try
            {
                arguments[0] = Marshal.StringToCoTaskMemUTF8("screenshot-to-file");
                arguments[1] = Marshal.StringToCoTaskMemUTF8(filePath);
                arguments[2] = Marshal.StringToCoTaskMemUTF8("video");
                return _command(_client, 0, arguments) >= 0;
            }
            finally
            {
                Marshal.FreeCoTaskMem(arguments[2]);
                Marshal.FreeCoTaskMem(arguments[1]);
                Marshal.FreeCoTaskMem(arguments[0]);
            }
        }
    }

    public void SetVideoAdjustment(LibMpvVideoAdjustment adjustment)
    {
        var clamped = adjustment.Clamped();
        lock (_clientGate)
        {
            if (_disposed || !_initialized)
            {
                return;
            }

            double brightness = clamped.Brightness;
            double contrast = clamped.Contrast;
            double saturation = clamped.Saturation;
            double gamma = clamped.Gamma;
            _ = _setPropertyDoubleAsync(_client, 0, "brightness", FormatDouble, ref brightness);
            _ = _setPropertyDoubleAsync(_client, 0, "contrast", FormatDouble, ref contrast);
            _ = _setPropertyDoubleAsync(_client, 0, "saturation", FormatDouble, ref saturation);
            _ = _setPropertyDoubleAsync(_client, 0, "gamma", FormatDouble, ref gamma);
        }
    }

    public LibMpvPlaybackStats? TryReadStats()
    {
        lock (_clientGate)
        {
            if (_disposed || !_initialized)
            {
                return null;
            }

            var width = _getPropertyInt64(_client, "video-params/w", FormatInt64, out var w) == 0 ? (int)w : 0;
            var height = _getPropertyInt64(_client, "video-params/h", FormatInt64, out var h) == 0 ? (int)h : 0;
            double? fps = _getPropertyDouble(_client, "estimated-vf-fps", FormatDouble, out var estFps) == 0 && estFps > 0.5
                ? estFps
                : _getPropertyDouble(_client, "container-fps", FormatDouble, out var containerFps) == 0 && containerFps > 0.5
                    ? containerFps
                    : null;
            var videoBitrate = _getPropertyDouble(_client, "video-bitrate", FormatDouble, out var vb) == 0 && vb > 0 ? vb : 0;
            var audioBitrate = _getPropertyDouble(_client, "audio-bitrate", FormatDouble, out var ab) == 0 && ab > 0 ? ab : 0;
            double? totalBitrate = videoBitrate + audioBitrate > 0 ? videoBitrate + audioBitrate : null;
            double? cacheSecs = _getPropertyDouble(_client, "demuxer-cache-duration", FormatDouble, out var cache) == 0 && cache >= 0
                ? cache
                : null;

            return new LibMpvPlaybackStats(width, height, fps, totalBitrate, cacheSecs);
        }
    }

    internal static string FormatLatestPostOsdAss(BbsPost post)
    {
        var compact = post.FormatCompactSingleLine(maxBodyLength: 90);
        var escaped = compact
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("{", "\\{", StringComparison.Ordinal)
            .Replace("}", "\\}", StringComparison.Ordinal);
        return $"${{osd-ass-cc/0}}{{\\an1\\fs22\\b1\\bord2\\shad1\\c&HFFFFFF&\\3c&H000000&}}{escaped}";
    }

    public LibMpvPlaybackEvent WaitForEvent()
    {
        var notification = Marshal.PtrToStructure<NativeEvent>(_waitForEvent(_client, -1));
        switch (notification.Id)
        {
            case EventId.FileLoaded:
                lock (_clientGate)
                {
                    _pendingReloadEndFiles = 0;
                }
                return LibMpvPlaybackEvent.FileLoaded;
            case EventId.CommandReply when notification.ReplyUserdata == LoadFileReply:
                Check(notification.Error, "open the loopback HTTP stream");
                break;
            case EventId.CommandReply when notification.ReplyUserdata == ReloadFileReply:
                if (notification.Error < 0)
                {
                    lock (_clientGate)
                    {
                        if (_pendingReloadEndFiles > 0)
                        {
                            _pendingReloadEndFiles--;
                        }
                    }
                }
                break;
            case EventId.EndFile:
                var reason = notification.Data != 0 ? Marshal.ReadInt32(notification.Data) : 0;
                if (reason == EndFileError)
                {
                    throw new LibMpvPlaybackException(
                        $"libmpv ended loopback playback with error {Marshal.ReadInt32(notification.Data, sizeof(int))}.");
                }

                lock (_clientGate)
                {
                    if (ShouldSuppressEndFileForReload(reason, ref _pendingReloadEndFiles))
                    {
                        return LibMpvPlaybackEvent.Other;
                    }
                }
                return LibMpvPlaybackEvent.Ended;
            case EventId.Shutdown:
                return LibMpvPlaybackEvent.Ended;
            case EventId.QueueOverflow:
                throw new LibMpvPlaybackException("The libmpv playback event queue overflowed.");
        }
        return LibMpvPlaybackEvent.Other;
    }

    public void Wakeup() => _wakeup(_client);

    public void Dispose()
    {
        lock (_clientGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _destroy(_client);
            NativeLibrary.Free(_library);
        }
    }

    private void SetOption(string name, string value) =>
        Check(_setOption(_client, name, value), $"set the {name} option");

    private static void Check(int result, string action)
    {
        if (result < 0)
        {
            throw new LibMpvPlaybackException($"libmpv could not {action} (error {result}).");
        }
    }

    private static T Bind<T>(nint library, string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));

    private enum EventId
    {
        Shutdown = 1,
        CommandReply = 5,
        EndFile = 7,
        FileLoaded = 8,
        QueueOverflow = 24,
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeEvent
    {
        public EventId Id;
        public int Error;
        public ulong ReplyUserdata;
        public nint Data;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate uint ClientApiVersion();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint CreateClient();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int InitializeClient(nint client);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ClientAction(nint client);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetOptionString(nint client,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetPropertyDoubleAsync(nint client, ulong replyUserdata,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, ref double data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SetPropertyFlagAsync(nint client, ulong replyUserdata,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, ref int data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetPropertyDouble(nint client,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, out double data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetPropertyInt64(nint client,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int format, out long data);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int CommandAsync(nint client, ulong replyUserdata, [In] nint[] arguments);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ReadEvent(nint client, double timeoutSeconds);
}
