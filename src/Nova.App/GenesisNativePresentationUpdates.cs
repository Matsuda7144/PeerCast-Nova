using static PeerCast.Nova.App.Win32NativeMethods;

namespace PeerCast.Nova.App;

// One shell lifetime, UI thread only. Coalesce intermediate layout/visibility
// requests and apply the final value of each property once per synchronization.
internal sealed class GenesisNativePresentationUpdates
{
    private readonly Dictionary<(nint, string), object> _applied = new();
    private readonly Dictionary<(nint, string), (object Value, Func<bool> Apply)> _pending = new();
    private readonly HashSet<nint> _invalidations = new();
    private bool _closed;
    private object? _paint;
    private object? _nextPaint;
    private Action<nint>? _invalidate;

    internal IDisposable Begin(object paint, Action<nint> invalidate)
    {
        _nextPaint = paint;
        _invalidate = invalidate;
        return new Completion(this);
    }

    internal bool Set(nint control, string property, object value, Func<bool> apply)
    {
        if (_closed) return false;
        _pending[(control, property)] = (value, apply);
        return true;
    }

    internal bool Invalidate(nint control)
    {
        _invalidations.Add(control);
        return true;
    }

    internal void Reset() { _applied.Clear(); _paint = null; }

    internal void Close() { _closed = true; Reset(); }

    private void Complete()
    {
        var changed = !Equals(_paint, _nextPaint);
        try
        {
            if (_closed) return;
            foreach (var (key, request) in _pending)
            {
                if (_closed) return;
                if (_applied.TryGetValue(key, out var old) && Equals(old, request.Value)) continue;
                changed = true;
                var applied = request.Apply();
                if (_closed) return;
                if (applied) _applied[key] = request.Value;
                else _applied.Remove(key); // A failed native write must be retried.
            }
            _paint = _nextPaint;
            if (changed)
                foreach (var control in _invalidations) _invalidate!(control);
        }
        finally { _pending.Clear(); _invalidations.Clear(); _invalidate = null; }
    }

    private sealed class Completion(GenesisNativePresentationUpdates owner) : IDisposable
    {
        public void Dispose() => owner.Complete();
    }
}

internal sealed partial class GenesisGuiNextAppShellBridge
{
    private sealed partial class AppShellState
    {
        private bool PresentText(nint control, string text) =>
            _detailsUpdates.Set(control, "text", text, () => IsWindow(control) && SetWindowTextW(control, text));

        private bool PresentEnabled(nint control, bool enabled) =>
            _detailsUpdates.Set(control, "enabled", enabled, () =>
            {
                if (!IsWindow(control)) return false;
                _ = EnableWindow(control, enabled);
                return true; // EnableWindow returns the previous state, not success.
            });

        private bool PresentVisible(nint control, int show) =>
            _detailsUpdates.Set(control, "visible", show, () =>
            {
                if (!IsWindow(control)) return false;
                _ = ShowWindow(control, show);
                return true;
            });

        private bool PresentBounds(nint control, int x, int y, int width, int height, bool repaint) =>
            _detailsUpdates.Set(control, "bounds", (x, y, width, height),
                () => IsWindow(control) && MoveWindow(control, Scale(x), Scale(y), Scale(width), Scale(height), repaint));

        private bool PresentInvalidation(nint control, nint rectangle, bool erase) => _detailsUpdates.Invalidate(control);
    }
}
