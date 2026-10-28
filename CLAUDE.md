# CLAUDE.md

Fara Photo Album（VRChat の写真ブログ。本番 https://vrc-album.fara-labs.com/ ・確認用 https://vrc-album-dev.fara-labs.com/）。.NET 10 / Blazor WebAssembly + MudBlazor（共通部品は fara-site-kit。`external/fara-site-kit` の submodule、v0.6.0 に固定）の静的サイトを Cloudflare Workers（静的アセット + 小さな Worker）に公開する。写真は R2 の独自ドメイン（https://vrc-photos.fara-labs.com/）から配信する。設計は計画書（作業者の手元: `Temp/plan-vrc-album/plan.md`）。Cloudflare の手作業は docs/cloudflare-setup.md。

## ブランチ運用（必ず守る）

- 作業ブランチは `develop` から作成する（`master` から切らない）
- 作業ブランチの PR は `develop` 向けに作る
- `master` へは `develop` からのみマージする
- **サイトの公開は GitHub Actions では行わない**（このリポジトリはパブリックにするため、本物の記事 `content/` を Git に入れていない。Actions で公開すると空のサイトで本番を上書きしてしまう）。公開は、手元のデスクトップアプリの「公開」タブ（または `node tools/site.mjs deploy <develop|production>`）から行う。master／develop への push で公開されることは無い。本番の初回は `MAINTENANCE_MODE=true` のメンテナンス画面のまま作られる（公開してよくなったら `MAINTENANCE_MODE` を切り替える）
- master へのマージはユーザーが行う。Claude は develop → master の PR の作成までで、master へはマージしない
- ワークフローは `build.yml` だけ（PR と、develop・master への push。ビルドとテストと公開物の検査・`wrangler deploy --dry-run` まで。公開しない・Cloudflare のシークレットも使わない）。job は build（ビルド・テスト・検査）・lint（ESLint）・layout-check（レイアウトの自動計測。ubuntu・chromium）`.github/scripts/workflows.test.mjs` が「ワークフローは build.yml だけ・公開しない」を確かめる。CI は本物の `content/` が無いので、架空のサンプル `samples/content` で通す（`--content samples/content`）
- **リポジトリはパブリックにする予定**（今は private。2026-10-28 に、以前のリポジトリを `fara-vrc-photo-album_backup`（private）に改名し、履歴をまとめた新しいリポジトリを作った）。**人名・本物の `content/`・アバター情報は、Git にも履歴にも入れない**。private + 無料プランのためブランチ保護は使えない（パブリックにすれば使える）

## 構成メモ

