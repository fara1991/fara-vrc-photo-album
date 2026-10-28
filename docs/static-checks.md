# 静的解析とレイアウト計測（CI で止める決まり）

レビューで繰り返し見つかる指摘を、CI で機械的に止める。仕組みは kit（external/fara-site-kit）の共通部品。ここには、このサイトの設定・件数・除外の書き方だけを書く。

| 検査 | 手元のコマンド | CI の job | 設定 |
| --- | --- | --- | --- |
| レイアウトの自動計測（tap-target・横スクロール・フォーカス・文字の大きさ・Esc のメニュー） | `npm run layout-check` | layout-check | `layout-check.config.mjs`・`tools/layout-check.mjs` |
| ESLint（JavaScript） | `npm run lint` | lint | `eslint.config.mjs` |
| C# のアナライザー（ラチェット） | `dotnet build FaraVrcAlbum.sln -c Release` | build | `Directory.Build.props`・kit の `tools/lint/dotnet.editorconfig` |

kit の詳しい中身は `external/fara-site-kit/docs/layout-check.md`・`docs/lint.md`。

## 1. レイアウトの自動計測

`npm run layout-check` は、サンプルの記事（`samples/content`）で静的サイト（`site-production`）を作り、配って計測する。`--no-build` を付けると、作成済みの `site-production` をそのまま計測する。終了コードは 0（違反なし）・1（違反あり）・2（計測できなかった）。

- 対象（`layout-check.config.mjs`）: トップ・記事（サンプル）・一覧（2026 年 1 月）・ワールド・タグ・服・問い合わせ・プライバシーポリシー・404（`/404.html`）。幅は 360・390・768・1024・1440px。
- 配信: kit の `serve-static.mjs`（`createStaticServer`）で配る。kit の配信は、同じ名前のフォルダ（`/worlds` の `worlds/`）があると、`index.html` を探して 404 になるため、`tools/layout-check.mjs` は、拡張子の無いパスに `<パス>.html` があればそちらを渡す（Cloudflare と同じく `.html` を先に見る）。kit 側の直し（kit の docs の「`/works` → `works.html`」どおりにする）は、kit の別の変更として扱う。
- `readySelector` は `.mud-layout`（Blazor が描く。静的 HTML には無い）。起動の後にだけ計測する。

### 除外（exclusions）の書き方

除外は、**規則（rule）・要素のセレクター（selector）・理由（reason）**を必ず書く。理由の無い除外は設定エラー（終了コード 2）。`page` を付けると、そのパスだけに効く（付けないとすべてのページ）。

```js
{
  rule: "tap-target",
  selector: ".fsk-report-submit",
  page: "/report",
  reason: "送信ボタンは、確認（Turnstile）が済むまで無効（disabled は pointer-events: none）。無効のボタンは押せないので、計測では当たらない。有効になったときの大きさは kit の CSS（600px 未満で 44px）で決まる",
},
```

まず CSS で直す。除外は、CSS で直せないもの（kit の部品の作り・無効のボタン・計測の前提と合わない要素）だけ。今の除外:

| 規則 | セレクター | ページ | 理由（要約） |
| --- | --- | --- | --- |
| tap-target | `.fsk-report-submit` | `/report` | 送信ボタンは確認が済むまで無効（pointer-events: none）。計測の時点では押せない |
| tap-target | `a.album-card-link` | `/worlds` | カードの題のリンクの当たり判定の下側が、同じカードのタグ（別のリンク）と重なる。配置を変えると見た目が変わるため、別に検討する |

### 直したもの（この PR で）

- 記事・タグ・パンくずのリンク: 行の中のリンクの当たり判定（`padding` と負の `margin`）を広げ、隣の行の文字と重なる分は `position: relative; z-index: 1` で先に受ける。「ホーム」は 600px 未満で左右にも広げる。
- 見出しのリンク（記事の題）: 当たり判定の高さを 44px に。
- フッターの SNS アイコン（600px 未満）: 当たり判定を 44px（`padding` 11px・負の `margin`）にし、アイコンの間隔を 24px（隣と接すると端の 0.5px で隣が取るため）。見た目の大きさは変えない。
- 報告フォームの入力欄: キーボードのフォーカスを枠（`outline`）で示す（MudBlazor は下線の色だけだった）。

