# ヘッダー画像（Brand スロット）の確認結果（M0）

計画書 §4.2.1 の確認項目。M0 では **仮の SVG（`wwwroot/images/fara-album-header.svg`。白い文字 "Fara Album"）** で差し込み口を確かめた。作者の既存のヘッダー画像は未受領（M2 で差し替え。形式・寸法・透過・ファイルサイズを、このファイルに追記する）。

測定: 以前は `tools/verify/m0-smoke.mjs`（PC 1280・スマホ 390 × ダーク・ライト）で行った（そのスクリプトはリポジトリに無い）。

| 項目 | 結果 |
| --- | --- |
| 差し込み方 | `SiteLayout` の `Brand` に `<a class="fsk-brand album-brand" href="./" title="ホームへ"><img alt="Fara Album" width="168" height="28" fetchpriority="high"></a>`。kit の変更は不要。kit の文字リンクとは重ならない（`a.fsk-brand` は 1 つ）。テストは `tests/FaraVrcAlbum.Web.Tests/WebTests.cs` |
| ヘッダーの背景（両テーマ） | **同じ**: kit の `--fsk-header-bg` = `linear-gradient(90deg, #140a26, #241243)`（ダーク・ライトとも）。文字は白（`.fsk-brand { color:#fff }`）。→ **画像は 1 枚で足りる**（白系・暗い紫の上で読めること。`prefers-color-scheme` の `<picture>` は使わない） |
| 高さ・収まり | ヘッダー 56px の中で PC 28px（y=14〜42）、スマホ 24px（y=16〜40）。リンクの当たり判定は 44px。横スクロールなし |
| スマホでの幅 | ブランドのリンクは残りの幅を使う（kit の `flex:1 1 auto; min-width:0; overflow:hidden`）。画像 144px で収まる。検索ボタンは M0 では無い（M2 の検索を入れるときに再確認） |
| `.fsk-brand` の指定との相性 | `font-size`・`white-space`・`text-overflow` は画像に無関係。`display:inline-flex; min-height:44px` を `app.css` で足した |
| キーボードのフォーカス枠 | **kit の既定では出ない**: `.fsk-header :focus-visible` は `outline-color` だけで、MudBlazor の reset により `outline-style: none`。ブランドのリンクにフォーカスしても枠が見えない（kit の文字リンクも同じ）。サイトの `app.css` で `.album-brand:focus-visible { outline: 2px solid #e9d5ff }` を足した。**kit 側の修正候補**として申し送り |
| 画像ブロック時の alt | `alt="Fara Album"` がリンクの名前になる（テスト済み）。実画像でのブロック時の表示は M2 で目視 |
| CLS | width/height を付け、`loading` なし。M2 の boot-diff / cls で実測 |
| 静的 HTML 側の枠 | M2 の `StaticFrame` で同じ `<img>` を出す（M0 の静的 HTML は本文のみ） |

## M1 の変更: ヘッダーの表示名は『Fara』（テキスト）

ヘッダーのブランドは文字の **『Fara』だけ**（`SiteInfo.BrandName`。変えるのはこの 1 か所）。`<title>`・`og:site_name`・RSS のタイトル・フッター・プライバシーポリシーは『Fara Album』のまま。M2 で入れるヘッダー画像の `alt` は『Fara』にする。上の M0 の記録（仮の SVG・alt `Fara Album`）は、画像の差し込み方の検証結果として残す（`fara-album-header.svg` はいまは使っていない）。

## M2: 実際のヘッダー画像（決定）

- ファイル: `src/FaraVrcAlbum.Web/wwwroot/images/header-{内容の SHA-256 の先頭 8 桁}.webp`（**960×240 の WebP、約 25 KB**。元は 1920×480 の約 90 KB で、表示は最大 448px 幅なので 2x に縮めた。`python tools/make-header-image.py <元の WebP>` で作る。元の左下にある背景の扉のかすかな明るい四角は、左隣の色でなじませてある。左端・右端は CSS の mask でヘッダーの背景へ溶かす。青空の写真・アバター・文字『Fara』が焼き込み済み。元の写真の PNG は入れない）。ファイル名の 8 桁は `SiteInfo.HeaderImage` と一致させる（`WebTests.TheHeaderImageFileIsHashNamedByItsContentAndIs1920By480` が確かめる）。差し替えたら名前も変える（`tools/site.json` の cacheRules が `/images/header-*.webp` を immutable にしている）
- 差し込み: `MainLayout` の `Brand` に `<a class="fsk-brand album-brand" href="./" title="ホームへ"><img alt="Fara" width="1920" height="480" fetchpriority="high"></a>`。遅延読み込みにしない。両テーマでヘッダーの背景は同じ（kit の紫のグラデーション）なので 1 枚
- 高さ: **PC 112px・スマホ（767.98px 以下）72px**。kit は変更せず `app.css` で上書き: `.fsk-header.mud-appbar`・`.fsk-header .mud-toolbar`（kit は 3.5rem 固定）、`--mud-appbar-height`（`html:root` と `.fsk-page`。MudBlazor のテーマが後から `:root` に出すので詳しさで勝たせる）、本文の上余白（MudBlazor は 600px 未満の縦向きで 7/8 にする）、`html { scroll-padding-top }`（スマホはページ全体がスクロールするので、アンカーがヘッダーに隠れない）
- 画像は高さに合わせて縮め、左寄せ（`object-fit: cover; object-position: left`）。右端の 45% は `mask-image` でヘッダーの背景へゆるやかに溶かし、ヘッダーの背景には画像の色を右へ伸ばす薄い光（radial-gradient）を重ねる（画像の右端で途切れて見えないように。元ファイルの右端は青空のまま）。画像の右の空きは押せない（リンクは画像の幅だけ）
- 画像の `alt` は『Fara』。h1 にはしない（トップは視覚的に隠した h1『Fara Album』）。フォーカス枠は画像の内側（`outline-offset: -3px`）
- 確認: 以前は `tools/verify/m2-check.mjs` で行った（サンプル記事に依存したため削除。git 履歴 `18603ce`）。kit の `.fsk-header` の 3.5rem 固定と、MudBlazor の 7/8 は、kit 側で CSS 変数にする候補（申し送り）

## M3: 画像は各ページの本文の最初へ（現在の形）

- ヘッダーの名前は kit 既定の文字 **『Fara Photo Album』**（サイト名。`appsettings.json` の `SiteName`。`<title>`・`og:site_name`・RSS のタイトル・フッター・プライバシーポリシーと同じ。`SiteLayout` の `Brand` は使わない）。ヘッダーの高さは kit 既定の 3.5rem（`--album-header-h`。PC・スマホとも同じ）。**上の M0〜M2 のヘッダー画像・高さ（112px・72px）・`SiteInfo.BrandName` の記述は、この変更で置き換わった。**
- 画像（`SiteRules.HeaderImage`。960×240 の WebP）は、**すべてのページの本文の最初**に、本文の幅いっぱいに出す（コンポーネント `SiteBanner`、CSS は `.album-banner`）。縦横比は 4:1（`aspect-ratio`）で、画像が来る前から高さが決まる。alt は空（名前は文字で出ているので飾り）。
- **静的 HTML にも同じものを入れる**（`PageCatalog` が全ページの `<main class="entry-static">` の先頭に `SiteBanner` を足す）。起動の前後で画像が動かない（CLS）。
- 表示の幅が 960px を超える画面では拡大して出る（元の画像は 960 幅）。粗く見える場合は 1920 幅の画像に差し替える（`tools/make-header-image.py`）。
