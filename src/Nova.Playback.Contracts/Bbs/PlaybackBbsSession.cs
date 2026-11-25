namespace PeerCast.Nova.Playback.Bbs;

public sealed class PlaybackBbsSession : IAsyncDisposable
{
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(15);

    private readonly IPlaybackBbsClient _client;
    private readonly bool _ownsClient;
    private readonly TimeSpan _pollInterval;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly object _snapshotGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly List<BbsPost> _posts = [];
    private BbsTarget _target;
    private BbsFetchState _fetchState = BbsFetchState.Initial;
    private IReadOnlyList<BbsThreadSummary> _threads = [];
    private string _threadTitle = string.Empty;
    private string? _statusMessage;
    private bool _isLoading;
    private bool _initialPostsLoaded;
    private Task? _pollLoop;
    private int _disposed;

    public PlaybackBbsSession(
        BbsTarget initialTarget,
        IPlaybackBbsClient? client = null,
        TimeSpan? pollInterval = null)
    {
        _target = initialTarget;
        _client = client ?? new PlaybackBbsClient();
        _ownsClient = client is null;
        _pollInterval = pollInterval ?? DefaultPollInterval;
        Snapshot = BuildSnapshot();
    }

    public event Action<BbsSessionSnapshot>? StateChanged;

    public event Action<IReadOnlyList<BbsPost>>? NewPostsReceived;

    public BbsSessionSnapshot Snapshot { get; private set; }

    public void Start()
    {
        if (Volatile.Read(ref _disposed) != 0 || _pollLoop is not null)
        {
            return;
        }

        _pollLoop = RunPollingLoopAsync(_lifetime.Token);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _operationGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            await RefreshCoreAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task SelectThreadAsync(string threadKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(threadKey))
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _operationGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (string.Equals(_target.ThreadKey, threadKey, StringComparison.Ordinal))
            {
                await RefreshCoreAsync(linked.Token).ConfigureAwait(false);
                return;
            }

            _target = _target.WithThread(threadKey);
            _posts.Clear();
            _fetchState = BbsFetchState.Initial;
            _initialPostsLoaded = false;
            var known = _threads.FirstOrDefault(t => string.Equals(t.Key, threadKey, StringComparison.Ordinal));
            _threadTitle = known?.Title ?? string.Empty;
            _statusMessage = null;
            PublishSnapshot();
            await RefreshCoreAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<BbsPostResult> PostAsync(
        string name,
        string mail,
        string message,
        CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        await _operationGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            if (string.IsNullOrEmpty(_target.ThreadKey))
            {
                await ResolveActiveThreadAsync(linked.Token).ConfigureAwait(false);
                if (string.IsNullOrEmpty(_target.ThreadKey))
                {
                    return new BbsPostResult(false, "スレッドを取得できませんでした。");
                }
            }

            BbsPostResult result;
            try
            {
                result = await _client.PostAsync(_target, new BbsPostRequest(name, mail, message), linked.Token)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                return new BbsPostResult(false, "掲示板への接続に失敗しました。");
            }

            if (result.Succeeded)
            {
                _statusMessage = null;
                await RefreshCoreAsync(linked.Token).ConfigureAwait(false);
            }
            else if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
            {
                _statusMessage = result.ErrorMessage;
                PublishSnapshot();
            }

            return result;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();
        if (_pollLoop is not null)
        {
            try
            {
                await _pollLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _operationGate.Dispose();
        _lifetime.Dispose();
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    private async Task RunPollingLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
            using var timer = new PeriodicTimer(_pollInterval);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        _isLoading = true;
        PublishSnapshot();

        IReadOnlyList<BbsPost>? newlyArrived = null;
        try
        {
            if (string.IsNullOrEmpty(_target.ThreadKey) || _threads.Count == 0)
            {
                await ResolveActiveThreadAsync(cancellationToken).ConfigureAwait(false);
            }

            if (string.IsNullOrEmpty(_target.ThreadKey))
            {
                _statusMessage = "スレッドが見つかりません";
                return;
            }

            var fetch = await _client.FetchPostsAsync(_target, _fetchState, cancellationToken).ConfigureAwait(false);
            _fetchState = fetch.NextState;
            if (!string.IsNullOrWhiteSpace(fetch.ThreadTitle))
            {
                _threadTitle = fetch.ThreadTitle;
            }

            if (fetch.ResetExisting)
            {
                var previousCount = _posts.Count;
                _posts.Clear();
                _posts.AddRange(fetch.AddedPosts);
                if (_initialPostsLoaded && _posts.Count > previousCount)
                {
                    newlyArrived = _posts.Skip(previousCount).ToArray();
                }
            }
            else if (fetch.AddedPosts.Count > 0)
            {
                var existingMax = _posts.Count > 0 ? _posts[^1].Number : 0;
                var fresh = fetch.AddedPosts.Where(p => p.Number > existingMax).ToArray();
                if (fresh.Length > 0)
                {
                    _posts.AddRange(fresh);
                    if (_initialPostsLoaded)
                    {
                        newlyArrived = fresh;
                    }
                }
            }

            _initialPostsLoaded = true;
            _statusMessage = null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
            _statusMessage = "掲示板の取得に失敗しました";
        }
        finally
        {
            _isLoading = false;
            PublishSnapshot();
        }

        if (newlyArrived is { Count: > 0 })
        {
            NewPostsReceived?.Invoke(newlyArrived);
        }
    }

    private async Task ResolveActiveThreadAsync(CancellationToken cancellationToken)
    {
        var threads = await _client.GetThreadsAsync(_target, cancellationToken).ConfigureAwait(false);
        _threads = threads;
        if (threads.Count == 0)
        {
            return;
        }

        if (!string.IsNullOrEmpty(_target.ThreadKey))
        {
            var matched = threads.FirstOrDefault(t => string.Equals(t.Key, _target.ThreadKey, StringComparison.Ordinal));
            if (matched is not null && string.IsNullOrWhiteSpace(_threadTitle))
            {
                _threadTitle = matched.Title;
            }

            return;
        }

        var selected = threads.FirstOrDefault(static t => t.PostCount < 1000) ?? threads[0];
        _target = _target.WithThread(selected.Key);
        _threadTitle = selected.Title;
    }

    private void PublishSnapshot()
    {
        BbsSessionSnapshot next;
        lock (_snapshotGate)
        {
            next = BuildSnapshot();
            Snapshot = next;
        }

        StateChanged?.Invoke(next);
    }

    private BbsSessionSnapshot BuildSnapshot() =>
        new(
            _target,
            _target.ThreadKey,
            _threadTitle,
            _posts.Count > 0 ? _posts[^1].Number : 0,
            _threads,
            _posts.ToArray(),
            _statusMessage,
            _isLoading);
}