- 記事データは `content/posts/{slug}.json`（**Git 管理外**。`.gitignore` の `/content/`。手元と R2 の控え用バケット（「同期」タブ）で持つ。形式は `src/FaraVrcAlbum.Content/ContentLoader.cs` と `Shared/Models.cs`、例は docs/content-format.md。未知の項目・null・重複したキーはエラー）。マスタは `content/worlds.json`（ワールド ID → name・aliases。記事の `world.id` が無ければビルドエラー）と `content/tags.json`（タグの slug → 表示名）。CI 用の架空のサンプルは `samples/content`（Git 管理。slug は `sample-` で始まる。テストは本物の `content/` があればそれ、無ければサンプルを検査する: `tests/FaraVrcAlbum.Content.Tests/RepoRoot.cs` の `ContentDir()`）。予約公開の確認が要るときは、公開日が未来の記事を一時的に作る
- **人名は表示しない**: 撮影者・写っている人の名前と人物 ID（`p_`）・VRChat のユーザー ID（`usr_`）は、記事の JSON にも静的 HTML にも `data/` にも入れない（人物データ・`/people`・`author`/`people` の項目は廃止）。ワールド名は表示する（`content/worlds.json` のマスタ・`nameAt`。更新は `album draft --apply`）。`verify-site.mjs` が出力の人名を止める
- 環境による出し分け（`--env`）: 公開日が未来の記事（予約公開）は本番に出さず、確認用に出す（draft = 非公開は、どちらの環境にも出さない。アプリの「公開する」「非公開にする」で切り替える）。**予約公開は次のビルドまで出ない**（時刻での自動公開は無い）。アーカイブは公開日の JST の月。一覧系のページ（一覧・アーカイブ・タグ・ワールド）は、載せる記事がすべて noindex なら noindex
- 静的 HTML のページ: `/`・`/page/n`・`/posts/{slug}`・`/archive/{yyyy}/{MM}`（+ `/page/n`）・`/worlds`・`/worlds/{uuid}`（`wrld_` を除いた UUID。+ `/page/n`）・`/tags`・`/tags/{tag}`・`/privacy`・`/report`。Build が出した URL には、必ず Web の `@page` のルートが要る（`SiteBuilderTests.EveryStaticPageHasAMatchingBlazorRoute` が確認）。`feed.xml`（RSS）も Build が出す
- `src/FaraVrcAlbum.Shared`（モデル・規則・日付。ブラウザとビルドで共用）／`Content`（ビルド時のみ: 検証・Markdown）／`Pages`（Razor クラスライブラリ。Web と Build の HtmlRenderer で共用。素の HTML だけ・MudBlazor 不可・リンクは相対）／`Web`（Blazor WASM）／`Build`（CLI: content → data/*.json + 静的 HTML。kit の build-pages を呼ぶ）
- `Build` の出力は環境ごと（`--env production|develop`）。`tools/site.json`（名前・ホスト・CSP・cacheRules）が kit の設定。`img-src` の画像ドメインは `appsettings.json` の `ImageBaseUrl` から自動で入る
- 保存のキー `fara-vrc-album.settings` / `.sidebar-hidden` は、`Services/SiteInfo.cs`・`wwwroot/index.html`・`wwwroot/js/theme-default.js` の 3 か所で同じ（`tests/js/theme-default.test.mjs` が確認）。既定のテーマはダーク
- スタイルは `wwwroot/css/app.css` 1 ファイル。ヘッダーの名前は kit 既定の文字（サイト名『Fara Photo Album』。`appsettings.json` の `SiteName`。title・og:site_name・RSS・フッター・プライバシーポリシーと同じ）。画像（`images/header-{ハッシュ}.webp`。文字『Fara』入り）は、すべてのページの本文の最初に本文の幅いっぱいで出す（`SiteBanner`。静的 HTML にも入れる。docs/header-image.md）
- M2: 記事の写真は `<a class="album-photo-link" href="2048px"><img></a>`（JS なしでもフル画像へ）。`wwwroot/js/lightbox.js`（ネイティブ dialog・document で受ける・`#photo-N`。純粋な関数は tests/js/lightbox.test.mjs）が引き継ぐ。ギャラリーは枚数で `album-gallery--1|2|n`（CSS だけ・`--ar`）。一覧のカードは 640 / 1280 の srcset（3:2 の枠）。前後・関連の記事は `SiteModel.ToPage` が環境に出す記事の中だけから決める
- ヘッダーの高さは kit 既定の 3.5rem（`--album-header-h`。PC・スマホとも同じ。tests/js/header-css.test.mjs が値を固定）。本文の画像は縦横比 4:1（`.album-banner`）。画像は 960×240 の WebP（元の 1920×480 から `tools/make-header-image.py` で作る）
- ギャラリーの写真の `sizes` は枚数・縦横比別（`SiteRules.GallerySizes`。640 で足りる小さい写真は 640 を取る）。本文内のリンク（キャプション・パンくず・日付・タグ）は、行の高さを変えずに padding と負の margin で当たり判定を 44px 以上にしている（app.css）。axe の全ページ確認は、サンプル記事に依存していた m2-check とともに削除（tools/verify/README.md）
- ライトボックスは URL のハッシュ（`#photo-N`）を **replaceState** で更新する（履歴を増やさない。Back では閉じない）。pushState + popstate にすると Blazor の LocationChanged が focusMain を呼んでフォーカス戻しが壊れ、記事ページの再読み込みも起きるため。画像の URL は同一オリジンか data/site.json の imageBaseUrl のホストだけ許可する（`isAllowedImageUrl`）
- **ワールドの画像と情報**: 画像は `album worlds --apply`（手元・R2 の鍵が要る）が VRChat から取って R2 の `worlds/{uuid}/…_512.webp`・`_1024.webp` に置き、`content/worlds.json` の `image` に書く。情報（訪問数など。今いる人数は除く）は `content/world-stats.json`。自動の更新は無い（以前の GitHub Actions の `update-data.yml` は、パブリック化のときにやめた）。更新は、アプリの公開タブの「サイトの画像情報を更新する」か、手元の `album world-stats --apply`。VRChat の API は User-Agent `FaraVrcAlbum/1.0 (Fara; game_fara@outlook.com)`・約 60 秒おき・429 で止める。ビルド・公開・閲覧では VRChat を読まない。作者の表示名はワールドの作者へのクレジットとして出す（写っている人・撮影者の名前を出さない決まりとは別）。記事のキャプションのワールド名は、VRChat のワールドページへのリンク（サイト内の /worlds へのリンクは出さない）
- 画像の基準 URL: 確認用も本番も `ImageBaseUrl`（R2）。確認用は本番と同じ写真を見る（合成画像は作らない。`DevelopImageBaseUrl` と `tools/dev-photos` は廃止）。og:image・JSON-LD・CSP も同じ URL（`SiteSettings.PublicImageBaseUrl`）。実写真は Git に入れない
- 公開設定は `wrangler.jsonc`（本番はトップレベル、確認用は `env.develop`）。`run_worker_first` は配列（docs/cloudflare-setup.md）。wrangler の版は `package.json` で固定。`keep_vars` はトップレベルだけ
- 公開の関門: `.github/scripts/verify-site.mjs`（本番/確認用の出力の取り違え。`/*` のルールの中だけ noindex を見る。**出力に `sample-` の記事があると止める**: `ALBUM_ALLOW_SAMPLE=1` を付けた CI のビルドだけ通す）、`require-maintenance.mjs`（デスクトップアプリの本番の公開は `MAINTENANCE_MODE` が `notice`／`true` のときだけ。Worker が 404（無い・名前の打ち間違いも同じ）は止める。初回の本番の公開だけ `ALLOW_MISSING_WORKER=1`（または `true`。大文字小文字は問わない）で通す。判定は pokemon 版と同じ）。Build は `content/` のフォルダが無いと**失敗する**（空のサイトを作らない。`SiteBuilder.Run`）
- ワークフローを含む push は SSH で行う（gh の token に workflow スコープが無い）
- **写真の取り込み（M3）は手元の CLI `album`**（`src/FaraVrcAlbum.Tools`。`npm run album -- help`。手順は docs/album-tool.md）。`src/FaraVrcAlbum.Images`（NetVips・AWSSDK.S3）は Tools・Desktop.Core と、そのテストだけが使い、Web・Build・Pages には載せない。CI では画像処理もアップロードもしない
- **記事の中身（content）のフォルダは設定（`local/album.local.json` の `contentDir`）で変えられる**（既定はリポジトリの `content`。設定したフォルダが無いときは黙って切り替えずエラー。Build は `--content` で指定し、設定ファイルは読まない）。**アプリの「公開」タブ（第 2 段階。`node tools/site.mjs deploy`・`SitePublishService`・記録は `local/publish-state.json`）はできたが、公開は常にこのアプリ／`tools/site.mjs` から行う（Actions では公開しない）**。本番の公開は、確認用（dev）に今の内容を公開済みのときだけ・MAINTENANCE_MODE の確認つき。R2 への控え（スナップショット）は、**写真のバケットとは別の公開しないバケット**（`backupBucket`。空・同じ名前は拒否）にだけ置く。詳しくは docs/album-desktop.md の設定の節
- 台帳 `content/catalog/photos.json`（元写真の内容ハッシュ → キー・状態・寸法・色・ワールド ID）は `content/` の一部（Git 管理外）。元ファイル名の控えを含む。Build は読まない
- **`_og.jpg` はカバー写真だけ**（`album cover <key> --apply`）。`verify` と `audit` も表紙だけを見る
- 写真の取り込みは既定がドライラン。`--apply` が無いと R2・台帳・記事に書かない。バケット名は `^[a-z0-9][a-z0-9-]{2,62}$`
- **デスクトップ版（取り込み（アップロードまで）・記事（下書きと編集）・アバター・服・管理・設定の画面。`npm run desktop`）**: `src/FaraVrcAlbum.Desktop`（WPF・net10.0-windows。Windows 専用。**`FaraVrcAlbum.sln` に入れない**＝ubuntu の CI の `dotnet build` を壊さないため）と `src/FaraVrcAlbum.Desktop.Core`（ViewModel とロジック。net10.0。sln 内。テストは Linux でも動く）。ロジックは Tools の公開 API を呼ぶだけで、CLI と同じ安全策。`--demo <作業フォルダ>` は合成データで R2 に接続しない起動（実バケットには触れない）。詳細は docs/album-desktop.md
- `verify` の対象は記事の写真（非公開の記事も含む。公開したときに写真が揃っているように）。`verify --ledger` で、記事に無い台帳（uploaded）の写真も加える。対象が 0 件のときは理由を出す。

- **アバターと服**: `content/avatars.json`（アバター名 → 着せている服の id の順序つき）と `content/outfits.json`（服の名前・任意で店名と https の URL）。写真の `avatar` / `outfits` は id だけ。キャプションには服の名前だけを出し、アバター名は出さない（静的 HTML・data に入れない。テストで確認）。写真へのアバターの付与は手で行う（VRChat のログは使わない・写真の XMP にアバターは無い）。`album avatar|outfit|photo-avatar`（docs/album-tool.md）。

## 運用メモ（アプリでの公開・同期・取得。変更のたびに、ここと docs/ を更新する）

- **公開（dev・本番）は、記事の中身をサイトにするだけ**。ワールドの画像・情報（VRChat）は、公開とは別のボタン「サイトの画像情報を更新する」（公開タブ）で事前に行う（`WorldFetchService`・`PublishViewModel`）。**服の画像・情報（BOOTH Library Manager）は、「アバター・服」タブの「フォルダから服を読む」ウィンドウの「インポート」で取り込む**（未登録は登録と同時に・登録済みは差分（足りない項目だけ）か、「上書き」で再インポート。`AvatarOutfitService.OutfitFillFromBooth` の overwrite / onlyIds。`FolderImportViewModel.RegisteredViaImport`）。公開タブの出力の欄は、行が増えたら自動で下までスクロールする取れなかったワールドは `local/world-fetch-state.json` に記録して 24 時間待つ。VRChat へは約 60 秒おき・429 で止める・UA を名乗る。接続は IPv4 優先（`WorldImageService.ConnectIpv4FirstAsync`。この PC は files.vrchat.cloud への IPv6 だけ切られる）
- **記事は「公開」か「非公開」**（JSON の `draft`）。非公開はどの環境にも出さない。「記事」画面の「公開する」「非公開にする」「記事を削除する」で切り替え・削除（削除は `local/deleted-posts` に残す。写真（R2）は消さない）
- **公開の前に、アプリの「同期」タブで content が R2 と揃っているか確かめる。公開前のガードは Git の作業ツリーだけを見る（content/ は Git 外）**（`tools/publish-guard.mjs`。docs/album-desktop.md の「公開」の節）
- **「同期」タブ**（`ContentSyncService`・`SyncViewModel`）: `content/` の JSON をローカルと R2 の控え用バケットの `sync/` で比べ、ファイルごとに送る・取り込む・消す（相手側に無いファイルだけ）を選ぶ。上書き・消す前の内容は `local/sync-backups` に残し、ローカルを変えたあとの content が読めなければ元に戻す。履歴用のスナップショット（`snapshots/`）は別
- **R2 の一覧は、該当が無いと S3Objects が null**（AWS SDK）。`S3ObjectStore.ObjectsOf` で空として扱う
- 終わったことは `IDialogService.InformAsync` の完了ダイアログで知らせる（同期・取得）
- アプリが起動中だと Desktop の DLL がロックされるので、画面（XAML）のビルド確認は `-p:OutDir=<別の場所>` で行う
- **パブリック化の経緯（2026-10-28 実施）**: `content/` を Git から外し、Actions の公開（deploy.yml・deploy-develop.yml・update-data.yml）をやめ、CI はサンプルで通す形にした。履歴には本人のユーザー ID・写真の記録・アバター名が残っていたため、履歴を書き換えるのでなく**新しいリポジトリ（履歴は 1 コミットにまとめ）**にした（GitHub は `refs/pull/*` に古い履歴を残すため）。以前のものは `fara-vrc-photo-album_backup`（private）。ワールドの情報の更新は、毎日の Actions をやめて、アプリの「サイトの画像情報を更新する」ボタンに一本化した

## 確認

```bash
dotnet test           # Content / Build（node と submodule が要る）/ Web（bUnit）
npm test              # Worker・kit の worker・公開の関門・wrangler.jsonc・ブラウザ用 JS
npm run check         # publish → 本番・確認用の出力 → verify-site → wrangler deploy --dry-run（両環境）
npm run preview       # Cloudflare と同じ動きで http://localhost:8787（確認用は preview:develop）
npm run lint          # ESLint（tools・worker・.github/scripts・tests/js・画面の JS。kit の共通設定）
npm run layout-check  # レイアウトの自動計測（サンプルで静的サイトを作って計測。違反があれば終了コード 1）
```

- 静的解析（ESLint・C# のアナライザー）とレイアウト計測の決まり・除外の書き方・C# のルールの件数は docs/static-checks.md
- ブラウザでの確認（Playwright）は tools/verify/README.md（`playwright-core` は devDependency。1.64.1 に固定）
- 公開前の手動確認（CORS・ホットリンク・axe・ヘッダー・ライトボックス）は docs/verification.md
- ローカルの `dotnet publish` の ILLink がたまに落ちることがある（他サイトと同じ）。コードの問題ではない。`tools/site.mjs`（アプリの「公開」・`npm run site` 系）は、ILLink が原因の失敗だけ、自動で最大 3 回までやり直す（`tools/publish-retry.mjs`）。それでも落ちたら、再実行する（`npm run clean` で bin/obj を消してからでもよい）
