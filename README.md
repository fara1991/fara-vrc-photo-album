# fara-vrc-photo-album

**Fara Photo Album**: VRChat で撮った写真のブログ（本番 https://vrc-album.fara-labs.com/ ・確認用 https://vrc-album-dev.fara-labs.com/）。C# / .NET 10 の Blazor WebAssembly + MudBlazor（共通部品は [fara-site-kit](https://github.com/fara1991/fara-site-kit)）で作った静的サイトを Cloudflare Workers に公開します。写真は R2（https://vrc-photos.fara-labs.com/）から配信します。

現在は **M0（リポジトリ・骨組み・デプロイの関門）** です。記事の一覧・記事ページ・メンテナンス表示・プライバシー・報告まで。ライトボックス・ワールド・タグ・アーカイブ・検索なし・画像のアップロードツールは後続のマイルストーンで足します。

- 記事は `content/posts/*.json`（Markdown 本文）。ビルド（`FaraVrcAlbum.Build`）が、本文入りの静的 HTML（検索エンジン・JavaScript の無い環境向け）とアプリが読む `data/*.json` を作ります。ブラウザは起動すると静的 HTML を引き継ぎます。
- サーバー・データベースは使いません（Worker は、メンテナンスの状態・報告の受け口だけ）。

## フォルダ構成

```
fara-vrc-photo-album/
├── FaraVrcAlbum.sln / global.json / Directory.Build.props
├── appsettings.json                  サイト名・画像の基準 URL・1 ページの件数
├── content/posts/                    ★ 記事（Git 管理外。手元と R2 の控えで持つ）
├── samples/content/                  CI 用の架空のサンプル記事（Git 管理）
├── external/fara-site-kit/           共通部品（submodule。v0.8.0）
├── src/
│   ├── FaraVrcAlbum.Shared/          モデル・規則・日付（ブラウザとビルドで共用）
│   ├── FaraVrcAlbum.Content/         ビルド時のみ: content の検証・Markdown
│   ├── FaraVrcAlbum.Pages/           ページの部品（Web と Build の HtmlRenderer で共用）
│   ├── FaraVrcAlbum.Web/             Blazor WebAssembly
│   └── FaraVrcAlbum.Build/           content → data/*.json + 静的 HTML（kit の build-pages）
├── tests/                            Content / Build / Web（bUnit）/ js（node --test）
├── tools/                            site.json（kit の設定）・site.mjs（ビルド・check・preview）・verify/（Playwright）
├── worker/                           Worker（メンテナンス・報告）と、そのテスト
├── wrangler.jsonc / package.json     Cloudflare Workers の公開設定（本番 / develop）・wrangler の版固定
├── docs/                             cloudflare-setup.md・privacy-and-takedown.md・header-image.md
└── .github/                          workflows/（build.yml だけ。公開はしない）・scripts/（verify-site・require-maintenance と、そのテスト）
```

## 公開（デプロイ）

| 公開先 | タイミング |
| --- | --- |
| https://vrc-album-dev.fara-labs.com/（確認用） | デスクトップアプリの「公開」タブ（`node tools/site.mjs deploy develop`）から。Actions では公開しない |
| https://vrc-album.fara-labs.com/（本番） | 同じく手元から（`node tools/site.mjs deploy production`。確認用に公開済みのときだけ）。**初回はメンテナンス画面のまま**作られる |

テスト・検証が通らなければ公開されません。初回の設定（API トークン・Access・R2・ドメイン）は [docs/cloudflare-setup.md](docs/cloudflare-setup.md)。

## ブランチ運用

```
feature/xxx ──PR──▶ develop ──PR──▶ master ──(自動)──▶ vrc-album.fara-labs.com
                       │
                       └──(自動)──▶ vrc-album-dev.fara-labs.com
```

作業ブランチは **develop から** 作成して **develop へ** マージする。リリースは **develop → master**（ユーザーがマージ）。

## ローカルで動かす

```bash
git submodule update --init       # 初回のみ（external/fara-site-kit）
npm ci                            # 初回のみ（wrangler）
dotnet test                       # Content / Build / Web
npm test                          # Worker・公開の関門・wrangler.jsonc・ブラウザ用 JS
npm run preview                   # dotnet publish → 本番用の出力 → wrangler dev（http://localhost:8787）
npm run check                     # 公開設定の検証（Cloudflare にはつながない）
```

## デスクトップ版（写真の取り込み・記事・マスタの管理）

Windows 用の画面アプリです（`npm run desktop`。詳しくは [docs/album-desktop.md](docs/album-desktop.md)）。取り込み（VRChat の写真のアップロード）・記事（日付ごとに作成・編集）・アバター・服・管理・設定の画面があります。

- **服のインポートは、BOOTH Library Manager を前提にした機能です。** 公式アプリが手元に持つ商品データ（`%APPDATA%\pm.booth.library-manager\data.db`）を読んで、カテゴリ「3D モデル」→「3D 衣装」の商品だけを、商品名・お店・商品ページの URL つきで一覧にします。この一覧は、このアプリが作ったものではなく、公式アプリが持っているデータです（BOOTH のサイトには問い合わせません）。BOOTH Library Manager を使っていない場合は、カテゴリでの絞り込みはできず、フォルダ直下の名前がそのまま出ます（お店・URL は手で入れます）。
- アバターのインポートは、Unity のフォルダの `.prefab` のファイル名を読みます。登録済みの服の名前から、着ている服を予測して紐づけることもできます。
- 設定の「Cloudflare R2 のバケット名」は、最初は空です。入力するまで、写真の保存先には接続しません。

記事の追加は `content/posts/{slug}.json` を 1 つ足します（形式は `Shared/Models.cs`。slug は小文字・数字・ハイフン、写真の key は `yyyy-mm/yyyyMMdd-HHmmss-fff_ハッシュ8桁`）。間違いは `dotnet test`・Build が止めます。

ライセンス: コードは MIT。写真・記事の内容は対象外です。
