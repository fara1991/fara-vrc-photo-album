using System.Collections.ObjectModel;
using FaraVrcAlbum.Desktop.Mvvm;
using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Content;
using FaraVrcAlbum.Tools.Cli;
using FaraVrcAlbum.Tools.Services;

namespace FaraVrcAlbum.Desktop.ViewModels;

/// <summary>.env.local の 1 項目の表示（名前と「あり／なし」だけ。値は読んで捨てる）。</summary>
public sealed record CredentialRowViewModel(string Name, bool Present)
{
    public string PresenceText => Present ? "設定あり" : "未設定";
}

/// <summary>どのファイルの項目か。</summary>
public enum SettingStore
{
    /// <summary>.env.local（KEY=値）。</summary>
    EnvFile,

    /// <summary>local/album.local.json。</summary>
    LocalConfig,
}

/// <summary>
/// 設定の 1 項目（編集できる）。秘密の項目は、値を画面に出さない（「●」）。「表示」を押している間（IsRevealed）だけ、今の値を出す。
/// 新しい値は <see cref="SetNewValue"/> で受ける（秘密の項目は PasswordBox から。プロパティとしては公開しない）。
/// </summary>
public sealed class SettingRowViewModel : ObservableObject
{
    public const string Mask = "●●●●●●●●●●●●";

    private readonly string? _current;
    private string? _newValue;
    private bool _isRevealed;
    private string _editText;

    private readonly string? _defaultValue;

    /// <summary>フォルダの項目か（エクスプローラーで選べる）。</summary>
    public bool IsFolder { get; init; }

    /// <summary>フォルダをエクスプローラーで選ぶ（フォルダの項目だけ）。</summary>
    public RelayCommand? BrowseCommand { get; set; }

    public SettingRowViewModel(string key, string label, SettingStore store, bool isSecret, string? current, bool overriddenByEnvironment, string note, string? defaultValue = null)
    {
        _defaultValue = defaultValue;
        Key = key;
        Label = label;
        Store = store;
        IsSecret = isSecret;
        _current = current;
        OverriddenByEnvironment = overriddenByEnvironment;
        Note = note;
        _editText = isSecret ? "" : current ?? "";
    }

    /// <summary>ファイルの中の名前（R2_ACCOUNT_ID・uploadRoot など）。</summary>
    public string Key { get; }

    public string Label { get; }

    public SettingStore Store { get; }

    public bool IsSecret { get; }

    public bool HasValue => _current is not null;

    /// <summary>環境変数に同じ名前があり、そちらが優先される（ファイルを直しても効かない）。</summary>
    public bool OverriddenByEnvironment { get; }

    public string Note { get; }

    /// <summary>今の値の表示。秘密は、押している間だけ値、それ以外は「●」。</summary>
    public string DisplayText => _current is null ? (_defaultValue is null ? "未設定" : $"{_defaultValue}（既定）") : IsSecret && !IsRevealed ? Mask : _current;

    /// <summary>「表示」ボタンを押している間 true。</summary>
    public bool IsRevealed
    {
        get => _isRevealed;
        set
        {
            if (!IsSecret || !Set(ref _isRevealed, value)) return;
            OnPropertyChanged(nameof(DisplayText));
        }
    }

    /// <summary>秘密でない項目の入力欄（今の値が入っている）。秘密の項目では使わない（常に空）。</summary>
    public string EditText
    {
        get => _editText;
        set
        {
            if (IsSecret || !Set(ref _editText, value)) return;
            _newValue = value.Trim();
            OnPropertyChanged(nameof(IsChanged));
        }
    }

    /// <summary>秘密の項目の新しい値（PasswordBox から）。空は「変えない」。</summary>
    public void SetNewValue(string value)
    {
        _newValue = value.Trim().Length == 0 ? null : value.Trim();
        OnPropertyChanged(nameof(IsChanged));
    }

    /// <summary>秘密の項目を消す（保存で行ごと消える）。</summary>
    public void Clear()
    {
        _newValue = "";
        OnPropertyChanged(nameof(IsChanged));
    }

    public bool IsChanged => _newValue is not null && _newValue != (_current ?? "");

    /// <summary>保存する値（空 = 消す）。変わっていなければ null。</summary>
    internal string? PendingValue => IsChanged ? _newValue : null;
}

