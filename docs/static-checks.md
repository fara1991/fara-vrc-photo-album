# 静的解析とレイアウト計測（CI で止める決まり）

レビューで繰り返し見つかる指摘を、CI で機械的に止める。仕組みは kit（external/fara-blazor-component）の共通部品。ここには、このサイトの設定・件数・除外の書き方だけを書く。

| 検査 | 手元のコマンド | CI の job | 設定 |
| --- | --- | --- | --- |
| レイアウトの自動計測（tap-target・横スクロール・フォーカス・文字の大きさ・Esc のメニュー） | `npm run layout-check` | layout-check | `layout-check.config.mjs`・`tools/layout-check.mjs` |
| ESLint（JavaScript） | `npm run lint` | lint | `eslint.config.mjs` |
| C# のアナライザー（ラチェット） | `dotnet build FaraVrcAlbum.sln -c Release` | build | `Directory.Build.props`・kit の `tools/lint/dotnet.editorconfig` |

kit の詳しい中身は `external/fara-blazor-component/docs/layout-check.md`・`docs/lint.md`。

## 1. レイアウトの自動計測

`npm run layout-check` は、サンプルの記事（`samples/content`）で静的サイト（`site-production`）を作り、配って計測する。`--no-build` を付けると、作成済みの `site-production` をそのまま計測する。終了コードは 0（違反なし）・1（違反あり）・2（計測できなかった）。

- 対象（`layout-check.config.mjs`）: トップ・記事（サンプル）・一覧（2026 年 1 月）・ワールド・タグ・服・問い合わせ・プライバシーポリシー・404（`/404.html`）。幅は 360・390・768・1024・1440px。
- メニュー（`menuEsc`）: `buttonSelector` は `.fbc-menu-toggle`、`closeSelector` は `.fbc-drawer-close`（kit 0.7.0 の SiteLayout が、スマホのドロワーの左上に出す閉じるボタン）。ドロワーはメニューボタンを覆うため、閉じる手段は閉じるボタンで測る（`menu-close-visible`）。`closeSelector` を省くと、覆われたメニューボタンで違反になる（kit 0.7.0 に上げた時点で、18 件出た）。
- 配信: kit の `serve-static.mjs`（`createStaticServer`）で配る。kit の配信は、同じ名前のフォルダ（`/worlds` の `worlds/`）があると、`index.html` を探して 404 になるため、`tools/layout-check.mjs` は、拡張子の無いパスに `<パス>.html` があればそちらを渡す（Cloudflare と同じく `.html` を先に見る）。kit 側の直し（kit の docs の「`/works` → `works.html`」どおりにする）は、kit の別の変更として扱う。
- `readySelector` は `.mud-layout`（Blazor が描く。静的 HTML には無い）。起動の後にだけ計測する。

### 除外（exclusions）の書き方

除外は、**規則（rule）・要素のセレクター（selector）・理由（reason）**を必ず書く。理由の無い除外は設定エラー（終了コード 2）。`page` を付けると、そのパスだけに効く（付けないとすべてのページ）。

```js
{
  rule: "tap-target",
  selector: ".fbc-report-submit",
  page: "/report",
  reason: "送信ボタンは、確認（Turnstile）が済むまで無効（disabled は pointer-events: none）。無効のボタンは押せないので、計測では当たらない。有効になったときの大きさは kit の CSS（600px 未満で 44px）で決まる",
},
```

まず CSS で直す。除外は、CSS で直せないもの（kit の部品の作り・無効のボタン・計測の前提と合わない要素）だけ。今の除外:

| 規則 | セレクター | ページ | 理由（要約） |
| --- | --- | --- | --- |
| tap-target | `.fbc-report-submit` | `/report` | 送信ボタンは確認が済むまで無効（pointer-events: none）。計測の時点では押せない |
| tap-target | `a.album-card-link` | `/worlds` | カードの題のリンクの当たり判定の下側が、同じカードのタグ（別のリンク）と重なる。配置を変えると見た目が変わるため、別に検討する |

### 直したもの（この PR で）

- 記事・タグ・パンくずのリンク: 行の中のリンクの当たり判定（`padding` と負の `margin`）を広げ、隣の行の文字と重なる分は `position: relative; z-index: 1` で先に受ける。「ホーム」は 600px 未満で左右にも広げる。
- 見出しのリンク（記事の題）: 当たり判定の高さを 44px に。
- フッターの SNS アイコン（600px 未満）: 当たり判定を 44px（`padding` 11px・負の `margin`）にし、アイコンの間隔を 24px（隣と接すると端の 0.5px で隣が取るため）。見た目の大きさは変えない。
- 問い合わせフォームの入力欄: キーボードのフォーカスを枠（`outline`）で示す（MudBlazor は下線の色だけだった）。

## 2. ESLint

`eslint.config.mjs`。共通の中身は kit の `tools/lint/eslint.config.shared.mjs`。既定の `shared` は使わず、`sharedIgnores` と、対象ごとの `createSharedConfig` を書く（Node のグローバルを画面の JS に当てないため）。

