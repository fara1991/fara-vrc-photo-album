using FaraVrcAlbum.Desktop.Mvvm;
using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Tools.Services;

namespace FaraVrcAlbum.Desktop.ViewModels;

/// <summary>
/// 公開の画面: 確認用（dev）と本番へ、アプリからサイトを公開する。それぞれ、今の記事の中身が公開済みかどうかを出す。
/// 本番は、確認用に今の内容を公開してからだけ（ボタンが押せない）。公開の前には必ず確認ダイアログ。
/// 公開は、記事の中身をサイトにするだけ。ワールドの画像・情報（VRChat から取る。1 件ごとに約 1 分）は、公開とは別のボタン（「サイトの画像情報を更新する」）で、事前に行う。
/// デモ（保存先に接続しない）では公開も取得もしない。
/// </summary>
public sealed class PublishViewModel : PageViewModel
{
    public const string DevUrl = "https://vrc-album-dev.fara-labs.com/";
    public const string ProductionUrl = "https://vrc-album.fara-labs.com/";
    private const int MaxLogLines = 400;

    private readonly IProcessRunner _runner;
    private readonly Func<bool, Action<string>, CancellationToken, Task> _worldFetch;
    private readonly object _logGate = new();
    private readonly List<string> _logLines = [];
    private bool _busy;
    private string _currentHash = "";
    private PublishRecord? _dev;
    private PublishRecord? _production;

    /// <param name="worldFetch">ワールドの画像・情報を取る（引数は、前に失敗したワールドも取り直すか・ログ・キャンセル。テストが差し替える）。既定は VRChat の API → R2。</param>
    public PublishViewModel(AppState app, IDialogService dialogs, IProcessRunner? runner = null, Func<bool, Action<string>, CancellationToken, Task>? worldFetch = null) : base(app, dialogs)
    {
        _runner = runner ?? new ProcessRunner();
        _worldFetch = worldFetch ?? FetchWorldsAsync;
        FetchWorldsCommand = Command(FetchWorldsFromUiAsync, () => !_busy && !Session.IsOffline);
        PublishDevCommand = Command(ct => PublishAsync(PublishTarget.Develop, ct), () => !_busy);
        PublishProductionCommand = Command(ct => PublishAsync(PublishTarget.Production, ct), () => !_busy && DevIsCurrent);
        RefreshCommand = new RelayCommand(Refresh);
        Refresh();
    }

    /// <summary>ワールドの画像・情報（VRChat から）を、足りない分だけ取る。公開とは別の操作。</summary>
    public AsyncCommand FetchWorldsCommand { get; }

    public AsyncCommand PublishDevCommand { get; }

    public AsyncCommand PublishProductionCommand { get; }

    public RelayCommand RefreshCommand { get; }

    /// <summary>確認用（dev）の公開の状態（画面に出す文）。</summary>
    public string DevText => Describe(_dev);

    /// <summary>本番の公開の状態（画面に出す文）。</summary>
    public string ProductionText => Describe(_production);

    /// <summary>本番の公開ボタンの補足（押せない理由など）。</summary>
    public string ProductionHint =>
        DevIsCurrent ? "本番は、公開の前にメンテナンスの表示（お知らせ）も確かめます。"
        : "本番へ公開するには、先に今の内容を確認用（dev）へ公開して、サイトを確かめてください。";

    /// <summary>実行中の出力（秘密は消してある。長いときは新しいほうだけ）。</summary>
    public string Log
    {
        get { lock (_logGate) return string.Join("\n", _logLines); }
    }

    public bool IsBusy => _busy;

    private bool _retryFailed;

    /// <summary>前に取れなかったワールドも、もう一度取る（既定は、前の失敗から 24 時間は問い合わせない）。</summary>
    public bool RetryFailed
    {
        get => _retryFailed;
        set
        {
            if (_retryFailed == value) return;
            _retryFailed = value;
            OnPropertyChanged();
            RefreshWorlds();
        }
    }

    private string _worldsText = "";

