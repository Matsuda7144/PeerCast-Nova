using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using PeerCast.Nova.Foundation.Configuration;
using PeerCast.Nova.Network.Publishing;

namespace PeerCast.Nova.App;

internal sealed record PublishingChannelPreferences(string ChannelName, string Genre, string Description,
    string Comment, string ContactUrl, int? BitrateKbps)
{
    internal PublishingAnnounceOptions Announce { get; init; } = new();
    internal ObsPublishLanOptions Lan { get; init; } = new();
    internal ObsPublishWanOptions Wan { get; init; } = new();
    internal PublishingRelayMode RelayMode => Wan.Enabled ? PublishingRelayMode.Wan : Lan.Enabled ? PublishingRelayMode.Lan : PublishingRelayMode.Loopback;
    internal static PublishingChannelPreferences Default { get; } = new("Local publish", "", "", "", "", null);
    internal ObsPublishChannelOptions ToOptions() => new()
    {
        ChannelName = ChannelName,
        Genre = Genre,
        Description = Description,
        Comment = Comment,
        ContactUrl = ContactUrl,
        BitrateKbps = BitrateKbps,
        Lan = Lan,
        Wan = Wan,
        Announce = Announce,
    };
    internal PublishingChannelPreferences Validate()
    {
        Announce.Validate();
        if (Announce.Enabled && (!Wan.Enabled || Wan.AdvertisedPort == 0 || !PublishingIpv4.IsPublicAddress(Wan.AdvertisedAddress)))
            throw new ArgumentException("YP requires explicit WAN and a public endpoint.");
        Lan.Validate();
        Wan.Validate();
        if (Lan.Enabled && Wan.Enabled) throw new ObsPublishWanException(ObsPublishWanFailure.InvalidSettings);
        var metadata = ToOptions().CreateMetadata();
        return this with { ChannelName = metadata.Name! };
    }
}

internal sealed record PublishingSettingsSnapshot(long SettingsRevision, PublishingChannelPreferences Values,
    ImmutableDictionary<string, JsonElement> Extensions)
{
    internal static PublishingSettingsSnapshot Default { get; } = new(0, PublishingChannelPreferences.Default,
        ImmutableDictionary<string, JsonElement>.Empty);
}