- Node（`tools/`・`worker/`・`.github/scripts/`・`tests/js/`・`eslint.config.mjs`）と、ブラウザ（`src/FaraVrcAlbum.Web/wwwroot/js/`。classic script）。
- `tools/verify/lib.mjs`・`report-form.mjs` だけ、Node とブラウザの両方（`page.evaluate` に渡す関数が `window`・`document` を使う）。
- 無視: `external/**`（kit は kit の CI で lint する）・`bin.unix`・`obj.unix`・`site-*`・`dist`・`verify-out`（生成物）。

違反は直す。`eslint-disable` は使わない。どうしても直せないものは、設定の `overrides` に理由つきで書く。

## 3. C# のアナライザー（ラチェット）

方針: 違反が 0 件のルールだけ error（新しい違反を止める）。

`Directory.Build.props` が、kit v1.0.1 の `dotnet.editorconfig`（下の表の 9 ルールはすべて error）を読み、そのあとに album 独自の `tools/lint/dotnet.album.editorconfig` を読む。kit v1.0.1 で kit 側も error になったので、album の上書きは kit に無い VSTHRD200 だけ（`global_level = 1`、kit は 0）。`Microsoft.VisualStudio.Threading.Analyzers` 17.14.15（`PrivateAssets="all"`）。

error にしたルールは、違反を入れたファイルで、`dotnet build` が error を出すことを確かめた（CS4014・VSTHRD100・CA1816・IDE0059 は 2026-10-29、CA1031・CA2000・VSTHRD002・VSTHRD003・VSTHRD103 は album 側で上げたときに確認。kit v1.0.1 で kit 側も error になった。確かめたファイルは消した）。

| ルール | kit | album | album の違反 | 中身 |
| --- | --- | --- | --- | --- |
| CS4014 | error | error | 0 | async のメソッドの中で await していない呼び出し |
| VSTHRD100 | error | error | 0 | `async void`（WPF のイベントの形でも報告される。起動の読み込みは `Task` を返すメソッドにした） |
| CA1816 | error | error | 0 | Dispose で `GC.SuppressFinalize` を呼んでいない |
| IDE0059 | error | error | 0 | 使われない代入 |
| CA1031 | error | error | 0 | 一般の例外（`Exception`）の catch。最上位のハンドラー 4 か所だけ、理由つきで抑止（下） |
| CA2000 | error | error | 0 | Dispose 漏れ。所有権を渡している 3 か所だけ、理由つきで抑止（下） |
| VSTHRD002 | error | error | 0 | 非同期の処理を同期で待つ。同期の入口 3 か所だけ、理由つきで抑止（下） |
| VSTHRD003 | error | error | 0 | 他のスレッドで開始した Task を await している。誤検知 2 か所だけ、理由つきで抑止（下） |
| VSTHRD103 | error | error | 0 | async のメソッドの中で、同期の呼び出しを使っている（非同期版を使う） |
| VSTHRD200 | （既定） | error | 0 | `Async` の接尾辞（命名の規則。0 件にしたので error にした） |

抑止（`#pragma warning disable`。すべて理由のコメントつき・その 1 文だけ）:

- CA1031: `Tools/Program.cs`（CLI の最上位）・`Desktop/App.xaml.cs`（起動の最上位）・`ContentEditor.TryRestore`（1 件の失敗で戻しを止めない）・`DesktopPreferences.NotifyRecovered`（通知先の例外を設定の読み書きに伝えない）
- CA2000: `WorldImageService.CreateClient`（Throttle は HttpClient が持つ）・`ConnectIpv4FirstAsync`（Socket は NetworkStream が持つ。失敗は catch で破棄）・`Web/Program.cs`（アプリの存続中の HttpClient）・`WorldFetchTests.Client`（テスト用の HttpClient を返す）
- VSTHRD002: `Build/PageRenderer`（同期 API。`Render`・`RenderFooter`）・`LaunchOptions.CreateSession`（WPF の OnStartup は同期）
- VSTHRD003: `BackgroundTasks.ObserveAsync`（引数の Task を待つのが目的）・`IngestPlanTests.GatedDialogs`（テストの門）

件数は、`FaraVrcAlbum.sln`（src・tests）と `src/FaraVrcAlbum.Desktop`（WPF。sln の外）を `--no-incremental` でビルドし、`ファイル・行・ルール` で重複を除いた数。kit（`external/`）の分は含めない（kit の docs の件数。kit の分の warning は kit 側で扱う）。

ラチェットに入れていないもの: `CA2012`（この SDK では検出しなかった）・`CA2007`（使わない）。コンパイラーの警告（CS8602 など）と xUnit の解析は、今は 0 件。

### 新しい違反が出たら

- error のルールの違反は、直す（ビルドが止まる）。どうしても直せないときは、理由のコメントつきで、その 1 文だけ抑止し、この表の「抑止」に足す。
- album の warning のルールは、今は無い（kit にあって album に無いルールを足すときは、先に件数を 0 にする）。

## 4. 限界

見た目（余白・揃え・色のコントラスト）・実機の挙動・CLS・設計の妥当性は、これらの検査では保証しない（人・エージェントのレビューで見る）。レイアウト計測は、配信中のサイトの数値の崩れだけを見る。
