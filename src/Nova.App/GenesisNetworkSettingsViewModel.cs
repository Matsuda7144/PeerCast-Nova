using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PeerCast.Nova.App;

internal sealed class GenesisNetworkBasicSettings
{
    public GenesisNetworkBasicSettings(
        int port,
        bool ipv6Enabled,
        bool automaticPortMappingEnabled,
        bool allowLocalRelay,
        bool allowGlobalRelay)
    {
        GenesisNetworkListenerSettings.ValidatePort(port, nameof(port));
        Port = port;
        Ipv6Enabled = ipv6Enabled;
        AutomaticPortMappingEnabled = automaticPortMappingEnabled;
        AllowLocalRelay = allowLocalRelay;
        AllowGlobalRelay = allowGlobalRelay;
    }

    public int Port { get; }

    public bool Ipv6Enabled { get; }

    public bool AutomaticPortMappingEnabled { get; }

    public bool AllowLocalRelay { get; }

    public bool AllowGlobalRelay { get; }
}

internal sealed class GenesisNetworkSettingsAppliedSnapshot
{
    public GenesisNetworkSettingsAppliedSnapshot(
        GenesisNetworkBasicSettings settings,
        GenesisRelayNetworkStatusSnapshot runtimeStatus)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(runtimeStatus);
        Settings = settings;
        RuntimeStatus = runtimeStatus;
    }

    public GenesisNetworkBasicSettings Settings { get; }

    public GenesisRelayNetworkStatusSnapshot RuntimeStatus { get; }
}

internal enum GenesisNetworkSettingsApplyOutcome
{
    Applied,
    Rejected,
    Failed,
    Cancelled,
}

internal interface IGenesisNetworkSettingsController
{
    GenesisNetworkSettingsAppliedSnapshot Snapshot { get; }

