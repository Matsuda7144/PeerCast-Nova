using System.Globalization;
using PeerCast.Nova.Foundation.Configuration;

namespace PeerCast.Nova.App;

/// <summary>Saved next-start configuration. Null limits mean unlimited; zero is an explicit limit.</summary>
internal sealed record GenesisGuiNextConnectionPreferences
{
    public int SchemaVersion { get; init; } = 1;
    public int ListenPort { get; init; } = new NovaOptions().ListenPort;
    public bool UseIpv6 { get; init; }
    public int? TotalUpstreamIpv4Kbps { get; init; }
    public int? TotalUpstreamIpv6Kbps { get; init; }
    public int? MaxRelaysTotal { get; init; }
    public int? MaxRelaysPerBroadcastChannel { get; init; }
    public int? MaxRelaysPerViewingChannel { get; init; }

    public static GenesisGuiNextConnectionPreferences Default { get; } = new();

    internal void Validate()
    {
        if (SchemaVersion != 1)
            throw new InvalidDataException("Unsupported connection settings schema.");
        GenesisNetworkListenerSettings.ValidatePort(ListenPort, nameof(ListenPort));
        if (TotalUpstreamIpv4Kbps < 0 || TotalUpstreamIpv6Kbps < 0 || MaxRelaysTotal < 0 ||
            MaxRelaysPerBroadcastChannel < 0 || MaxRelaysPerViewingChannel < 0)
            throw new InvalidDataException("Connection limits cannot be negative.");
    }
}

