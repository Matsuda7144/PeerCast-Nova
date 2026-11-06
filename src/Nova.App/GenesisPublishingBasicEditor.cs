using PeerCast.Nova.Network.Publishing;

namespace PeerCast.Nova.App;

// Modern presentation owns one existing editor, never persistence or runtime application.
internal sealed class GenesisPublishingBasicEditor(PublishingSettingsController settings) : IDisposable
{
    private readonly object _gate = new();
    private PublishingSettingsEditor? _editor;
    private bool _saving, _released;
    internal bool Editing { get { lock (_gate) return _editor is not null; } }
    internal bool Saving { get { lock (_gate) return _saving; } }
    internal PublishingSettingsInput? Input { get { lock (_gate) return _editor?.Input; } }
    internal bool Dirty { get { lock (_gate) return _editor is not null && _editor.Input != PublishingSettingsInput.From(settings.Saved.Values); } }
    internal PublishingSettingsSaveResult Result { get; private set; } = new(false, "変更は次回の配信開始から適用されます。");

    internal bool Begin()
    {
        lock (_gate)
        {
            if (_released || _saving || _editor is not null) return false;
            if (settings.LoadFailed)
            {
                Result = new(false, "設定を読み込めません。「配信設定…」で内容を確認し、明示的に復旧してください。");
                return false;
            }
            try { _editor = new(settings); }
            catch (InvalidOperationException)
            {
                Result = new(false, "配信設定が別の画面で編集中です。先に保存またはキャンセルしてください。");
                return false;
            }
            Result = new(false, "編集中です。保存またはキャンセルしてください。変更は次回開始から適用されます。");
            return true;
        }
    }

    internal void Edit(string name, string genre, string description, string comment, string contact, string bitrate)
    {
        lock (_gate)
        {
            if (_released || _saving || _editor is null) return;
            _editor.Edit(_editor.Input with
            {
                ChannelName = name,
                Genre = genre,
                Description = description,
                Comment = comment,
                ContactUrl = contact,
                BitrateKbps = bitrate,
            }); // Preserve all advanced settings captured by this same editor.
        }
    }

    internal bool EditAdvanced(PublishingSettingsInput fields)
    {
        lock (_gate)
        {
            if (_released || _saving || _editor is null) return false;
            var current = _editor.Input;
            if (fields.AnnounceEnabled && !current.AnnounceEnabled && !fields.WanEnabled)
            {
                Result = new(false, "YP Announceの有効化には明示的なWAN設定が必要です。", nameof(fields.AnnounceEnabled));
                return false;
            }
            _editor.Edit(current with
            {
                LanEnabled = fields.LanEnabled,
                LanInterfaceId = fields.LanInterfaceId,
                LanAddress = fields.LanAddress,
                LanPort = fields.LanPort,
                WanEnabled = fields.WanEnabled,
                WanInterfaceId = fields.WanInterfaceId,
                WanAddress = fields.WanAddress,
                WanPort = fields.WanPort,
                AdvertisedAddress = fields.AdvertisedAddress,
                AdvertisedPort = fields.AdvertisedPort,
                AnnounceEnabled = fields.AnnounceEnabled,
                AnnounceName = fields.AnnounceName,
                AnnounceEndpoint = fields.AnnounceEndpoint,
            });
            Result = new(false, "基本・詳細は同じ未保存Draftです。保存後、次回開始から適用します。保存だけでは通信しません。");
            return true;
        }
    }

    internal IReadOnlyList<ObsPublishLanInterface> InterfaceChoices()
    {
        var input = Input;
        if (input is null) return [];
        var result = new List<ObsPublishLanInterface>();
        try
        {
            foreach (var group in settings.Interfaces.Read().GroupBy(n => n.Address))
            {
                if (group.Count() != 1) continue; // ambiguous addresses cannot identify a bind owner
                var nic = group.Single();
                if (!nic.IsUp) continue;
                if (input.WanEnabled) { if (PublishingIpv4.IsBindAddress(nic.Address)) result.Add(nic); }
                else
                {
                    try { _ = new ObsPublishLanBinding(nic); result.Add(nic); }
                    catch (ObsPublishLanException) { }
                }
            }
        }
        catch { result.Clear(); }
        var id = input.WanEnabled ? input.WanInterfaceId : input.LanInterfaceId;
        var address = input.WanEnabled ? input.WanAddress : input.LanAddress;
        if (address.Length > 0 && !result.Any(n => n.Id == id && n.Address == address))
            result.Add(new(id, "保存済み（利用不可・未確認）", false, address, "", 0));
        return result;
    }

    internal async Task SaveAsync()
    {
        PublishingSettingsEditor editor;
        lock (_gate)
        {
            if (_released || _saving || _editor is null) return;
            editor = _editor;
            _saving = true;
            Result = new(false, "保存しています…");
        }
        try
        {
            await editor.SaveAsync().ConfigureAwait(false);
            lock (_gate)
            {
                if (_released || !ReferenceEquals(_editor, editor)) return;
                Result = editor.Result;
                if (Result.Saved) { _editor.Dispose(); _editor = null; }
            }
        }
        catch
        {
            lock (_gate) if (!_released) Result = new(false, "保存の完了を確認できません。入力を保持しています。保存値を再確認してください。");
        }
        finally { lock (_gate) _saving = false; }
    }

    internal bool Cancel()
    {
        lock (_gate)
        {
            if (_saving || _released) return false;
            _editor?.Dispose(); _editor = null;
            Result = new(false, "編集をキャンセルしました。保存済み設定は変更していません。");
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _released = true;
            _editor?.Dispose(); _editor = null;
            // Root-owned settings retain and join any in-flight save at shutdown.
        }
    }
}