    ValueTask<GenesisNetworkSettingsApplyOutcome> ApplyAsync(
        GenesisNetworkBasicSettings settings,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Framework-neutral basic Network / Relay settings state. Draft values are kept
/// separate from the controller's applied snapshot so the GUI never reports an
/// edited textbox or checkbox as live configuration before a successful apply.
/// </summary>
internal sealed class GenesisNetworkSettingsViewModel : INotifyPropertyChanged
{
    private const string InvalidPortMessage = "Enter a valid port from 1 to 65535.";
    private const string SharedListenerUnavailableMessage =
        "Application-wide listener configuration is not available.";
    private const string Ipv6UnavailableMessage = "IPv6 listener support is not available.";
    private const string PortMappingUnavailableMessage =
        "Automatic port mapping is not available.";
    private const string LocalRelayPolicyUnavailableMessage =
        "Local relay policy cannot be changed by this backend.";
    private const string GlobalRelayPolicyUnavailableMessage =
        "Internet relay policy cannot be changed by this backend.";
    private const string ApplyRejectedMessage = "Network settings were rejected.";
    private const string ApplyFailedMessage = "Network settings failed.";
    private const string StatusRefreshFailedMessage = "Network status refresh failed.";

    private readonly IGenesisNetworkSettingsController _controller;
    private GenesisNetworkSettingsAppliedSnapshot _applied;
    private int _port;
    private bool _ipv6Enabled;
    private bool _automaticPortMappingEnabled;
    private bool _allowLocalRelay;
    private bool _allowGlobalRelay;
    private bool _isApplying;
    private bool _isRuntimeStatusCurrent = true;
    private string? _errorMessage;

    public GenesisNetworkSettingsViewModel(
        IGenesisNetworkSettingsController controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller = controller;
        _applied = controller.Snapshot;
        ArgumentNullException.ThrowIfNull(_applied);
        CopyAppliedSettingsToDraft();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public GenesisRelayNetworkStatusSnapshot RuntimeStatus =>
        _applied.RuntimeStatus;

    public bool IsRuntimeStatusCurrent => _isRuntimeStatusCurrent;

    public GenesisRelayNetworkCapabilities Capabilities =>
        _isRuntimeStatusCurrent
            ? RuntimeStatus.Capabilities
            : GenesisRelayNetworkCapabilities.None;

    public int Port
    {
        get => _port;
        set => SetDraft(ref _port, value);
    }

    public bool Ipv6Enabled
    {
        get => _ipv6Enabled;
        set => SetDraft(ref _ipv6Enabled, value);
    }

    public bool AutomaticPortMappingEnabled
    {
        get => _automaticPortMappingEnabled;
        set => SetDraft(ref _automaticPortMappingEnabled, value);
    }

    public bool AllowLocalRelay
    {
        get => _allowLocalRelay;
        set => SetDraft(ref _allowLocalRelay, value);
    }

    public bool AllowGlobalRelay
    {
        get => _allowGlobalRelay;
        set => SetDraft(ref _allowGlobalRelay, value);
    }

    public bool IsApplying => _isApplying;

    public string? ErrorMessage => _errorMessage;

    public bool IsDirty =>
        _port != _applied.Settings.Port ||
        _ipv6Enabled != _applied.Settings.Ipv6Enabled ||
        _automaticPortMappingEnabled !=
            _applied.Settings.AutomaticPortMappingEnabled ||
        _allowLocalRelay != _applied.Settings.AllowLocalRelay ||
        _allowGlobalRelay != _applied.Settings.AllowGlobalRelay;

    public bool CanEditPort =>
        !_isApplying && Capabilities.SupportsSharedListener;

    public bool CanEditIpv6 =>
        !_isApplying &&
        Capabilities.SupportsSharedListener &&
        Capabilities.SupportsIpv6;

    public bool CanEditAutomaticPortMapping =>
        !_isApplying && Capabilities.SupportsAutomaticPortMapping;

    public bool CanEditLocalRelay =>
        !_isApplying && Capabilities.SupportsLocalRelayPolicy;

    public bool CanEditGlobalRelay =>
        !_isApplying && Capabilities.SupportsGlobalRelayPolicy;

    public bool CanApply =>
        !_isApplying &&
        IsDirty &&
        GetValidationError() is null;

    public void Revert()
    {
        if (_isApplying)
        {
            return;
        }

        CopyAppliedSettingsToDraft();
        SetError(null);
        NotifyDraftAndCommandState();
    }

    public void RefreshStatus()
    {
        if (_isApplying)
        {
            return;
        }

        try
        {
            SetAppliedSnapshot(_controller.Snapshot, resetDraft: false);
            SetError(null);
        }
        catch
        {
            SetRuntimeStatusCurrent(false);
            SetError(StatusRefreshFailedMessage);
        }
    }

    public async ValueTask ApplyAsync(
        CancellationToken cancellationToken = default)
    {
        if (_isApplying || !IsDirty)
        {
            return;
        }

        var validationError = GetValidationError();
        if (validationError is not null)
        {
            SetError(validationError);
            NotifyDraftAndCommandState();
            return;
        }

        GenesisNetworkBasicSettings requested;
        try
        {
            requested = new GenesisNetworkBasicSettings(
                _port,
                _ipv6Enabled,
                _automaticPortMappingEnabled,
                _allowLocalRelay,
                _allowGlobalRelay);
        }
        catch (ArgumentException)
        {
            SetError(InvalidPortMessage);
            NotifyDraftAndCommandState();
            return;
        }

        SetApplying(true);
        SetError(null);
        try
        {
            var outcome = await _controller.ApplyAsync(
                requested,
                cancellationToken).ConfigureAwait(false);

            switch (outcome)
            {
                case GenesisNetworkSettingsApplyOutcome.Applied:
                    SetAppliedSnapshot(_controller.Snapshot, resetDraft: true);
                    break;
                case GenesisNetworkSettingsApplyOutcome.Rejected:
                    SetAppliedSnapshot(_controller.Snapshot, resetDraft: false);
                    SetError(ApplyRejectedMessage);
                    break;
                case GenesisNetworkSettingsApplyOutcome.Failed:
                    SetAppliedSnapshot(_controller.Snapshot, resetDraft: false);
                    SetError(ApplyFailedMessage);
                    break;
                case GenesisNetworkSettingsApplyOutcome.Cancelled:
                    SetAppliedSnapshot(_controller.Snapshot, resetDraft: false);
                    break;
                default:
                    SetRuntimeStatusCurrent(false);
                    SetError(ApplyFailedMessage);
                    break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetRuntimeStatusCurrent(false);
            SetError(null);
        }
        catch
        {
            SetRuntimeStatusCurrent(false);
            SetError(ApplyFailedMessage);
        }
        finally
        {
            SetApplying(false);
            NotifyDraftAndCommandState();
        }
    }

    private string? GetValidationError()
    {
        if (_port is < 1 or > 65535)
        {
            return InvalidPortMessage;
        }

        var applied = _applied.Settings;
        var capabilities = Capabilities;

        if (_port != applied.Port && !capabilities.SupportsSharedListener)
        {
            return SharedListenerUnavailableMessage;
        }

        if (_ipv6Enabled != applied.Ipv6Enabled &&
            (!capabilities.SupportsSharedListener || !capabilities.SupportsIpv6))
        {
            return Ipv6UnavailableMessage;
        }

        if (_automaticPortMappingEnabled !=
                applied.AutomaticPortMappingEnabled &&
            !capabilities.SupportsAutomaticPortMapping)
        {
            return PortMappingUnavailableMessage;
        }

        if (_allowLocalRelay != applied.AllowLocalRelay &&
            !capabilities.SupportsLocalRelayPolicy)
        {
            return LocalRelayPolicyUnavailableMessage;
        }

        if (_allowGlobalRelay != applied.AllowGlobalRelay &&
            !capabilities.SupportsGlobalRelayPolicy)
        {
            return GlobalRelayPolicyUnavailableMessage;
        }

        return null;
    }

    private void SetAppliedSnapshot(
        GenesisNetworkSettingsAppliedSnapshot snapshot,
        bool resetDraft)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _applied = snapshot;
        SetRuntimeStatusCurrent(true);
        if (resetDraft)
        {
            CopyAppliedSettingsToDraft();
        }

        OnPropertyChanged(nameof(RuntimeStatus));
        OnPropertyChanged(nameof(Capabilities));
        NotifyDraftAndCommandState();
    }

    private void CopyAppliedSettingsToDraft()
    {
        _port = _applied.Settings.Port;
        _ipv6Enabled = _applied.Settings.Ipv6Enabled;
        _automaticPortMappingEnabled =
            _applied.Settings.AutomaticPortMappingEnabled;
        _allowLocalRelay = _applied.Settings.AllowLocalRelay;
        _allowGlobalRelay = _applied.Settings.AllowGlobalRelay;
    }

    private void SetDraft<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        where T : IEquatable<T>
    {
        if (field.Equals(value))
        {
            return;
        }

        field = value;
        SetError(null);
        OnPropertyChanged(propertyName);
        NotifyCommandState();
    }

    private void SetApplying(bool value)
    {
        if (_isApplying == value)
        {
            return;
        }

        _isApplying = value;
        OnPropertyChanged(nameof(IsApplying));
        NotifyCommandState();
    }

    private void SetRuntimeStatusCurrent(bool value)
    {
        if (_isRuntimeStatusCurrent == value)
        {
            return;
        }

        _isRuntimeStatusCurrent = value;
        OnPropertyChanged(nameof(IsRuntimeStatusCurrent));
        OnPropertyChanged(nameof(RuntimeStatus));
        OnPropertyChanged(nameof(Capabilities));
        NotifyCommandState();
    }

    private void SetError(string? value)
    {
        if (string.Equals(_errorMessage, value, StringComparison.Ordinal))
        {
            return;
        }

        _errorMessage = value;
        OnPropertyChanged(nameof(ErrorMessage));
    }

    private void NotifyDraftAndCommandState()
    {
        OnPropertyChanged(nameof(Port));
        OnPropertyChanged(nameof(Ipv6Enabled));
        OnPropertyChanged(nameof(AutomaticPortMappingEnabled));
        OnPropertyChanged(nameof(AllowLocalRelay));
        OnPropertyChanged(nameof(AllowGlobalRelay));
        NotifyCommandState();
    }

    private void NotifyCommandState()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(CanEditPort));
        OnPropertyChanged(nameof(CanEditIpv6));
        OnPropertyChanged(nameof(CanEditAutomaticPortMapping));
        OnPropertyChanged(nameof(CanEditLocalRelay));
        OnPropertyChanged(nameof(CanEditGlobalRelay));
        OnPropertyChanged(nameof(CanApply));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
