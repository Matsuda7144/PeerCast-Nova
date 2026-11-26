namespace PeerCast.Nova.App;

internal sealed partial class GenesisGuiNextTabsViewModel
{
    private NovaObsPublishingConnectionPresentation _publishingConnection = NovaObsPublishingConnectionPresentation.Checking;
    private string? _publishingRevealedKey;
    private NovaObsPublishingConnectionIdentity? _publishingRevealedIdentity;
    private string _publishingConnectionFeedback = string.Empty;
    private long _publishingWindowLifetime;

    internal NovaObsPublishingConnectionPresentation PublishingConnection => _publishingConnection;
    internal string PublishingKeyDisplay => _publishingRevealedIdentity == _publishingConnection.Identity &&
        _publishingConnection.CanUseKey && _publishingRevealedKey is { } key ? key : NovaObsPublishingConnectionPresentation.Mask;
    internal bool PublishingKeyRevealed => _publishingRevealedKey is not null;
    internal string PublishingConnectionFeedback => _publishingConnectionFeedback;

    private void ResetPublishingConnectionLocked()
    {
        _publishingConnection = NovaObsPublishingConnectionPresentation.Checking;
        _publishingRevealedKey = null;
        _publishingRevealedIdentity = null;
        _publishingConnectionFeedback = string.Empty;
    }

    internal void MaskPublishingKey()
    {
        lock (_publishingUiGate)
        {
            lock (_recordingRulesLifecycleGate)
            {
                _publishingRevealedKey = null;
                _publishingRevealedIdentity = null;
                _publishingWindowLifetime++;
                if (_disposed || _publishingClosed) return;
            }
            OnPropertyChanged(nameof(PublishingConnection));
        }
    }

    private void NotifyPublishingConnection()
    {
        Action<Action>? dispatch;
        long lifetime;
        lock (_recordingRulesLifecycleGate) { dispatch = _publishingDispatch; lifetime = _publishingWindowLifetime; }
        dispatch?.Invoke(() =>
        {
            lock (_recordingRulesLifecycleGate)
                if (_disposed || _publishingClosed || lifetime != _publishingWindowLifetime) return;
            OnPropertyChanged(nameof(PublishingConnection));
        });
    }

    private void UpdatePublishingConnectionLocked(NovaObsPublishingConnectionPresentation presentation)
    {
        if (_publishingConnection.Identity != presentation.Identity || !presentation.CanUseKey)
        {
            _publishingRevealedKey = null;
            _publishingRevealedIdentity = null;
            _publishingConnectionFeedback = string.Empty;
        }
        _publishingConnection = presentation;
    }

    internal Task TogglePublishingKeyAsync()
    {
        if (PublishingKeyRevealed) { MaskPublishingKey(); return Task.CompletedTask; }
        return RequestPublishingConnectionActionAsync(reveal: true, key: true, write: null);
    }

    // The writer must call its supplied validation immediately before the OS write.
    internal Task CopyPublishingConnectionAsync(bool key, Func<string, Func<bool>, bool> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        return RequestPublishingConnectionActionAsync(reveal: false, key, write);
    }

    private Task RequestPublishingConnectionActionAsync(bool reveal, bool key, Func<string, Func<bool>, bool>? write)
    {
        PublishingBinding binding;
        INovaObsPublishingConnectionInfo reader;
        NovaObsPublishingConnectionIdentity identity;
        NovaObsPublishingReadOnlySnapshot status;
        Action<Action> dispatch;
        long lifetime;
        lock (_recordingRulesLifecycleGate)
        {
            if (_disposed || _publishingClosed || SelectedTab != GenesisGuiNextTab.Publishing ||
                _publishingDispatch is null || _publishingPublishedBinding is not { } current ||
                !ReferenceEquals(current, _publishingBinding) ||
                current.Source is not INovaObsPublishingConnectionInfo connection ||
                _publishingConnection.Identity is not { } id || _publishingRows.Count != 1 ||
                (key ? !_publishingConnection.CanUseKey : !_publishingConnection.CanCopyServer))
                return Task.CompletedTask;
            binding = current; reader = connection; identity = id; status = _publishingRows[0];
            dispatch = _publishingDispatch; lifetime = _publishingWindowLifetime;
            var task = Task.Run(() =>
            {
                NovaObsPublishingConnectionInfo? info;
                try { info = reader.ReadConnectionInfo(status); }
                catch { info = null; } // Never publish exception text or raw connection values.
                dispatch(() => CompletePublishingConnectionAction(binding, reader, identity, lifetime, info, reveal, key, write));
            });
            _publishingOperations.Add(task);
            ObservePublishingTask(task);
            return task;
        }
    }