/// <summary>
/// 設定の表示と編集: Cloudflare R2 の認証情報（.env.local）、各種のフォルダ（local/album.local.json。フォルダはエクスプローラーでも選べる）。VRChat の写真のフォルダは、ピクチャの下の VRChat に決まっていて、設定では変えない。
/// バケット名も編集できる（local/album.local.json の bucket。CLI の R2_BUCKET とは別。確認用と本番は同じバケットを見る）。
/// 秘密（R2 の 3 項目）は「●」で出し、「表示」を押している間だけ値を出す。ログや状態行には値を出さない。
/// 保存は確認ダイアログ（変える項目の名前だけを出す）の後だけ。保存したら設定を読み直す。
/// </summary>
public sealed class SettingsViewModel : PageViewModel
{
    public SettingsViewModel(AppState app, IDialogService dialogs) : base(app, dialogs)
    {
        Rows = [];
        SaveCommand = Command(SaveAsync, () => Rows.Any(r => r.IsChanged));
        RevertCommand = new RelayCommand(Refresh);
        CopyOutCommand = Command(CopyOutAsync);
        BackupCommand = Command(BackupAsync);
        RestoreCommand = Command(RestoreAsync);
        Refresh();
    }

    public IReadOnlyList<CredentialRowViewModel> Credentials { get; private set; } = [];

    /// <summary>編集できる項目。</summary>
    public ObservableCollection<SettingRowViewModel> Rows { get; }

    public AsyncCommand SaveCommand { get; }

    /// <summary>記事の中身を、リポジトリの外のフォルダへコピーする（コピーだけ。使う場所は変えない）。</summary>
    public AsyncCommand CopyOutCommand { get; }

    /// <summary>記事の中身を、控え用のバケットにスナップショットとして置く。</summary>
    public AsyncCommand BackupCommand { get; }

    /// <summary>控え用のバケットの、いちばん新しいスナップショットから、記事の中身を戻す。</summary>
    public AsyncCommand RestoreCommand { get; }

    /// <summary>今、記事の中身として使っているフォルダ（画面に出す文）。</summary>
    public string ContentFolderText =>
        Session.Repo.ConfiguredContentDir is { } configured ? $"今使っている場所: {configured}" : "今使っている場所: 既定の content フォルダ";

    /// <summary>最後に控えを作った日時（無ければ、まだ作っていない）。</summary>
    public string BackupStatusText => Session.Preferences.LastBackup is { Length: > 0 } last ? $"最後の控え: {last}" : "控えは、まだ作っていません。";

    /// <summary>入力を捨てて、ファイルから読み直す。</summary>
    public RelayCommand RevertCommand { get; }

    public string ConnectionText { get; private set; } = "";

    /// <summary>デモで起動しているとき true（実際の保存先には接続しない）。</summary>
    public bool IsDemo => Session.IsOffline;

    /// <summary>今使っている Cloudflare の R2 バケット名。</summary>
    public string BucketText => Session.Bucket;