internal sealed record PublishingSettingsInput(string ChannelName, string Genre, string Description,
    string Comment, string ContactUrl, string BitrateKbps)
{
    internal bool AnnounceEnabled { get; init; }
    internal string AnnounceName { get; init; } = "";
    internal string AnnounceEndpoint { get; init; } = "";
    internal bool LanEnabled { get; init; }
    internal string LanInterfaceId { get; init; } = "";
    internal string LanAddress { get; init; } = "";
    internal string LanPort { get; init; } = "";
    internal bool WanEnabled { get; init; }
    internal string WanInterfaceId { get; init; } = "";
    internal string WanAddress { get; init; } = "";
    internal string WanPort { get; init; } = "";
    internal string AdvertisedAddress { get; init; } = "";
    internal string AdvertisedPort { get; init; } = "";
    internal IReadOnlyList<PublishingAnnounceTarget> AdditionalAnnounceTargets { get; init; } = [];

    public bool Equals(PublishingSettingsInput? other)
    {
        if (ReferenceEquals(this, other)) return true;
        if (other is null) return false;
        return string.Equals(ChannelName, other.ChannelName, StringComparison.Ordinal) &&
            string.Equals(Genre, other.Genre, StringComparison.Ordinal) &&
            string.Equals(Description, other.Description, StringComparison.Ordinal) &&
            string.Equals(Comment, other.Comment, StringComparison.Ordinal) &&
            string.Equals(ContactUrl, other.ContactUrl, StringComparison.Ordinal) &&
            string.Equals(BitrateKbps, other.BitrateKbps, StringComparison.Ordinal) &&
            AnnounceEnabled == other.AnnounceEnabled &&
            string.Equals(AnnounceName, other.AnnounceName, StringComparison.Ordinal) &&
            string.Equals(AnnounceEndpoint, other.AnnounceEndpoint, StringComparison.Ordinal) &&
            AdditionalAnnounceTargets.SequenceEqual(other.AdditionalAnnounceTargets) &&
            LanEnabled == other.LanEnabled &&
            string.Equals(LanInterfaceId, other.LanInterfaceId, StringComparison.Ordinal) &&
            string.Equals(LanAddress, other.LanAddress, StringComparison.Ordinal) &&
            string.Equals(LanPort, other.LanPort, StringComparison.Ordinal) &&
            WanEnabled == other.WanEnabled &&
            string.Equals(WanInterfaceId, other.WanInterfaceId, StringComparison.Ordinal) &&
            string.Equals(WanAddress, other.WanAddress, StringComparison.Ordinal) &&
            string.Equals(WanPort, other.WanPort, StringComparison.Ordinal) &&
            string.Equals(AdvertisedAddress, other.AdvertisedAddress, StringComparison.Ordinal) &&
            string.Equals(AdvertisedPort, other.AdvertisedPort, StringComparison.Ordinal);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ChannelName, StringComparer.Ordinal);
        hash.Add(Genre, StringComparer.Ordinal);
        hash.Add(Description, StringComparer.Ordinal);
        hash.Add(Comment, StringComparer.Ordinal);
        hash.Add(ContactUrl, StringComparer.Ordinal);
        hash.Add(BitrateKbps, StringComparer.Ordinal);
        hash.Add(AnnounceEnabled);
        hash.Add(AnnounceName, StringComparer.Ordinal);
        hash.Add(AnnounceEndpoint, StringComparer.Ordinal);
        foreach (var target in AdditionalAnnounceTargets) hash.Add(target);
        hash.Add(LanEnabled);
        hash.Add(LanInterfaceId, StringComparer.Ordinal);
        hash.Add(LanAddress, StringComparer.Ordinal);
        hash.Add(LanPort, StringComparer.Ordinal);
        hash.Add(WanEnabled);
        hash.Add(WanInterfaceId, StringComparer.Ordinal);
        hash.Add(WanAddress, StringComparer.Ordinal);
        hash.Add(WanPort, StringComparer.Ordinal);
        hash.Add(AdvertisedAddress, StringComparer.Ordinal);
        hash.Add(AdvertisedPort, StringComparer.Ordinal);
        return hash.ToHashCode();
    }

    internal static PublishingSettingsInput From(PublishingChannelPreferences value) => new(value.ChannelName,
        value.Genre, value.Description, value.Comment, value.ContactUrl, value.BitrateKbps?.ToString(CultureInfo.InvariantCulture) ?? "")
    {
        AnnounceEnabled = value.Announce.Enabled,
        AnnounceName = value.Announce.Name,
        AnnounceEndpoint = value.Announce.Endpoint,
        AdditionalAnnounceTargets = value.Announce.AdditionalTargets?.ToArray() ?? [],
        LanEnabled = value.Lan.Enabled,
        LanInterfaceId = value.Lan.InterfaceId,
        LanAddress = value.Lan.Address,
        LanPort = value.Lan.Port == 0 ? "" : value.Lan.Port.ToString(CultureInfo.InvariantCulture),
        WanEnabled = value.Wan.Enabled,
        WanInterfaceId = value.Wan.InterfaceId,
        WanAddress = value.Wan.Address,
        WanPort = value.Wan.Port == 0 ? "" : value.Wan.Port.ToString(CultureInfo.InvariantCulture),
        AdvertisedAddress = value.Wan.AdvertisedAddress,
        AdvertisedPort = value.Wan.AdvertisedPort == 0 ? "" : value.Wan.AdvertisedPort.ToString(CultureInfo.InvariantCulture)
    };
    internal PublishingChannelPreferences Validate()
    {
        int? bitrate = null;
        if (BitrateKbps.Length != 0)
        {
            if (!int.TryParse(BitrateKbps, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1)
                throw new ArgumentException("1～2147483647の整数kbps、または空欄で入力してください。", nameof(BitrateKbps));
            bitrate = number;
        }
        var port = 0;
        if (LanPort.Length != 0 && (!int.TryParse(LanPort, NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535))
            throw new ArgumentException("LANポートは1～65535の整数で入力してください。", nameof(LanPort));
        var lan = new ObsPublishLanOptions(LanEnabled, LanInterfaceId, LanAddress, port);
        try { lan.Validate(); }
        catch (ObsPublishLanException) { throw new ArgumentException("LANのNIC/IPとポートを選択してください。", nameof(LanAddress)); }
        var wan = new ObsPublishWanOptions(WanEnabled, WanInterfaceId, WanAddress, ParsePort(WanPort, nameof(WanPort)),
            AdvertisedAddress, ParsePort(AdvertisedPort, nameof(AdvertisedPort)));
        try { wan.Validate(); if (lan.Enabled && wan.Enabled) throw new ObsPublishWanException(ObsPublishWanFailure.InvalidSettings); }
        catch (ObsPublishWanException) { throw new ArgumentException("WANのNIC/IP・内部ポート・公開IPv4/外部ポートを確認してください。", nameof(WanAddress)); }
        var announce = new PublishingAnnounceOptions(AnnounceEnabled, AnnounceName, AnnounceEndpoint)
        {
            AdditionalTargets = AdditionalAnnounceTargets.Count == 0 ? null : AdditionalAnnounceTargets.ToArray(),
        };
        return new PublishingChannelPreferences(ChannelName, Genre, Description, Comment, ContactUrl, bitrate) { Lan = lan, Wan = wan, Announce = announce }.Validate();
    }
    private static int ParsePort(string text, string field)
    {
        if (text.Length == 0) return 0;
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
            throw new ArgumentException("ポートは1～65535の整数で入力してください。", field);
        return port;
    }
}

internal interface IPublishingPreferencesStore
{
    ValueTask<PublishingSettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default);
    ValueTask SaveAsync(PublishingSettingsSnapshot snapshot, CancellationToken cancellationToken = default);
}