    private bool IsPublishingConnectionTargetCurrent(PublishingBinding binding,
        INovaObsPublishingConnectionInfo reader, NovaObsPublishingConnectionIdentity identity, long lifetime)
    {
        bool Local() { lock (_recordingRulesLifecycleGate) return IsPublishingConnectionBindingCurrentLocked(binding, identity, lifetime); }
        if (!Local()) return false;
        try { if (!reader.IsConnectionInfoCurrent(identity)) return false; }
        catch { return false; }
        return Local(); // No external callback under a VM admission gate.
    }

    private void InvalidatePublishingConnectionTarget(PublishingBinding binding,
        NovaObsPublishingConnectionIdentity identity, long lifetime)
    {
        lock (_publishingUiGate)
        {
            lock (_recordingRulesLifecycleGate)
            {
                if (!IsPublishingConnectionBindingCurrentLocked(binding, identity, lifetime)) return;
                ResetPublishingConnectionLocked();
            }
            OnPropertyChanged(nameof(PublishingConnection));
        }
    }

    private bool IsPublishingConnectionBindingCurrentLocked(PublishingBinding binding,
        NovaObsPublishingConnectionIdentity identity, long lifetime) => !_disposed && !_publishingClosed &&
        lifetime == _publishingWindowLifetime && _publishingDispatch is not null &&
        SelectedTab == GenesisGuiNextTab.Publishing && ReferenceEquals(binding, _publishingBinding) &&
        ReferenceEquals(binding, _publishingPublishedBinding) && binding.Version == _publishingVersion &&
        _publishingConnection.Identity == identity;

    private void CompletePublishingConnectionAction(PublishingBinding binding, INovaObsPublishingConnectionInfo reader,
        NovaObsPublishingConnectionIdentity identity, long lifetime, NovaObsPublishingConnectionInfo? info,
        bool reveal, bool key, Func<string, Func<bool>, bool>? write)
    {
        bool Current() => IsPublishingConnectionTargetCurrent(binding, reader, identity, lifetime);
        if (info is null || info.Identity != identity || !Current() || (key ? !info.CanUseKey : !info.CanCopyServer))
        {
            InvalidatePublishingConnectionTarget(binding, identity, lifetime);
            RefreshPublishingStatus();
            return;
        }
        if (reveal)
        {
            lock (_publishingUiGate)
            {
                if (!Current())
                {
                    InvalidatePublishingConnectionTarget(binding, identity, lifetime);
                    RefreshPublishingStatus();
                    return;
                }
                lock (_recordingRulesLifecycleGate)
                {
                    if (!IsPublishingConnectionBindingCurrentLocked(binding, identity, lifetime)) return;
                    _publishingRevealedIdentity = identity;
                    _publishingRevealedKey = info.Key;
                    _publishingConnectionFeedback = string.Empty;
                }
                OnPropertyChanged(nameof(PublishingConnection));
            }
        }
        else
        {
            bool copied;
            try { copied = write!(key ? info.Key : info.Server!, Current); }
            catch { copied = false; }
            // Rebinding/window closure during an OS callback must not publish feedback.
            if (!Current()) { InvalidatePublishingConnectionTarget(binding, identity, lifetime); RefreshPublishingStatus(); return; }
            lock (_publishingUiGate)
            {
                lock (_recordingRulesLifecycleGate)
                {
                    if (!IsPublishingConnectionBindingCurrentLocked(binding, identity, lifetime)) return;
                    _publishingConnectionFeedback = copied ? "コピーしました" : "コピーできませんでした。もう一度操作してください。";
                }
                OnPropertyChanged(nameof(PublishingConnection));
            }
        }
    }
}
