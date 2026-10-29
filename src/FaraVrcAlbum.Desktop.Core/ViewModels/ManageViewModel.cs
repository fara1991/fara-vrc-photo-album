using System.Collections.ObjectModel;
using FaraVrcAlbum.Content;
using FaraVrcAlbum.Desktop.Mvvm;
using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Images;
using FaraVrcAlbum.Tools.Services;

namespace FaraVrcAlbum.Desktop.ViewModels;

/// <summary>削除の候補（本番の台帳の uploaded）。</summary>
public sealed class RemovableItemViewModel(string key, string takenText, string worldText, string? blockReason = null)
{
    public string Key { get; } = key;

    public string TakenText { get; } = takenText;

    public string WorldText { get; } = worldText;

    /// <summary>削除できない理由（表紙・SNS 共有用の画像に設定されている、など）。null なら削除できる。</summary>
    public string? BlockReason { get; } = blockReason;

    public bool CanRemove => BlockReason is null;

    public override string ToString() => $"{TakenText}  {WorldText}" + (BlockReason is null ? "" : "　【選べません】" + BlockReason);
}

/// <summary>写真の点検の 1 行（アップロード済みの写真。どこで使われているか・使われていないかの判定つき）。</summary>
public sealed class AuditPhotoViewModel(string key, string takenText, string worldText, string? previewUrl) : ObservableObject
{
    private string _judgement = "未点検";
    private bool _isUnused;

    public string Key { get; } = key;

    public string TakenText { get; } = takenText;

    public string WorldText { get; } = worldText;

    public string? PreviewUrl { get; } = previewUrl;

    /// <summary>判定（使用中: 記事名 / 使われていません / 未点検）。</summary>
    public string Judgement
    {
        get => _judgement;
        set => Set(ref _judgement, value);
    }

    /// <summary>どの記事にも使われていないとき true（一覧で目立たせる）。</summary>
    public bool IsUnused
    {
        get => _isUnused;
        set => Set(ref _isUnused, value);
    }
}

/// <summary>確認・点検の結果の 1 行。写真（またはファイル）を選ぶとプレビューを出すために、保存先のキーを持つ。</summary>
public sealed record ManageLineViewModel(string Text, string? Key, string? PreviewUrl, string Result = "")
{
    public override string ToString() => Text;
}

/// <summary>
/// 管理: verify（公開の画像と記事の一致）・写真の点検（アップロード済みの写真が記事で使われているか）・remove（写真の削除。確認ダイアログの後だけ）。
/// verify はデモでは公開 URL を見ない。remove は本番だけ（CLI と同じ規則）。
/// </summary>
public sealed class ManageViewModel : PageViewModel
{
    private RemovableItemViewModel? _selectedRemovable;
    private string _verifySummary = "";
    private string _auditSummary = "";
    private string _removePlanText = "";
    private bool _includeLedger;

    public ManageViewModel(AppState app, IDialogService dialogs) : base(app, dialogs)
    {
        VerifyProblems = [];
        VerifyTargets = [];
        VerifyNotes = [];
        ScopeCommand = Command(_ => { PreviewScope(); return Task.CompletedTask; });
        AuditPhotos = [];
        Removable = [];
        VerifyCommand = Command(VerifyAsync, () => !Session.IsOffline);
        AuditCommand = Command(AuditAsync);
        RemoveRefreshCommand = Command(_ => LoadRemovableAsync());
        RemovePlanCommand = Command(_ => PlanRemoveAsync(), () => SelectedRemovable is not null);
        RemoveCommand = Command(RemoveAsync, () => SelectedRemovable is not null);
    }

    public ObservableCollection<ManageLineViewModel> VerifyProblems { get; }

    /// <summary>確認の対象の写真（どの写真を調べているかを示す）。</summary>
    public ObservableCollection<ManageLineViewModel> VerifyTargets { get; }

    private ManageLineViewModel? _selectedLine;

    /// <summary>確認・点検の一覧で選んだ行（右のプレビューに出す）。</summary>
    public ManageLineViewModel? SelectedLine
    {
        get => _selectedLine;
        set
        {
            if (Set(ref _selectedLine, value)) OnPropertyChanged(nameof(LinePreviewUrl));
        }
    }

    public string? LinePreviewUrl => SelectedLine?.PreviewUrl;