internal sealed class JsonPublishingPreferencesStore(string path, Action? beforeReplace = null) : IPublishingPreferencesStore
{
    internal static JsonPublishingPreferencesStore CreateDefault() => new(
        NovaAppDataPaths.GetPrimaryFilePath("publishing-preferences.json"));
    internal const int MaximumBytes = 64 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, MaxDepth = 16, AllowDuplicateProperties = false };
    private readonly string _path = Path.GetFullPath(path);

    private sealed class Document
    {
        public int SchemaVersion { get; init; }
        public long SettingsRevision { get; init; }
        public string ChannelName { get; init; } = "Local publish";
        public string Genre { get; init; } = "";
        public string Description { get; init; } = "";
        public string Comment { get; init; } = "";
        public string ContactUrl { get; init; } = "";
        public int? BitrateKbps { get; init; }
        public PublishingAnnounceOptions? Announce { get; init; }
        public ObsPublishLanOptions? Lan { get; init; }
        public ObsPublishWanOptions? Wan { get; init; }
        public PublishingRelayMode? RelayMode { get; init; }
        [JsonExtensionData] public Dictionary<string, JsonElement>? Extra { get; init; }
    }

    public async ValueTask<PublishingSettingsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        FileStream file;
        try { file = new(_path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous); }
        catch (FileNotFoundException) { return PublishingSettingsSnapshot.Default; }
        catch (DirectoryNotFoundException) { return PublishingSettingsSnapshot.Default; }
        await using (file.ConfigureAwait(false))
        {
            var buffer = new byte[MaximumBytes + 1];
            var count = 0;
            while (count < buffer.Length)
            {
                var read = await file.ReadAsync(buffer.AsMemory(count), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                count += read;
            }
            if (count > MaximumBytes) throw new InvalidDataException("Publishing settings exceed the file limit.");
            var document = JsonSerializer.Deserialize<Document>(buffer.AsSpan(0, count), JsonOptions)
                ?? throw new InvalidDataException("Publishing settings must contain an object.");
            if (document.SchemaVersion is not (1 or 2 or 3 or 4) || document.SettingsRevision < 0 || document.SettingsRevision == long.MaxValue)
                throw new InvalidDataException("Unsupported publishing settings version or revision.");
            var values = new PublishingChannelPreferences(document.ChannelName, document.Genre, document.Description,
                document.Comment, document.ContactUrl, document.BitrateKbps)
            {
                Announce = document.SchemaVersion < 4 ? new() : document.Announce ?? throw new InvalidDataException("YP configuration missing."),
                Lan = document.SchemaVersion == 1 ? new() : document.Lan ?? throw new InvalidDataException("LAN configuration is missing."),
                Wan = document.SchemaVersion < 3 ? new() : document.Wan ?? throw new InvalidDataException("WAN configuration is missing."),
            };
            values = values.Validate();
            if (document.SchemaVersion >= 3 && (document.RelayMode is null || !Enum.IsDefined(document.RelayMode.Value) || document.RelayMode != values.RelayMode))
                throw new InvalidDataException("Publishing relay mode is missing or inconsistent.");
            var extensions = (document.Extra ?? []).ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Clone());
            return new(document.SettingsRevision, values, extensions);
        }
    }

    public async ValueTask SaveAsync(PublishingSettingsSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var values = snapshot.Values.Validate();
        if (snapshot.SettingsRevision < 0 || snapshot.SettingsRevision == long.MaxValue)
            throw new InvalidDataException("Publishing settings revision is out of range.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new Document
        {
            SchemaVersion = 4,
            SettingsRevision = snapshot.SettingsRevision,
            ChannelName = values.ChannelName,
            Genre = values.Genre,
            Description = values.Description,
            Comment = values.Comment,
            ContactUrl = values.ContactUrl,
            BitrateKbps = values.BitrateKbps,
            Lan = values.Lan,
            Wan = values.Wan,
            Announce = values.Announce,
            RelayMode = values.RelayMode,
            Extra = snapshot.Extensions.ToDictionary(pair => pair.Key, pair => pair.Value.Clone()),
        }, JsonOptions);
        if (bytes.Length > MaximumBytes) throw new InvalidDataException("Publishing settings exceed the file limit.");
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(_path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await file.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await file.FlushAsync(cancellationToken).ConfigureAwait(false);
                file.Flush(flushToDisk: true);
            }
            beforeReplace?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, _path, overwrite: true);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}