    /// <summary>設定を読み直す（入力中の値は捨てる）。</summary>
    public void Refresh()
    {
        var repo = Session.Repo;
        Credentials = RepoContext.R2Variables
            .Concat(["R2_BUCKET"])
            .Select(name => new CredentialRowViewModel(name, repo.Env(name) is not null))
            .ToList();

        foreach (var row in Rows) row.PropertyChanged -= OnRowChanged;
        Rows.Clear();
        Rows.Add(EnvRow(repo, "R2_ACCOUNT_ID", "Cloudflare R2 のアカウント ID", secret: true, "Cloudflare のダッシュボードの R2 の画面に出ている、アカウントの ID です。"));
        Rows.Add(EnvRow(repo, "R2_ACCESS_KEY_ID", "Cloudflare R2 のアクセスキー ID", secret: true, "R2 の「API トークン」を作ったときに表示される、アクセスキーの ID です。"));
        Rows.Add(EnvRow(repo, "R2_SECRET_ACCESS_KEY", "Cloudflare R2 のシークレットアクセスキー", secret: true, "API トークンを作ったときに一度だけ表示される、秘密の値です。"));
        Rows.Add(EnvRow(repo, "CLOUDFLARE_API_TOKEN", "Cloudflare の API トークン（サイトの公開用）", secret: true, "サイトの公開に使う、秘密の値です。Cloudflare の「API トークンを作成」で、「Cloudflare Workers を編集する」のテンプレートから作ります（R2 用のトークンとは別です）。"));
        Rows.Add(new SettingRowViewModel("bucket", "Cloudflare R2 のバケット名", SettingStore.LocalConfig, false, repo.LocalConfigValue("bucket"), false,
            "サイトの写真の保存先（R2 のバケット）の名前です。小文字の英数字とハイフンで、3〜63 文字。未設定のままでは、写真の保存先に接続できません。"));
        Rows.Add(new SettingRowViewModel("avatarThumbnailDir", "アバターのサムネイル画像のフォルダ", SettingStore.LocalConfig, false, repo.LocalConfigValue("avatarThumbnailDir"), false,
            "「アバター・服」の一覧に出す、アバターのサムネイル画像（アバター名と同じ名前の画像ファイル）があるフォルダです。空にすると、アバターの prefab があるフォルダの上の Assets の下から、サムネイルのフォルダ（名前に thumb や icon を含む）を自動で探します。",
            "自動で探す") { IsFolder = true });
        Rows.Add(new SettingRowViewModel("contentDir", "記事の中身（content）のフォルダ", SettingStore.LocalConfig, false, repo.LocalConfigValue("contentDir"), false,
            "記事・マスタ・台帳の JSON を置くフォルダです。空なら、既定の content フォルダを使います。今は変えないでください（このアプリから公開できるようになる前に変えると、サイトに反映されなくなります）。別のフォルダへコピーしておくには、下の「外のフォルダへコピー」を使います。",
            "既定の content フォルダ") { IsFolder = true });
        Rows.Add(new SettingRowViewModel("backupBucket", "記事の中身の控え用のバケット名", SettingStore.LocalConfig, false, repo.LocalConfigValue("backupBucket"), false,
            "記事の中身の控えを置く、R2 のバケットの名前です。写真のバケットとは別の、公開しないバケットにします（写真のバケットは公開されているので、控えを置くと誰でも読めてしまいます）。未設定のあいだは、控えを作れません。"));
        foreach (var row in Rows.Where(r => r.IsFolder))
        {
            var target = row;
            target.BrowseCommand = new RelayCommand(() =>
            {
                // エクスプローラーのフォルダ選択を開く（選ばなければ何もしない）
                var picked = Dialogs.PickFolder(target.Label + "を選ぶ", target.EditText);
                if (picked is not null) target.EditText = picked;
            });
        }
        foreach (var row in Rows) row.PropertyChanged += OnRowChanged;

        var missing = Rows.Where(r => r.Store == SettingStore.EnvFile && r.Key.StartsWith("R2_", StringComparison.Ordinal) && !r.HasValue && !RepoContext.HasProcessEnv(r.Key)).Select(r => r.Label).ToList();
        if (!RepoContext.HasConfiguredProductionBucket) missing.Add("Cloudflare R2 のバケット名");
        ConnectionText = Session.IsOffline
            ? "デモで起動しています（実際の保存先には接続しません）。"
            : missing.Count == 0
                ? "写真の保存先（Cloudflare R2）に接続する設定がそろっています。"
                : $"足りない設定があります: {string.Join("、", missing)}。そろうまで、アップロードや削除はできません。";
        OnPropertyChanged(nameof(Credentials));
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(IsDemo));
        OnPropertyChanged(nameof(BucketText));
        OnPropertyChanged(nameof(ContentFolderText));
        OnPropertyChanged(nameof(BackupStatusText));
        SaveCommand.RaiseCanExecuteChanged();
    }

    private static SettingRowViewModel EnvRow(RepoContext repo, string name, string label, bool secret, string note) =>
        new(name, label, SettingStore.EnvFile, secret, repo.EnvFileValue(name), RepoContext.HasProcessEnv(name), note);

    private void OnRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingRowViewModel.IsChanged)) SaveCommand.RaiseCanExecuteChanged();
    }

    /// <summary>記事の中身のフォルダ・控え用のバケット名の変更を検査する。問題があれば理由（無ければ null）。</summary>
    private string? ValidateContentSettings(IReadOnlyList<SettingRowViewModel> changed)
    {
        if (changed.FirstOrDefault(r => r.Key == "contentDir") is { PendingValue: { Length: > 0 } folder })
        {
            var full = Path.GetFullPath(folder, Session.Repo.Root);
            if (!Directory.Exists(full)) return "記事の中身のフォルダが見つかりません（先に「外のフォルダへコピー」で作るか、あるフォルダを選んでください）。";
            if (Directory.EnumerateFileSystemEntries(full).Any())
            {
                try
                {
                    ContentLoader.Load(full);                                           // 記事の中身として読めるフォルダだけ
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    return "このフォルダは、記事の中身として読めません: " + UserText.Plain(Secrets.Redact(e.Message, Session.Repo));
                }
            }
        }
        if (changed.FirstOrDefault(r => r.Key == "backupBucket") is { PendingValue: { Length: > 0 } backup })
        {
            var photoBucket = changed.FirstOrDefault(r => r.Key == "bucket")?.PendingValue is { Length: > 0 } pendingPhoto ? pendingPhoto : Session.ProductionBucket;
            try
            {
                ContentBackupService.RequireBackupBucket(backup, photoBucket);
            }
            catch (InvalidOperationException e)
            {
                return e.Message;
            }
        }
        return null;
    }

    /// <summary>記事の中身を、外のフォルダへコピーする。コピーだけで、使うフォルダは変えない。</summary>
    private async Task CopyOutAsync(CancellationToken ct)
    {
        var dest = Dialogs.PickFolder("記事の中身のコピー先のフォルダを選ぶ（空のフォルダ、または新しいフォルダ）", null);
        if (dest is null) return;
        var source = Session.Repo.ContentDir;
        var confirmed = await Dialogs.ConfirmAsync(
            "記事の中身をコピーします",
            $"記事・マスタ・台帳の JSON（{ContentBackupService.ContentFiles(source).Count} ファイル）を、選んだフォルダにコピーします。\n元は変えません（移しません）。コピーしただけでは、このアプリが使うフォルダは変わりません。\n\n注意: 使うフォルダを切り替えるのは、このアプリから公開（ビルドとデプロイ）できるようになってからにしてください。今のサイトの公開は、既定の content フォルダから行われます。",
            "コピーする");
        if (!confirmed) return;
        var count = await Task.Run(() => ContentBackupService.CopyContent(source, dest), ct);
        Status = $"記事の中身 {count} ファイルをコピーしました（検査済み）。使うフォルダは変えていません。";
    }

    /// <summary>記事の中身を、控え用のバケットにスナップショットとして置く。</summary>
    private async Task BackupAsync(CancellationToken ct)
    {
        var bucket = ContentBackupService.RequireBackupBucket(Session.BackupBucket, Session.ProductionBucket);
        var dir = Session.Repo.ContentDir;
        var files = ContentBackupService.ContentFiles(dir);
        var confirmed = await Dialogs.ConfirmAsync(
            "記事の中身の控えを作ります",
            $"記事・マスタ・台帳の JSON（{files.Count} ファイル）を、控え用のバケット（{bucket}）に、新しい控えとして置きます。古い控えは消しません。\nこのバケットは、公開しない設定にしてください（台帳に元のファイル名が入っています）。",
            "控えを作る");
        if (!confirmed) return;
        using var handle = Session.OpenStore(bucket);
        var info = await Guard(bucket, () => ContentBackupService.BackupAsync(dir, handle.Store, DateTimeOffset.UtcNow, ct));
        Session.Preferences.LastBackup = $"{info.CreatedAt.ToOffset(TimeSpan.FromHours(9)):yyyy-MM-dd HH:mm}（{info.FileCount} ファイル）";
        OnPropertyChanged(nameof(BackupStatusText));
        Status = $"記事の中身の控えを作りました（{info.Id}・{info.FileCount} ファイル）。";
    }

    /// <summary>控え用のバケットへのアクセスが拒否されたとき（トークンがそのバケットを許していない）、原因と直し方を示す文にする。</summary>
    internal static async Task<T> Guard<T>(string bucket, Func<Task<T>> run)
    {
        try { return await run(); }
        catch (Amazon.S3.AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            throw new InvalidOperationException($"控え用のバケット（{bucket}）にアクセスできません（拒否されました）。R2 の API トークンが、このバケットを読み書きできる設定になっているか確認してください（Cloudflare → R2 → API トークンを管理 →「バケットを指定」に {bucket} を足す、または全バケットにする）");
        }
        catch (Amazon.S3.AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"控え用のバケット（{bucket}）が見つかりません。名前が合っているか、Cloudflare の R2 にあるか確認してください");
        }
    }

    /// <summary>控え用のバケットのいちばん新しいスナップショットから、記事の中身を戻す。戻す前の内容は、このアプリの local/restore-backups に残す。</summary>
    private async Task RestoreAsync(CancellationToken ct)
    {
        var bucket = ContentBackupService.RequireBackupBucket(Session.BackupBucket, Session.ProductionBucket);
        using var handle = Session.OpenStore(bucket);
        var snapshots = await Guard(bucket, () => ContentBackupService.ListAsync(handle.Store, ct));
        if (snapshots.Count == 0)
        {
            Status = "控えが見つかりません（控え用のバケットに、まだ控えがありません）。";
            return;
        }
        var latest = snapshots[0];
        var dir = Session.Repo.ContentDir;
        var safety = Path.Combine(Session.Repo.LocalDir, "restore-backups", DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(9)).ToString("yyyyMMdd-HHmmss"));
        var confirmed = await Dialogs.ConfirmAsync(
            "控えから記事の中身を戻します",
            $"いちばん新しい控え（{latest.CreatedAt.ToOffset(TimeSpan.FromHours(9)):yyyy-MM-dd HH:mm}・{latest.FileCount} ファイル）の内容に、今の記事の中身を入れ替えます。\n控えに無いファイルは消えます。入れ替える前の内容は、このアプリの手元のフォルダ（local/restore-backups）にコピーを残します。\n中身は、入れ替える前に検査し、読めない内容なら何も変えません。",
            "戻す");
        if (!confirmed) return;
        var count = await ContentBackupService.RestoreAsync(handle.Store, latest.Id, dir, safety, ct);
        Status = $"控え（{latest.Id}）から {count} ファイルを戻しました。戻す前の内容は {safety} にあります。";
    }

    private async Task SaveAsync(CancellationToken ct)
    {
        var changed = Rows.Where(r => r.IsChanged).ToList();
        if (changed.FirstOrDefault(r => r.Key == "bucket") is { PendingValue: { Length: > 0 } name } && !RepoContext.IsValidBucketName(name))
        {
            Status = "バケット名は、小文字の英数字とハイフンで、3〜63 文字にしてください（先頭は英数字）。";
            return;
        }
        if (ValidateContentSettings(changed) is { } problem)
        {
            Status = problem;
            return;
        }
        var lines = changed.Select(r => $"・{r.Label}: " + (string.IsNullOrEmpty(r.PendingValue) ? "消す" : r.IsSecret ? "新しい値にする" : r.PendingValue));
        var confirmed = await Dialogs.ConfirmAsync(
            "設定を保存します",
            string.Join("\n", lines) + "\n\nこの PC の中にだけ保存します（サイトや GitHub には送りません）。",
            "保存する");
        if (!confirmed) return;

        var repo = Session.Repo;
        var env = changed.Where(r => r.Store == SettingStore.EnvFile).ToDictionary(r => r.Key, r => r.PendingValue, StringComparer.Ordinal);
        var local = changed.Where(r => r.Store == SettingStore.LocalConfig).ToDictionary(r => r.Key, r => r.PendingValue, StringComparer.Ordinal);
        await Task.Run(() =>
        {
            if (env.Count > 0) LocalSettings.SetEnvValues(repo.EnvFilePath, env);
            if (local.Count > 0) LocalSettings.SetLocalConfig(repo.LocalConfigPath, local);
        }, ct);
        Session.Reload();
        Refresh();
        Status = $"設定を保存しました（{string.Join("、", changed.Select(r => r.Label))}）。" +
                 (changed.Any(r => r.OverriddenByEnvironment) ? " 環境変数に同じ設定があるものは、環境変数が優先されます。" : "");
    }
}