internal sealed record GenesisGuiNextConnectionInput(
    string ListenPort,
    bool UseIpv6,
    string TotalUpstreamIpv4Kbps,
    string TotalUpstreamIpv6Kbps,
    string MaxRelaysTotal,
    string MaxRelaysPerBroadcastChannel,
    string MaxRelaysPerViewingChannel)
{
    internal static GenesisGuiNextConnectionInput From(GenesisGuiNextConnectionPreferences value) => new(
        value.ListenPort.ToString(CultureInfo.InvariantCulture), value.UseIpv6,
        Format(value.TotalUpstreamIpv4Kbps), Format(value.TotalUpstreamIpv6Kbps),
        Format(value.MaxRelaysTotal), Format(value.MaxRelaysPerBroadcastChannel),
        Format(value.MaxRelaysPerViewingChannel));

    private static string Format(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    internal bool TryValidate(out GenesisGuiNextConnectionPreferences? value, out string? error)
    {
        value = null;
        if (!TryNumber(ListenPort, out var port) || port is < 1 or > 65535)
        {
            error = "ポート番号は1～65535の整数で入力してください。";
            return false;
        }

        var texts = new[] { TotalUpstreamIpv4Kbps, TotalUpstreamIpv6Kbps, MaxRelaysTotal,
            MaxRelaysPerBroadcastChannel, MaxRelaysPerViewingChannel };
        var labels = new[] { "合計(IPv4) kbps", "合計(IPv6) kbps", "合計リレー数",
            "チャンネル毎(配信時)", "チャンネル毎(視聴時)" };
        var limits = new int?[texts.Length];
        for (var index = 0; index < texts.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(texts[index])) continue;
            if (!TryNumber(texts[index], out var limit))
            {
                error = $"{labels[index]}は0～2147483647の整数、または空欄（制限無し）で入力してください。";
                return false;
            }
            limits[index] = limit;
        }

        value = new()
        {
            ListenPort = port,
            UseIpv6 = UseIpv6,
            TotalUpstreamIpv4Kbps = limits[0],
            TotalUpstreamIpv6Kbps = limits[1],
            MaxRelaysTotal = limits[2],
            MaxRelaysPerBroadcastChannel = limits[3],
            MaxRelaysPerViewingChannel = limits[4],
        };
        error = null;
        return true;
    }

    private static bool TryNumber(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
}

internal sealed record GenesisGuiNextConnectionSaveResult(bool Saved, string Message);

/// <summary>
/// One application-owned configuration snapshot captured at startup, plus a saved
/// next-start snapshot. Saving never mutates startup state or invokes networking.
/// No passive shutdown save can replace a file that failed to load.
/// </summary>
internal sealed class GenesisGuiNextConnectionSettings : IAsyncDisposable
{
    internal const string NextStartNotice =
        "保存値は次回起動時に読み込みます。LAN配信のリレー数上限に適用。待受ポート・IPv6への適用は後続対応。IPv4帯域数値設定時はLAN配信開始不可。";
    internal const string LoadFailureMessage =
        "設定を読み込めませんでした。今回は既定値で開始します。保存すると既存の設定を置き換えます。";
    private readonly IGenesisGuiNextConnectionPreferencesStore _store;
    private readonly object _saveGate = new();
    private Task _pendingSave = Task.CompletedTask;
    private bool _closing;
    private int _loadFailed;
    private GenesisGuiNextConnectionPreferences _saved;

    internal GenesisGuiNextConnectionSettings(GenesisGuiNextConnectionPreferences startup,
        IGenesisGuiNextConnectionPreferencesStore store, bool loadFailed = false)
    {
        ArgumentNullException.ThrowIfNull(startup);
        ArgumentNullException.ThrowIfNull(store);
        startup.Validate();
        Startup = startup;
        _saved = startup;
        _store = store;
        _loadFailed = loadFailed ? 1 : 0;
    }

    public GenesisGuiNextConnectionPreferences Startup { get; }
    public GenesisGuiNextConnectionPreferences Saved => Volatile.Read(ref _saved);
    public bool LoadFailed => Volatile.Read(ref _loadFailed) != 0;
    public bool HasPendingRestart => Saved != Startup;

    internal async ValueTask<GenesisGuiNextConnectionSaveResult> SaveAsync(
        GenesisGuiNextConnectionInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        if (!input.TryValidate(out var next, out var error))
            return new(false, error!);

        Task<GenesisGuiNextConnectionSaveResult> pending;
        lock (_saveGate)
        {
            if (_closing) return new(false, "終了処理中のため保存できません。設定は変更されていません。");
            pending = PersistAfterAsync(_pendingSave, next!, cancellationToken);
            _pendingSave = pending;
        }
        return await pending.ConfigureAwait(false);
    }

    private async Task<GenesisGuiNextConnectionSaveResult> PersistAfterAsync(Task previous,
        GenesisGuiNextConnectionPreferences next, CancellationToken cancellationToken)
    {
        try { await previous.ConfigureAwait(false); }
        catch (OperationCanceledException) { /* An earlier canceled save does not cancel the next request. */ }
        try
        {
            await _store.SaveAsync(next!, cancellationToken).ConfigureAwait(false);
            Volatile.Write(ref _saved, next!);
            Volatile.Write(ref _loadFailed, 0);
            return new(true, "設定を保存しました。次回起動時に読み込みます。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return new(false, "設定を保存できませんでした。保存場所・アクセス権を確認してください。設定は変更されていません。");
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task pending;
        lock (_saveGate)
        {
            _closing = true;
            pending = _pendingSave;
        }
        try { await pending.ConfigureAwait(false); }
        catch (OperationCanceledException) { /* Explicit cancellation cannot block shutdown. */ }
    }
}

/// <summary>Managed editor keeps raw input after rejection and replaces old feedback on success.</summary>
internal sealed class GenesisGuiNextConnectionEditor
{
    private readonly GenesisGuiNextConnectionSettings _settings;
    internal GenesisGuiNextConnectionEditor(GenesisGuiNextConnectionSettings settings)
    {
        _settings = settings;
        Input = GenesisGuiNextConnectionInput.From(settings.Saved);
        Status = settings.LoadFailed ? GenesisGuiNextConnectionSettings.LoadFailureMessage : string.Empty;
    }

    internal GenesisGuiNextConnectionInput Input { get; set; }
    internal string Status { get; private set; }

    internal async ValueTask SaveAsync(CancellationToken cancellationToken = default)
    {
        var result = await _settings.SaveAsync(Input, cancellationToken).ConfigureAwait(false);
        Status = result.Message;
    }
}