internal sealed record PublishingSettingsSaveResult(bool Saved, string Message, string? Field = null);

/// <summary>Application-owned Saved/Applied snapshots; no runtime callbacks or I/O under its gate.</summary>
internal sealed class PublishingSettingsController(IPublishingPreferencesStore store, IObsPublishLanInterfaces? interfaces = null) : IAsyncDisposable
{
    internal IObsPublishLanInterfaces Interfaces { get; } = interfaces ?? SystemObsPublishLanInterfaces.Instance;
    private readonly object _gate = new();
    private PublishingSettingsSnapshot _saved = PublishingSettingsSnapshot.Default;
    private PublishingSettingsSnapshot? _applied;
    private bool _trusted, _closing;
    private PublishingSettingsInput? _draft;
    private long _editorToken;
    private bool _editorOpen;
    private int _saving;
    private Task _pending = Task.CompletedTask;
    internal event Action? Changed;
    internal event Action<PublishingSettingsSnapshot>? SavedUpdated;
    internal PublishingSettingsSnapshot Saved { get { lock (_gate) return _saved; } }
    internal PublishingSettingsSnapshot? Applied { get { lock (_gate) return _applied; } }
    internal bool LoadFailed { get { lock (_gate) return !_trusted; } }
    private bool CanStartLocked => !_closing && _trusted && _saving == 0 &&
        (!_editorOpen || _draft == PublishingSettingsInput.From(_saved.Values));
    internal bool CanStart { get { lock (_gate) return CanStartLocked; } }
    internal bool IsSaving { get { lock (_gate) return _saving != 0; } }
    internal const string SaveOrCancel = "設定を保存またはキャンセルしてください。保存値は次の配信開始から適用されます。";
    internal const string LoadWarning = "配信設定を読み込めません。開始できません。内容を確認して保存すると既存ファイルを置き換えます。";

