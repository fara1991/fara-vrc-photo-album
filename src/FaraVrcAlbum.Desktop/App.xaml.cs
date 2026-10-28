using System.Threading.Tasks;
using System.Windows;
using FaraVrcAlbum.Desktop.Services;
using FaraVrcAlbum.Desktop.ViewModels;
using FaraVrcAlbum.Tools.Cli;

namespace FaraVrcAlbum.Desktop;

/// <summary>
/// 起動。未処理の例外（画面のスレッド・バックグラウンドの Task）は、必ず秘密を消してから表示する。
/// 例外の内容はファイルにもログにも残さない（表示だけ）。
/// </summary>
public partial class App : Application
{
    private static RepoContext? _repo;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            ShowError(args.Exception);
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            ShowError(args.Exception.GetBaseException());
            args.SetObserved();
        };

        try
        {
            var session = LaunchOptions.Parse(e.Args).CreateSession();
            _repo = session.Repo;
            var window = new MainWindow(new MainViewModel(session, new WpfDialogService(() => Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? MainWindow)));
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            ShowError(ex);
            Shutdown(1);
        }
    }

    /// <summary>表示の直前に、R2 の値（環境変数と、開いたリポジトリの .env.local）と認証情報の形を消す。</summary>
    private static void ShowError(Exception ex)
    {
        var text = Secrets.Redact(ex.Message, _repo);
        MessageBox.Show(
            $"エラー（{ex.GetType().Name}）: {text}",
            "Fara Photo Album",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
