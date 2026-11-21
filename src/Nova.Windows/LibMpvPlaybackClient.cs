using System.Globalization;
using System.Runtime.InteropServices;
using PeerCast.Nova.Playback;

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
    LibMpvPlaybackEvent WaitForEvent();
    void Wakeup();
}

internal sealed class LibMpvPlaybackException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

internal sealed class LibMpvPlaybackClient : ILibMpvPlaybackClient
{
    private const uint ClientApiMajor = 2;
    private const ulong LoadFileReply = 1;
    private const int EndFileError = 4;
    private const int FormatFlag = 3;
    private const int FormatDouble = 5;
    private readonly object _clientGate = new();
    private readonly nint _library;
    private readonly nint _client;
    private readonly ClientAction _destroy;
    private readonly InitializeClient _initialize;
    private readonly SetOptionString _setOption;
    private readonly SetPropertyDoubleAsync _setPropertyDoubleAsync;
    private readonly SetPropertyFlagAsync _setPropertyFlagAsync;
    private readonly CommandAsync _command;
    private readonly ReadEvent _waitForEvent;
    private readonly ClientAction _wakeup;
    private bool _initialized;
    private bool _disposed;
    private LibMpvPlaybackAudioState _desiredAudio = LibMpvPlaybackAudioState.Default;

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

    public LibMpvPlaybackEvent WaitForEvent()
    {
        var notification = Marshal.PtrToStructure<NativeEvent>(_waitForEvent(_client, -1));
        switch (notification.Id)
        {
            case EventId.FileLoaded:
                return LibMpvPlaybackEvent.FileLoaded;
            case EventId.CommandReply when notification.ReplyUserdata == LoadFileReply:
                Check(notification.Error, "open the loopback HTTP stream");
                break;
            case EventId.EndFile:
                if (notification.Data != 0 && Marshal.ReadInt32(notification.Data) == EndFileError)
                {
                    throw new LibMpvPlaybackException(
                        $"libmpv ended loopback playback with error {Marshal.ReadInt32(notification.Data, sizeof(int))}.");
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
    private delegate int CommandAsync(nint client, ulong replyUserdata, [In] nint[] arguments);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ReadEvent(nint client, double timeoutSeconds);
}
