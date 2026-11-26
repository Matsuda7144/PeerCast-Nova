using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

internal sealed record GenesisGuiNextFavoriteFeedbackPresentation(
    string Message,
    string Caption);

internal static class GenesisGuiNextFavoriteFeedbackResolver
{
    private const string PersistenceFailureMessage =
        "Favorite changes could not be saved.";
    private const string Caption = "Favorites";

    public static GenesisGuiNextFavoriteFeedbackPresentation? Resolve(
        GenesisGuiNextFavoriteToggleResult result) =>
        result == GenesisGuiNextFavoriteToggleResult.PersistenceFailed
            ? new(PersistenceFailureMessage, Caption)
            : null;
}

internal sealed class GenesisGuiNextFavoriteFeedbackSession : IAsyncDisposable
{
    private readonly GenesisGuiNextTabsViewModel _tabs;
    private readonly Action<GenesisGuiNextFavoriteFeedbackPresentation> _present;
    private int _disposed;

    private GenesisGuiNextFavoriteFeedbackSession(
        GenesisGuiNextTabsViewModel tabs,
        Action<GenesisGuiNextFavoriteFeedbackPresentation> present)
    {
        _tabs = tabs;
        _present = present;
        GenesisGuiNextFavoriteToggle.ResultPublished += OnResultPublished;
    }

    public static GenesisGuiNextFavoriteFeedbackSession Open(
        GenesisGuiNextTabsViewModel tabs,
        Action<GenesisGuiNextFavoriteFeedbackPresentation> present)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        ArgumentNullException.ThrowIfNull(present);
        return new(tabs, present);
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            GenesisGuiNextFavoriteToggle.ResultPublished -= OnResultPublished;
        }

        return ValueTask.CompletedTask;
    }

    private void OnResultPublished(
        GenesisGuiNextTabsViewModel tabs,
        GenesisGuiNextFavoriteToggleResult result)
    {
        if (Volatile.Read(ref _disposed) != 0 || !ReferenceEquals(tabs, _tabs))
        {
            return;
        }

        var presentation = GenesisGuiNextFavoriteFeedbackResolver.Resolve(result);
        if (presentation is null)
        {
            return;
        }

        try
        {
            _present(presentation);
        }
        catch
        {
            // Favorite feedback is auxiliary presentation. It must never alter
            // the already-completed favorite persistence result.
        }
    }
}

internal sealed class GenesisGuiNextAuxiliaryPresentationLifetime : IAsyncDisposable
{
    private readonly IAsyncDisposable _feedbackLifetime;
    private readonly IDisposable _keyboardNavigationLifetime;
    private int _disposed;

    public GenesisGuiNextAuxiliaryPresentationLifetime(
        IAsyncDisposable feedbackLifetime,
        IDisposable keyboardNavigationLifetime)
    {
        ArgumentNullException.ThrowIfNull(feedbackLifetime);
        ArgumentNullException.ThrowIfNull(keyboardNavigationLifetime);
        _feedbackLifetime = feedbackLifetime;
        _keyboardNavigationLifetime = keyboardNavigationLifetime;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        try
        {
            _keyboardNavigationLifetime.Dispose();
        }
        finally
        {
            await _feedbackLifetime.DisposeAsync().ConfigureAwait(false);
        }
    }
}

internal static class SystemGenesisGuiNextFavoriteFeedback
{
    private const uint MessageBoxOk = 0x00000000;
    private const uint MessageBoxIconWarning = 0x00000030;

    public static IAsyncDisposable? Open(
        long windowToken,
        GenesisGuiNextTabsViewModel tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        if (!OperatingSystem.IsWindows() || windowToken == 0)
        {
            return null;
        }

        var owner = (nint)windowToken;
        var feedbackLifetime = GenesisGuiNextFavoriteFeedbackSession.Open(
            tabs,
            presentation => Show(owner, presentation));
        var keyboardNavigationLifetime =
            GenesisGuiNextMainWindowKeyboardNavigation.Open(owner);
        return keyboardNavigationLifetime is null
            ? feedbackLifetime
            : new GenesisGuiNextAuxiliaryPresentationLifetime(
                feedbackLifetime,
                keyboardNavigationLifetime);
    }

    private static void Show(
        nint owner,
        GenesisGuiNextFavoriteFeedbackPresentation presentation)
    {
        if (owner == 0 || !IsWindow(owner))
        {
            return;
        }

        _ = MessageBoxW(
            owner,
            presentation.Message,
            presentation.Caption,
            MessageBoxOk | MessageBoxIconWarning);
    }

}