    internal async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var loaded = await Task.Run(async () => await store.LoadAsync(cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
            lock (_gate) { _saved = loaded; _trusted = true; }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { lock (_gate) { _trusted = false; } }
        Changed?.Invoke();
    }

    internal bool TryCaptureForStart(out PublishingSettingsSnapshot snapshot)
    {
        lock (_gate) { snapshot = _saved; return CanStartLocked; }
    }
    // Called at Start admission; never call subscribers under the owner gate.
    internal void MarkApplied(PublishingSettingsSnapshot snapshot) { lock (_gate) _applied = snapshot; }
    internal (long Token, PublishingSettingsInput Input) OpenEditor()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closing, this);
            if (_editorOpen) throw new InvalidOperationException("Publishing settings editor is already open.");
            _editorOpen = true;
            _draft = PublishingSettingsInput.From(_saved.Values);
            return (++_editorToken, _draft);
        }
    }
    internal void SetDraft(long token, PublishingSettingsInput draft)
    {
        lock (_gate) { if (!_editorOpen || token != _editorToken || _closing) return; _draft = draft; }
        Changed?.Invoke();
    }
    internal void CloseEditor(long token)
    {
        lock (_gate) { if (token != _editorToken) return; _editorOpen = false; _draft = null; }
        Changed?.Invoke();
    }

    internal async Task<PublishingSettingsSaveResult> SaveAsync(PublishingSettingsInput input, CancellationToken cancellationToken = default)
    {
        PublishingChannelPreferences next;
        try { next = input.Validate(); }
        catch (ArgumentException exception) { return new(false, exception.Message.Split(" (", StringSplitOptions.None)[0], exception.ParamName); }
        Task<PublishingSettingsSaveResult> pending;
        lock (_gate)
        {
            if (_closing) return new(false, "終了処理中のため保存できません。");
            var previous = _pending;
            _saving++;
            pending = Task.Run(async () =>
            {
                try
                {
                    await previous.ConfigureAwait(false);
                    PublishingSettingsSnapshot snapshot;
                    lock (_gate) snapshot = new(checked(_saved.SettingsRevision + 1), next, _saved.Extensions);
                    await store.SaveAsync(snapshot, cancellationToken).ConfigureAwait(false);
                    lock (_gate) { _saved = snapshot; _trusted = true; }
                    try { SavedUpdated?.Invoke(snapshot); } catch { }
                    return new PublishingSettingsSaveResult(true, "保存しました。次の配信開始から適用されます。");
                }
                catch (OperationCanceledException) { return new(false, "保存を中止しました。保存値は変更されていません。"); }
                catch { return new(false, "保存できませんでした。保存場所とアクセス権を確認してください。保存値は変更されていません。"); }
                finally { lock (_gate) _saving--; Changed?.Invoke(); }
            }, CancellationToken.None);
            _pending = pending;
        }
        Changed?.Invoke();
        return await pending.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        Task pending;
        lock (_gate) { _closing = true; _editorOpen = false; _draft = null; pending = _pending; }
        await pending.ConfigureAwait(false);
    }
}

internal sealed class PublishingSettingsEditor : IDisposable
{
    private readonly object _gate = new();
    private readonly PublishingSettingsController _settings;
    private readonly long _token;
    private bool _closed;
    private PublishingSettingsInput _input;
    private PublishingSettingsSaveResult _result;
    internal PublishingSettingsEditor(PublishingSettingsController settings)
    {
        _settings = settings;
        var opened = settings.OpenEditor();
        _token = opened.Token;
        _input = opened.Input;
        _result = new(false, settings.LoadFailed ? PublishingSettingsController.LoadWarning : "保存した設定は次の配信開始から適用されます。");
    }
    internal PublishingSettingsInput Input { get { lock (_gate) return _input; } }
    internal PublishingSettingsSaveResult Result { get { lock (_gate) return _result; } }
    internal void Edit(PublishingSettingsInput input)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_closed, this);
            _input = input;
            _settings.SetDraft(_token, _input);
        }
    }
    internal async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        PublishingSettingsInput input;
        lock (_gate) { ObjectDisposedException.ThrowIf(_closed, this); input = _input; }
        var result = await _settings.SaveAsync(input, cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            if (_closed) return;
            _result = result;
            if (result.Saved && _input == input) _input = PublishingSettingsInput.From(input.Validate());
            _settings.SetDraft(_token, _input);
        }
    }
    public void Dispose()
    {
        lock (_gate) { if (!_closed) { _closed = true; _settings.CloseEditor(_token); } }
    }
}
