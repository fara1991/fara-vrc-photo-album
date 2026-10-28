using FaraVrcAlbum.Tools.Cli;

namespace FaraVrcAlbum.Tools;

public static class Program
{
    private const string UsageText = """
        album — Fara Photo Album の写真の取り込み・アップロード・記事の下書き・削除（手元で実行）

        手順:
          album prepare                       inbox\yyyy-mm フォルダを作る（空のものだけ）
          （掲載してよい PNG を inbox\yyyy-mm にコピーする＝掲載確認の記録）
          album upload                       ドライラン（計画だけ表示）
          album upload --apply                R2 に書き、台帳に登録し、uploaded\ へ移す
          album draft --post <slug> --title 題 --since 2026-10-01
                                              記事の下書きのプレビュー（既定は書かない）。--apply で書く（draft: true）
          album draft --post <slug> --keys k1,k2 --append [--avatar id] [--outfits id,id] [--apply]
                                              既存の記事の末尾に足す（プレビューは既定）
          album avatar list|add|edit|link|remove … ／ album outfit list|add|edit|remove …
                                              アバターと服のマスタ（content/avatars.json・outfits.json）。既定はプレビュー（検査を通して戻す）。--apply で書く
          album photo-avatar <key> <アバター> [--outfits id,id] [--apply]
                                              記事の写真にアバター（と服）を付ける。既定はプレビュー
          album cover <key> [--apply]         表紙の写真の OGP（_og.jpg）を作る。カバー写真だけに必要
          album verify [--ledger]             公開 URL と JSON の一致を確かめる（--ledger: 記事に無い台帳の写真も）
          album audit                         R2 の孤児・欠けを表示（読み取りのみ）
          album worlds [--apply] [--refresh]  ワールドの画像を VRChat から取り、R2 に置いて content/worlds.json に書く。
                                              既定は画像の無いワールドだけ・ドライラン。--refresh で全部を取り直す
          album world-stats [--apply]         全ワールドの情報（訪問数など）を content/world-stats.json に書く
                                              （手元のアプリの「サイトの画像情報を更新する」ボタンも同じ）

        その他:
          album scan [フォルダ]                元写真を読み取り専用で走査し、月ごとの件数を表示
          album remove <key> [--apply]        写真の削除（R2・台帳・記事）。既定はドライラン

        共通のオプション:
          --repo <dir>          リポジトリの場所（既定: カレントから FaraVrcAlbum.sln を探す）
          --upload-root <dir>   アップロード用フォルダ（既定: ピクチャ\VRChatAlbum。例: C:\Users\{ユーザー}\Pictures\VRChatAlbum）
          --bucket <name>       R2 のバケット（既定: .env.local の R2_BUCKET か fara-vrc-photos）

        認証情報は .env.local（または環境変数）の R2_ACCOUNT_ID・R2_ACCESS_KEY_ID・R2_SECRET_ACCESS_KEY・R2_BUCKET。値は表示しません。
        詳しい手順は docs/album-tool.md。
        """;

    public static async Task<int> Main(string[] argv)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        // 表示する文字列は、すべて Secrets.Redact を通す（環境変数と、--repo のリポジトリの .env.local の値を消す）
        RepoContext? repo = null;
        try
        {
            var args = Cli.Args.Parse(argv);
            if (args.Command is "help" or "--help" or "-h" || args.Has("help"))
            {
                Console.WriteLine(UsageText);
                return 0;
            }
            repo = RepoContext.Discover(args.Value("repo"));
            return await Commands.RunAsync(args, repo, CancellationToken.None);
        }
        catch (UsageException e)
        {
            Console.Error.WriteLine(Secrets.Redact(e.Message, repo));
            Console.Error.WriteLine("（album help で使い方）");
            return 2;
        }
        catch (Exception e)
        {
            // 例外の表示は、R2 の値と認証情報の形を消してから（AWS の例外に混ざることがある）。種類は表示する
            Console.Error.WriteLine($"エラー（{e.GetType().Name}）: {Secrets.Redact(e.Message, repo)}");
            return 1;
        }
    }
}
