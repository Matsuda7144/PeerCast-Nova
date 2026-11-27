using System.Runtime.InteropServices;

using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed record GenesisChannelNotificationRuntime(
    IGenesisChannelPresenceNotificationPublisher Publisher,
    IAsyncDisposable Lifetime)
{
    public static GenesisChannelNotificationRuntime Empty { get; } =
        new(
            EmptyGenesisChannelPresenceNotificationPublisher.Instance,
            NoopLifetime.Instance);

    private sealed class NoopLifetime : IAsyncDisposable
    {
        public static NoopLifetime Instance { get; } = new();

        private NoopLifetime()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal static class SystemGenesisChannelNotificationRuntime
{
    public static GenesisChannelNotificationRuntime Create(long windowToken) =>
        Create(
            windowToken,
            new GenesisFavoriteChannelCollection(),
            static () => GenesisChannelNotificationPreferences.Default);

    internal static GenesisChannelNotificationRuntime Create(
        long windowToken,
        GenesisFavoriteChannelCollection favorites,
        Func<GenesisChannelNotificationPreferences> preferencesProvider)
    {
        ArgumentNullException.ThrowIfNull(favorites);
        ArgumentNullException.ThrowIfNull(preferencesProvider);

        var presenter = SystemGenesisChannelNotificationPresenter.TryCreate(windowToken);
        if (presenter is null)
        {
            return GenesisChannelNotificationRuntime.Empty;
        }

        var dispatcher = new GenesisChannelNotificationDispatcher(
            presenter,
            SystemGenesisSystemNotificationSoundPlayer.Instance);
        return new(
            new GenesisChannelNotificationPublisher(
                favorites,
                preferencesProvider,
                dispatcher),
            presenter);
    }
}

/// <summary>
/// Shows a bounded Windows notification-area balloon for one channel activation.
/// The notification-area icon exists only while notifications are being shown and
/// is removed automatically or when the Genesis GUI lifetime ends.
/// </summary>
internal sealed class SystemGenesisChannelNotificationPresenter :
    IGenesisChannelNotificationPresenter,
    IAsyncDisposable
{
    private const uint NotifyIconAdd = 0x00000000;
    private const uint NotifyIconModify = 0x00000001;
    private const uint NotifyIconDelete = 0x00000002;
    private const uint NotifyIconFlagIcon = 0x00000002;
    private const uint NotifyIconFlagTip = 0x00000004;
    private const uint NotifyIconFlagInfo = 0x00000010;
    private const uint NotifyIconInfo = 0x00000001;
    private const uint NotifyIconNoSound = 0x00000010;
    private const uint NotifyIconRespectQuietTime = 0x00000080;
    private const uint NotificationIconId = 7144;
    private const int ApplicationIconResourceId = 32512;
    private const int MaximumToolTipCharacters = 127;
    private const int MaximumInfoCharacters = 255;
    private const int MaximumInfoTitleCharacters = 63;
    private static readonly TimeSpan IconRetention = TimeSpan.FromSeconds(15);

    private readonly object _sync = new();
    private readonly nint _window;
    private readonly nint _icon;
    private Timer? _cleanupTimer;
    private bool _iconAdded;
    private int _disposed;

    private SystemGenesisChannelNotificationPresenter(nint window, nint icon)
    {
        _window = window;
        _icon = icon;
    }

    public static SystemGenesisChannelNotificationPresenter? TryCreate(long windowToken)
    {
        if (!OperatingSystem.IsWindows() || windowToken == 0)
        {
            return null;
        }

        var window = (nint)windowToken;
        if (!IsWindow(window))
        {
            return null;
        }

        var icon = LoadIconW(0, (nint)ApplicationIconResourceId);
        return icon == 0 ? null : new(window, icon);
    }

    public ValueTask PresentAsync(
        GenesisChannelNotification notification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
            var data = CreateNotificationData(_window, _icon, notification);
            var succeeded = _iconAdded
                ? Shell_NotifyIconW(NotifyIconModify, ref data)
                : Shell_NotifyIconW(NotifyIconAdd, ref data);
            if (!succeeded && _iconAdded)
            {
                _iconAdded = false;
                succeeded = Shell_NotifyIconW(NotifyIconAdd, ref data);
            }

            if (!succeeded)
            {
                throw new InvalidOperationException(
                    "Windows channel notification could not be presented.");
            }

            _iconAdded = true;
            ScheduleIconRemovalLocked();
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Timer? cleanupTimer;
        lock (_sync)
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return ValueTask.CompletedTask;
            }

            cleanupTimer = _cleanupTimer;
            _cleanupTimer = null;
            RemoveIconLocked();
        }

        cleanupTimer?.Dispose();
        return ValueTask.CompletedTask;
    }

    internal static string BuildNotificationTitle(
        GenesisChannelNotificationReason reasons)
    {
        var title = (reasons & GenesisChannelNotificationReason.FavoriteChannel) != 0
            ? "Favorite channel is live"
            : "New channel is live";
        return Truncate(title, MaximumInfoTitleCharacters);
    }

    internal static string BuildNotificationBody(GenesisChannelNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var text = string.IsNullOrWhiteSpace(notification.Channel.Name)
            ? notification.Channel.Source
            : notification.Channel.Name;
        return Truncate(text, MaximumInfoCharacters);
    }

    internal static Win32NotifyIconData CreateNotificationData(
        nint window,
        nint icon,
        GenesisChannelNotification notification) =>
        new()
        {
            Size = (uint)Marshal.SizeOf<Win32NotifyIconData>(),
            Window = window,
            Id = NotificationIconId,
            Flags = NotifyIconFlagIcon | NotifyIconFlagTip | NotifyIconFlagInfo,
            Icon = icon,
            ToolTip = Truncate("PeerCast Nova", MaximumToolTipCharacters),
            Info = BuildNotificationBody(notification),
            InfoTitle = BuildNotificationTitle(notification.Reasons),
            // The shared sound player owns optional audio, including Test Sound.
            // Do not let the Shell add sound outside that preference boundary.
            InfoFlags = NotifyIconInfo | NotifyIconNoSound | NotifyIconRespectQuietTime,
        };

    private void ScheduleIconRemovalLocked()
    {
        _cleanupTimer ??= new Timer(
            static state =>
                ((SystemGenesisChannelNotificationPresenter)state!).RemoveIconFromTimer(),
            this,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);
        _ = _cleanupTimer.Change(IconRetention, Timeout.InfiniteTimeSpan);
    }

    private void RemoveIconFromTimer()
    {
        lock (_sync)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            RemoveIconLocked();
        }
    }

    private void RemoveIconLocked()
    {
        if (!_iconAdded)
        {
            return;
        }

        var data = new Win32NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<Win32NotifyIconData>(),
            Window = _window,
            Id = NotificationIconId,
        };
        _ = Shell_NotifyIconW(NotifyIconDelete, ref data);
        _iconAdded = false;
    }

    private static string Truncate(string value, int maximumCharacters) =>
        value.Length <= maximumCharacters
            ? value
            : value[..maximumCharacters];
}

internal sealed class SystemGenesisSystemNotificationSoundPlayer :
    IGenesisSystemNotificationSoundPlayer
{
    private const uint SoundAsync = 0x00000001;
    private const uint SoundAlias = 0x00010000;
    private const uint SoundSystem = 0x00200000;

    public static SystemGenesisSystemNotificationSoundPlayer Instance { get; } = new();

    private SystemGenesisSystemNotificationSoundPlayer()
    {
    }

    public ValueTask PlayAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!OperatingSystem.IsWindows())
        {
            return ValueTask.CompletedTask;
        }

        if (!PlaySoundW(
                "SystemAsterisk",
                0,
                SoundAlias | SoundAsync | SoundSystem))
        {
            throw new InvalidOperationException(
                "Windows system notification sound could not be played.");
        }

        return ValueTask.CompletedTask;
    }
}