    /// <summary>検査の対象の注記（0 件の理由・サンプルの 404 は正常、など）。</summary>
    public ObservableCollection<string> VerifyNotes { get; }

    /// <summary>台帳（uploaded）の写真も検査に含める（CLI の verify --ledger と同じ）。</summary>
    public bool IncludeLedger
    {
        get => _includeLedger;
        set
        {
            if (!Set(ref _includeLedger, value)) return;
            BackgroundTasks.Fire(ScopeCommand.RunAsync(), Dialogs.ReportError);
        }
    }

    /// <summary>検査の対象を数える（公開 URL は見ない。デモでも使える）。</summary>
    public AsyncCommand ScopeCommand { get; }

    /// <summary>アップロード済みの写真の一覧（点検すると、使われているかの判定が付く）。</summary>
    public ObservableCollection<AuditPhotoViewModel> AuditPhotos { get; }

    private AuditPhotoViewModel? _selectedAuditPhoto;

    public AuditPhotoViewModel? SelectedAuditPhoto
    {
        get => _selectedAuditPhoto;
        set
        {
            if (Set(ref _selectedAuditPhoto, value)) OnPropertyChanged(nameof(AuditPreviewUrl));
        }
    }

    public string? AuditPreviewUrl => SelectedAuditPhoto?.PreviewUrl;

    public ObservableCollection<RemovableItemViewModel> Removable { get; }

    public AsyncCommand VerifyCommand { get; }

    public AsyncCommand AuditCommand { get; }

    public AsyncCommand RemoveRefreshCommand { get; }

    public AsyncCommand RemovePlanCommand { get; }

    public AsyncCommand RemoveCommand { get; }