## 2. ESLint

`eslint.config.mjs`。共通の中身は kit の `tools/lint/eslint.config.shared.mjs`。既定の `shared` は使わず、`sharedIgnores` と、対象ごとの `createSharedConfig` を書く（Node のグローバルを画面の JS に当てないため）。

- Node（`tools/`・`worker/`・`.github/scripts/`・`tests/js/`・`eslint.config.mjs`）と、ブラウザ（`src/FaraVrcAlbum.Web/wwwroot/js/`。classic script）。
- `tools/verify/lib.mjs`・`report-form.mjs` だけ、Node とブラウザの両方（`page.evaluate` に渡す関数が `window`・`document` を使う）。
- 無視: `external/**`（kit は kit の CI で lint する）・`bin.unix`・`obj.unix`・`site-*`・`dist`・`verify-out`（生成物）。

違反は直す。`eslint-disable` は使わない。どうしても直せないものは、設定の `overrides` に理由つきで書く。

## 3. C# のアナライザー（ラチェット）

方針: 今、違反が 0 件のルールだけ error（新しい違反を止める）。違反が残るルールは warning のまま（件数は下）。

`Directory.Build.props` が kit の `dotnet.editorconfig` を読む（error と warning の内容は kit のものをそのまま使う）。`Microsoft.VisualStudio.Threading.Analyzers` 17.14.15（`PrivateAssets="all"`）。

error にしたルールは、違反を入れたファイルで、`dotnet build` が error を出すことを確かめた（2026-10-29。確かめたファイルは消した）。

| ルール | 重大度 | album の違反 | 中身 |
| --- | --- | --- | --- |
| CS4014 | error | 0 | async のメソッドの中で await していない呼び出し |
| VSTHRD100 | error | 0 | `async void`（WPF のイベントの形でも報告される。起動の読み込みは `Task` を返すメソッドにした） |
| CA1816 | error | 0 | Dispose で `GC.SuppressFinalize` を呼んでいない |
| IDE0059 | error | 0 | 使われない代入 |
| CA1031 | warning | 7 | 一般の例外（`Exception`）の catch |
| CA2000 | warning | 40 | Dispose 漏れ（大半はテストの使い捨てのオブジェクト） |
| VSTHRD002 | warning | 9 | 非同期の処理を同期で待つ（`.Result`・`.Wait()` など） |
| VSTHRD003 | warning | 2 | 他のスレッドで開始した Task を await している |
| VSTHRD103 | warning | 16 | async のメソッドの中で、同期の呼び出しを使っている（非同期版を使う） |

件数は、`FaraVrcAlbum.sln`（src・tests）と `src/FaraVrcAlbum.Desktop`（WPF。sln の外）を `--no-incremental` でビルドし、`ファイル・行・ルール` で重複を除いた数。kit（`external/`）の分は含めない（kit の docs の件数）。

ラチェットに入れていないもの（kit と同じ理由）: `VSTHRD200`（Async の接尾辞。album に 7 件。命名の規則であり、危険ではない）・`CS1998`・`CA2012`（この SDK では検出しなかった）・`CA2007`（使わない）。

### 新しい違反が出たら

- error のルール（CS4014・VSTHRD100・CA1816・IDE0059）の違反は、直す（ビルドが止まる）。
- warning のルールは、増やさない。直せるものは直し、件数を上の表に反映する（減らしたら減らした数を書く）。
- 件数の残る warning を error にするのは、件数を 0 にしてから。

## 4. 限界

見た目（余白・揃え・色のコントラスト）・実機の挙動・CLS・設計の妥当性は、これらの検査では保証しない（人・エージェントのレビューで見る）。レイアウト計測は、配信中のサイトの数値の崩れだけを見る。