    /// <summary>取るものの状態（画面に出す文。画像・情報のないワールドの数など）。</summary>
    public string WorldsText
    {
        get => _worldsText;
        private set
        {
            if (_worldsText == value) return;
            _worldsText = value;
            OnPropertyChanged();
        }
    }

    private WorldFetchService.Pending _pending = new(0, 0, 0, 0);


    private string WorldStateFile() => Path.Combine(Session.Repo.LocalDir, "world-fetch-state.json");

    /// <summary>取るものの数を、記事の中身から数え直す（VRChat には問い合わせない）。</summary>
    private void RefreshWorlds()
    {
        if (Session.IsOffline)
        {
            WorldsText = "デモでは、VRChat から取得しません。";
            return;
        }
        try
        {
            _pending = WorldFetchService.CountPending(Session.Editor, WorldStateFile(), DateTimeOffset.UtcNow, _retryFailed);
            var parts = new List<string>();
            if (_pending.Images > 0) parts.Add($"画像のないワールド {_pending.Images} 件");
            if (_pending.Stats > 0) parts.Add($"情報（訪問数・説明など）のないワールド {_pending.Stats} 件");
            WorldsText = parts.Count == 0
                ? "取得するものはありません。" + (_pending.Waiting > 0 ? $"（前に取れなかったワールド {_pending.Waiting} 件は、24 時間たつまで待っています）" : "")
                : string.Join("・", parts) + $"（取得に約 {_pending.Minutes} 分）" + (_pending.Waiting > 0 ? $"。前に取れなかった {_pending.Waiting} 件は待っています" : "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            WorldsText = "ワールドの状態を読めません: " + e.Message;
        }
        FetchWorldsCommand.RaiseCanExecuteChanged();
    }

    private bool DevIsCurrent => SitePublishService.StateOf(_dev, _currentHash) == PublishState.UpToDate;

    /// <summary>今の記事の中身と、公開の記録を読み直す。</summary>
    public void Refresh()
    {
        try
        {
            _currentHash = SitePublishService.ContentHash(Session.Repo.ContentDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            _currentHash = "";
            Status = "記事の中身を読めません: " + e.Message;
        }
        var log = PublishLogFile();
        _dev = log.Get(PublishTarget.Develop);
        _production = log.Get(PublishTarget.Production);
        RefreshWorlds();
        RaiseState();
    }

    private PublishLog PublishLogFile() => new(Path.Combine(Session.Repo.LocalDir, "publish-state.json"));

    private string Describe(PublishRecord? record)
    {
        var text = SitePublishService.Describe(record, _currentHash);
        return record?.Commit is { Length: > 0 } commit ? $"{text}・サイトのコード {commit}" : text;
    }

    private void RaiseState()
    {
        OnPropertyChanged(nameof(DevText));
        OnPropertyChanged(nameof(ProductionText));
        OnPropertyChanged(nameof(ProductionHint));
        OnPropertyChanged(nameof(IsBusy));
        PublishDevCommand.RaiseCanExecuteChanged();
        PublishProductionCommand.RaiseCanExecuteChanged();
        FetchWorldsCommand.RaiseCanExecuteChanged();
    }

    private void AddLog(string line)
    {
        lock (_logGate)
        {
            _logLines.Add(line);
            if (_logLines.Count > MaxLogLines) _logLines.RemoveRange(0, _logLines.Count - MaxLogLines);
        }
        OnPropertyChanged(nameof(Log));
    }

    /// <summary>ワールドの画像・情報を、VRChat から足りない分だけ取る（画像は R2 に置く）。失敗は、ログと結果に出る（例外にしない）。</summary>
    private async Task FetchWorldsAsync(bool retryFailed, Action<string> log, CancellationToken ct)
    {
        using var http = WorldImageService.CreateClient();
        await WorldFetchService.FetchAsync(Session.Editor, http, () =>
        {
            var handle = Session.OpenStore(Session.Bucket);
            return (handle.Store, handle);
        }, WorldStateFile(), DateTimeOffset.UtcNow, retryFailed, log, ct);
    }

    /// <summary>「サイトの画像情報を更新する」。確認のあと、足りない分だけ取る（1 件ごとに約 1 分）。</summary>
    private async Task FetchWorldsFromUiAsync(CancellationToken ct)
    {
        if (Session.IsOffline)
        {
            Status = "デモでは、VRChat から取得しません。";
            return;
        }
        RefreshWorlds();
        if (!_pending.Any)
        {
            Status = "取得するものはありません。";
            return;
        }
        var confirmed = await Dialogs.ConfirmAsync(
            "サイトの画像・情報を更新します",
            $"画像のないワールド {_pending.Images} 件・情報のないワールド {_pending.Stats} 件を、VRChat の公開 API から取ります（画像は、Cloudflare R2 に置きます）。\nVRChat には 1 回ごとに約 1 分おくので、約 {_pending.Minutes} 分かかります。取れなかったワールドは、24 時間は問い合わせません。\n公開（確認用・本番）は、これとは別に行います。",
            "更新する");
        if (!confirmed) return;

        lock (_logGate) _logLines.Clear();
        OnPropertyChanged(nameof(Log));
        _busy = true;
        RaiseState();
        Status = "サイトの画像・情報を更新しています…（1 件ごとに約 1 分かかります）";
        var finished = false;
        try
        {
            await Task.Run(() => _worldFetch(_retryFailed, AddLog, ct), ct);
            Status = "サイトの画像・情報の更新が終わりました。内容が変わったので、確認用（dev）へ公開し直してください。";
            finished = true;
        }
        finally
        {
            _busy = false;
            Refresh();
        }
        if (finished) await Dialogs.InformAsync("更新が終わりました", "サイトの画像・情報（ワールド）の更新が終わりました。結果は、下の出力の欄にあります。\n内容が変わったので、確認用（dev）へ公開し直してください。");
    }

    private async Task PublishAsync(PublishTarget target, CancellationToken ct)
    {
        if (Session.IsOffline)
        {
            Status = "デモでは公開できません（実際のサイトには何も出しません）。";
            return;
        }
        Refresh();
        SitePublishService.RequireCloudflare(Session.Repo);                                  // 足りない設定は、確認の前に知らせる
        if (target == PublishTarget.Production) SitePublishService.RequireReadyForProduction(PublishLogFile(), _currentHash);

        var confirmed = target == PublishTarget.Production
            ? await Dialogs.ConfirmAsync(
                "本番へ公開します",
                $"今の記事の中身を、本番（{ProductionUrl}）へ公開します。確認用（dev）で確かめた内容です（ワールドの画像・情報は、本番では取りません。確認用で取ったものを公開します）。\n公開の前に、メンテナンスの表示（MAINTENANCE_MODE）が notice か true かを確かめます。そうでなければ公開しません。\n公開の前に、「同期」タブで content が R2 と揃っているか確かめてください（公開前のガードは Git の作業ツリーだけを見ます。content/ は Git の外です）。",
                "本番へ公開する")
            : await Dialogs.ConfirmAsync(
                "確認用（dev）へ公開します",
                $"今の記事の中身を、確認用のサイト（{DevUrl}）へ公開します。ビルドから行うので、数分かかります。\n公開の前に、「同期」タブで content が R2 と揃っているか確かめてください（公開前のガードは Git の作業ツリーだけを見ます。content/ は Git の外です）。",
                "確認用へ公開する");
        if (!confirmed) return;

        lock (_logGate) _logLines.Clear();
        OnPropertyChanged(nameof(Log));
        _busy = true;
        RaiseState();
        Status = $"{SitePublishService.Label(target)}へ公開しています…（数分かかります）";
        try
        {
            var record = await Task.Run(() => SitePublishService.PublishAsync(Session.Repo, target, _runner, PublishLogFile(), DateTimeOffset.UtcNow, AddLog, ct), ct);
            Status = $"{SitePublishService.Label(target)}へ公開しました（{record.At.ToOffset(TimeSpan.FromHours(9)):yyyy-MM-dd HH:mm}）。{(target == PublishTarget.Production ? ProductionUrl : DevUrl)}";
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }
}