    public RemovableItemViewModel? SelectedRemovable
    {
        get => _selectedRemovable;
        set
        {
            if (value is { CanRemove: false })
            {
                // 表紙などで削除できない写真は選べない（理由を出して、前の選択に戻す）
                Status = $"この写真は選べません: {value.BlockReason}";
                OnPropertyChanged(nameof(SelectedRemovable));
                return;
            }
            if (!Set(ref _selectedRemovable, value)) return;
            OnPropertyChanged(nameof(RemovePreviewUrl));
            RemovePlanText = "";
            RemovePlanCommand.RaiseCanExecuteChanged();
            RemoveCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>削除する写真のプレビュー。</summary>
    public string? RemovePreviewUrl => Session.PhotoPreviewUrl(SelectedRemovable?.Key);

    public string VerifySummary
    {
        get => _verifySummary;
        private set => Set(ref _verifySummary, value);
    }

    public string AuditSummary
    {
        get => _auditSummary;
        private set => Set(ref _auditSummary, value);
    }

    public string RemovePlanText
    {
        get => _removePlanText;
        private set => Set(ref _removePlanText, value);
    }

    /// <summary>画面のバケットが変わったら、前の結果を消す。</summary>
    /// <summary>検査の対象（CLI の verify と同じ決め方）。読み取りだけ。</summary>
    public VerifyScope CurrentScope() =>
        VerifyService.Plan(Session.Editor.LoadContent(), IncludeLedger ? Session.LoadProductionLedger() : null);

    private void PreviewScope()
    {
        var scope = CurrentScope();
        VerifyNotes.Clear();
        foreach (var note in scope.Notes) VerifyNotes.Add(UserText.Plain(note));
        FillTargets(scope);
        VerifySummary = $"確認する写真 {scope.Photos.Count} 枚（記事の写真 {scope.PostPhotoCount}・記事に入れていない写真 {scope.LedgerOnlyCount}）。まだサイトは見ていません。";
        Status = "確認する写真を数えました。";
    }

    /// <summary>確認する写真の一覧を作る。確認のあとは、写真ごとの結果（問題なし・問題の内容）を同じ行に添える。</summary>
    private void FillTargets(VerifyScope scope, IReadOnlyList<VerifyProblem>? problems = null)
    {
        VerifyTargets.Clear();
        foreach (var photo in scope.Photos.Take(500))
        {
            var label = photo.Key + (photo.Cover ? "（表紙）" : photo.Ledger ? "（記事に入れていない）" : "");
            var result = "未確認";
            if (problems is not null)
            {
                var mine = problems.Where(p => p.Key == photo.Key).Select(p => UserText.Plain(p.ToString())).ToList();
                result = mine.Count == 0 ? "✓ 問題なし（写真表示可能）" : "⚠ " + string.Join(" / ", mine);
            }
            VerifyTargets.Add(new ManageLineViewModel(label, photo.Key, Session.PhotoPreviewUrl(photo.Key), result));
        }
    }

    private async Task VerifyAsync(CancellationToken ct)
    {
        var baseUrl = Session.Repo.ImageBaseUrl;
        var scope = CurrentScope();
        VerifyNotes.Clear();
        foreach (var note in scope.Notes) VerifyNotes.Add(UserText.Plain(note));
        VerifyProblems.Clear();
        FillTargets(scope);
        if (scope.Photos.Count == 0)
        {
            VerifySummary = "確認する写真がありません（下の注記を見てください）。";
            Status = "確認する写真がありません。";
            return;
        }
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var problems = await VerifyService.RunAsync(scope.Photos, baseUrl, http, 8, ct);
        foreach (var problem in problems.Take(200))
            VerifyProblems.Add(new ManageLineViewModel(UserText.Plain(problem.ToString()), problem.Key, Session.PhotoPreviewUrl(problem.Key)));
        FillTargets(scope, problems);                                                   // 写真ごとの結果を一覧に添える
        VerifySummary = $"{scope.Photos.Count} 枚を確認しました: " + (problems.Count == 0 ? "すべて正しく表示できます。" : $"問題 {problems.Count} 件。");
        Status = "公開写真の確認が終わりました（何も変更していません）。";
    }

    /// <summary>アップロード済みの写真の一覧を作る（判定は点検で付ける。何も書かない）。</summary>
    public void LoadAuditRows()
    {
        var rows = Session.LoadProductionLedger().BySha.Values
            .Where(e => e.State == LedgerStates.Uploaded)
            .OrderBy(e => e.TakenAt, StringComparer.Ordinal).ThenBy(e => e.Key, StringComparer.Ordinal)
            .ToList();
        AuditPhotos.Clear();
        foreach (var e in rows)
            AuditPhotos.Add(new AuditPhotoViewModel(e.Key, e.TakenAt, e.World?.Name is { Length: > 0 } w ? w : "（ワールドなし）", Session.PhotoPreviewUrl(e.Key)));
        AuditSummary = $"アップロード済みの写真 {AuditPhotos.Count} 枚。「点検する」を押すと、写真ごとに使われているかを調べます。";
    }

    /// <summary>写真ごとに、記事（下書きを含む）で使われているか・表紙になっているかを判定する。読むだけ。</summary>
    private Task AuditAsync(CancellationToken ct)
    {
        LoadAuditRows();
        var editor = Session.Editor;
        var usedBy = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var post in PostService.List(editor))
        {
            foreach (var key in post.PhotoKeys)
            {
                if (!usedBy.TryGetValue(key, out var list)) usedBy[key] = list = [];
                list.Add(post.Slug + (post.Draft ? "（非公開）" : ""));
            }
        }
        var covers = PostService.CoverKeys(editor);
        foreach (var row in AuditPhotos)
        {
            if (usedBy.TryGetValue(row.Key, out var slugs))
            {
                row.Judgement = "使用中: " + string.Join("、", slugs.Distinct()) + (covers.Contains(row.Key) ? "（表紙）" : "");
                row.IsUnused = false;
            }
            else
            {
                row.Judgement = "使われていません";
                row.IsUnused = true;
            }
        }
        var unused = AuditPhotos.Count(p => p.IsUnused);
        AuditSummary = $"アップロード済みの写真 {AuditPhotos.Count} 枚: 使用中 {AuditPhotos.Count - unused} 枚・使われていない {unused} 枚。";
        Status = "写真の点検が終わりました（何も変更していません）。";
        return Task.CompletedTask;
    }

    /// <summary>本番の台帳から、削除の候補（uploaded）を読む。</summary>
    public async Task LoadRemovableAsync()
    {
        var session = Session;
        var (rows, covers, used) = await Task.Run(() => (
            session.LoadProductionLedger().BySha.Values
                .Where(e => e.State == LedgerStates.Uploaded)
                .OrderBy(e => e.TakenAt, StringComparer.Ordinal).ThenBy(e => e.Key, StringComparer.Ordinal)
                .Select(e => (e.Key, e.TakenAt, World: e.World?.Name ?? ""))
                .ToList(),
            PostService.CoverKeys(session.Editor),
            PostService.PhotoKeys(session.Editor)));
        Removable.Clear();
        foreach (var row in rows)
        {
            // 記事（非公開のものも含む）で使われている写真は、削除できない（先に記事から外す。表紙なら、先に本文の最初の写真を別の写真にする）
            var reason = covers.Contains(row.Key)
                ? "記事の表紙・SNS 共有用の画像に使われています（記事の本文の最初の写真を別の写真にしてから、削除してください）"
                : used.Contains(row.Key) ? "記事で使われています（記事の写真から外してから、削除してください）" : null;
            Removable.Add(new RemovableItemViewModel(row.Key, row.TakenAt, row.World.Length > 0 ? row.World : "（ワールドなし）", reason));
        }
        Status = $"削除できる写真 {Removable.Count} 枚を読みました。";
    }

    /// <summary>Cloudflare のキャッシュ（URL 指定）を消す手順と、消す URL。画面の表記は変わることがあるので、そう添える。</summary>
    private static IReadOnlyList<string> PurgeSteps(IReadOnlyList<string> urls)
    {
        var host = urls.Count > 0 && Uri.TryCreate(urls[0], UriKind.Absolute, out var first) ? first.Host : "写真の URL のホスト名";
        var lines = new List<string>
        {
            "",
            "【Cloudflare のキャッシュを消す手順】",
            "  1. https://dash.cloudflare.com/ を開いてログインする",
            $"  2. 下の URL のホスト名（{host}）を含むドメインを選ぶ（写真の配信に使っているドメイン）",
            "  3. 左のメニューの「キャッシュ（Caching）」→「Configuration」を開き、「Purge Cache」の「Custom Purge」を押す",
            "  4. 「URL」を選び、下の URL をすべて貼り付ける（1 行に 1 つ）",
            "  5. 「Purge」を押す（数秒で消えます）",
            "  ※ 画面の表記は、Cloudflare の更新で少し変わることがあります",
            "",
            "消す URL（コピーして貼り付けてください）:",
        };
        lines.AddRange(urls.Select(u => "  " + u));
        return lines;
    }

    private Task PlanRemoveAsync()
    {
        var key = SelectedRemovable?.Key ?? throw new InvalidOperationException("写真を選んでください");
        var plan = RemoveService.Plan(key, Session.LoadProductionLedger(), Session.Editor, Session.Repo.ImageBaseUrl);
        var lines = new List<string>
        {
            $"写真 {key} を削除すると:",
            $"・写真の保存先からファイル {plan.R2Keys.Count} 個を消します",
            "・写真の記録を「削除済み」にします（同じ写真は、もう取り込めなくなります）",
            "・削除のあと、Cloudflare に残っている古い画像のコピー（キャッシュ）を消してください（消さないと、しばらく古い画像が表示されることがあります）",
        };
        lines.AddRange(PurgeSteps(plan.PurgeUrls));
        RemovePlanText = string.Join("\n", lines);
        Status = "削除の内容を表示しました（まだ何も消していません）。";
        return Task.CompletedTask;
    }

    private async Task RemoveAsync(CancellationToken ct)
    {
        var key = SelectedRemovable?.Key ?? throw new InvalidOperationException("写真を選んでください");
        var ledgerPath = Session.LedgerPath(Session.ProductionBucket);
        var ledger = PhotoLedger.Load(ledgerPath);
        var plan = RemoveService.Plan(key, ledger, Session.Editor, Session.Repo.ImageBaseUrl);
        var confirmed = await Dialogs.ConfirmAsync(
            "写真を削除します",
            $"写真 {key} を写真の保存先から消します（記事では使われていません）。\n" +
            "元に戻せません。続けますか。",
            "削除する");
        if (!confirmed) return;

        using var handle = Session.OpenStore(Session.ProductionBucket);
        await RemoveService.ApplyAsync(plan, Session.ProductionBucket, ledger, ledgerPath, handle.Store, DateTimeOffset.UtcNow, ct);
        RemovePlanText = "削除しました（続けて、次の手順で Cloudflare のキャッシュを消してください）\n" + string.Join("\n", PurgeSteps(plan.PurgeUrls));
        Status = $"削除しました: {key}。サイトへの反映は、アプリの「公開」で公開したあとです。";
        await LoadRemovableAsync();
    }
}
