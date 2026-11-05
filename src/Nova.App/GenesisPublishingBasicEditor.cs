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
