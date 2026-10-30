using FaraVrcAlbum.Desktop.Mvvm;
using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Tools.Cli;

namespace FaraVrcAlbum.Desktop.ViewModels;

/// <summary>
/// 画面（タブ）の基底。例外は必ず <see cref="ReportError"/> を通して表示する（秘密を消してから）。
/// 状態（バケット）は <see cref="AppState"/> で全画面が共有する。
/// </summary>
public abstract class PageViewModel(AppState app, IDialogService dialogs) : ObservableObject
{
    private string _status = "";

    protected AppState App { get; } = app;

    protected AlbumSession Session => App.Session;

    protected IDialogService Dialogs { get; } = dialogs;

    /// <summary>画面下部の状態行（成功・注意・エラー）。秘密は含まない。</summary>
    public string Status
    {
        get => _status;
        protected set => Set(ref _status, value);
    }

    /// <summary>
    /// 例外を表示用の文に直す。Secrets.Redact を必ず通す（R2 の値・認証情報の形を消す）。
    /// 入力の誤りや未設定のように、メッセージが利用者向けの文である例外は、その文だけを出す。
    /// 想定外の例外だけ、原因をたどれるように型名を付ける。
    /// </summary>
    protected void ReportError(Exception e)
    {
        var text = UserText.Plain(Secrets.Redact(e.Message, Session.Repo));
        Status = IsExpected(e) ? text : $"エラー（{e.GetType().Name}）: {text}";
    }

    private static bool IsExpected(Exception e) =>
        e is InvalidOperationException or ArgumentException or UsageException
            or FileNotFoundException or DirectoryNotFoundException or InvalidDataException;

    /// <summary>例外を画面の状態行に出す（ViewModel の外から、報告が必要な非同期の処理が使う）。</summary>
    public void Report(Exception e) => ReportError(e);

    /// <summary>例外を報告する非同期の命令を作る。</summary>
    protected AsyncCommand Command(Func<CancellationToken, Task> run, Func<bool>? canExecute = null) =>
        new(run, ReportError, canExecute);
}
