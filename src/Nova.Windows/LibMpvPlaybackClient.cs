using System.Globalization;
using System.Runtime.InteropServices;

namespace PeerCast.Nova.Windows;

internal enum LibMpvPlaybackEvent
{
    Other,
    FileLoaded,
    Ended,
}

internal interface ILibMpvPlaybackClient : IDisposable
{
    void LoadFile(string streamUrl, uint windowId);
    LibMpvPlaybackEvent WaitForEvent();
    void Wakeup();
}

internal sealed class LibMpvPlaybackException(string message) : InvalidOperationException(message);

internal sealed class LibMpvPlaybackClient : ILibMpvPlaybackClient
{
    private const uint ClientApiMajor = 2;
    private const ulong LoadFileReply = 1;
    private const int EndFileError = 4;
    private readonly nint _library;
    private readonly nint _client;
    private readonly ClientAction _destroy;
    private readonly InitializeClient _initialize;
    private readonly SetOptionString _setOption;
    private readonly CommandAsync _command;
    private readonly ReadEvent _waitForEvent;
    private readonly ClientAction _wakeup;

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

    public void LoadFile(string streamUrl, uint windowId)
    {
        SetOption("config", "no");
        SetOption("load-scripts", "no");
        SetOption("input-default-bindings", "no");
        SetOption("wid", windowId.ToString(CultureInfo.InvariantCulture));
        SetOption("demuxer", "lavf");
        SetOption("demuxer-lavf-format", "flv");
        Check(_initialize(_client), "initialize playback");

        // libmpv copies the null-terminated UTF-8 command vector during the call.
        var arguments = new nint[3];
        try
        {
            arguments[0] = Marshal.StringToCoTaskMemUTF8("loadfile");
            arguments[1] = Marshal.StringToCoTaskMemUTF8(streamUrl);
            Check(_command(_client, LoadFileReply, arguments), "request the HTTP-FLV stream");
        }
        finally
        {
            Marshal.FreeCoTaskMem(arguments[1]);
            Marshal.FreeCoTaskMem(arguments[0]);
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
                Check(notification.Error, "open the HTTP-FLV stream");
                break;
            case EventId.EndFile:
                if (notification.Data != 0 && Marshal.ReadInt32(notification.Data) == EndFileError)
                {
                    throw new LibMpvPlaybackException(
                        $"libmpv ended HTTP-FLV playback with error {Marshal.ReadInt32(notification.Data, sizeof(int))}.");
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
        _destroy(_client);
        NativeLibrary.Free(_library);
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
    private delegate int CommandAsync(nint client, ulong replyUserdata, [In] nint[] arguments);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate nint ReadEvent(nint client, double timeoutSeconds);
}
