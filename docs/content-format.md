# content/ の形式と、Build が出すもの（M1）

`content/` は Git で管理するデータ。形式は `src/FaraVrcAlbum.Shared/Models.cs`、検証は `src/FaraVrcAlbum.Content/ContentLoader.cs`。**未知の項目・null・同じキーの重複・日時のオフセット無しはビルドエラー**（問題は全部まとめて 1 回で報告される）。

## `content/posts/{slug}.json`

```json
{
  "schema": 1,
  "slug": "2026-10-night-garden",
  "title": "夜の庭を歩いた",
  "publishedAt": "2026-10-24T20:00:00+09:00",
  "updatedAt": null,
  "draft": false,
  "noindex": false,
  "summary": "（省略可。無ければ本文の先頭から自動で 120 文字）",
  "cover": "2026-10/20261023-213312-345_a1b2c3d4",
  "tags": ["night"],
  "body": "Markdown 本文（生の HTML は無効）。",
  "photos": [
    {
      "key": "2026-10/20261023-213312-345_a1b2c3d4",
      "w": 2048, "h": 1152,
      "takenAt": "2026-10-23T21:33:12+09:00",
      "color": "1b2a3c",
      "caption": "（省略可）",
      "world":  { "id": "wrld_00000000-0000-4000-8000-000000000001", "name": "撮影時の名前（控え）" }
    }
  ]
}
```

| 項目 | 規則 |
| --- | --- |
| `slug` | `^[a-z0-9]+(-[a-z0-9]+)*$`（ASCII）。ファイル名と一致、一意。公開後は変えない |
| `publishedAt` | 公開日。**オフセット付き**（`+09:00` か `Z`）。アーカイブ・並び・フィードの基準。過去日でもよい。**未来なら本番に出さない**（確認用には出す） |
| `draft` | **非公開**の印。true はどの環境（確認用・本番）にも出さない。アプリの「公開する」「非公開にする」で切り替える（項目名は `draft` のまま） |
| `noindex` | true の記事は検索に載せない（sitemap・フィードにも入れない）。一覧系のページは、載せる記事がすべて noindex なら noindex |
| `tags` | slug。`content/tags.json` に登録済みであること |
| `photos[].key` | `yyyy-mm/yyyyMMdd-HHmmss-fff_{内容ハッシュ先頭 8 桁}`（再作成の世代は `-r2`）。月とファイル名の日付が一致すること。**2025 年 11 月より前（JST）はエラー**。`takenAt` があれば秒まで key と一致すること |
| `photos[].world` | `id` は `wrld_` + 小文字 UUID で、`content/worlds.json` にあること（無ければビルドエラー）。`name` は撮影時の控え。**画面・公開データの名前はマスタの `name`**。インスタンス ID（`:12345~…`）は形式エラー |

公開しない値（元ファイル名・インスタンス ID・位置・元の XMP）を置く項目は無い。置こうとすると未知の項目としてエラーになる。

## `content/worlds.json`（ワールドのマスタ）

```json
{
  "wrld_00000000-0000-4000-8000-000000000001": { "name": "ワールド名", "aliases": ["旧名", "別表記"] }
}
```

画面（キャプション・`/worlds/{uuid}`・フィード）に出す名前は `name`。`aliases` は表記ゆれ・旧名（検索を入れるときの対象。画面には出さない）。`nameAt`（省略可）は `name` を観測した写真の撮影日時（ISO 8601）。同じ `wrld_` の新しい写真の名前で `name` を更新するのは、その写真の撮影日時が `nameAt` より厳密に新しいときだけで、旧名は `aliases` に残し（新しい名前が `aliases` にあれば外す）、`nameAt` を更新する（`album draft --apply`。ワールドの `id`＝URL は変わらない）。同じ名前をさらに新しく観測したときは `nameAt` だけを進める。`name` と `aliases` の名前は、人物の名前と同じ表示名の規則（制御・書式文字（双方向の制御・ゼロ幅スペース・BOM など）は除く。ZWJ（U+200D）と ZWNJ（U+200C）は残す（絵文字の結合・ペルシア語の表記を壊さないため）。連続する ZWJ/ZWNJ は 1 つにまとめる。64 文字まで（コードポイント単位）。`content/` の検査は、整えた値と一致しなければ拒否）。ページの URL は `wrld_` を除いた UUID（`/worlds/00000000-0000-4000-8000-000000000001`）。VRChat のリンクは `https://vrchat.com/home/world/{wrld_id}/info`（記事のキャプションのワールド名も、このリンク）。`image`（省略可）は R2 に置いたワールドの画像（`{ "key": "worlds/{uuid}/file_…_{版}", "w": 1024, "h": 768 }`。key はそのワールドの uuid の下だけ。`album worlds --apply` が入れる）。ワールドの一覧のカードと、ワールドのページの先頭に出す。無いワールドは、一覧では撮った写真を代わりに出し、ワールドのページでは画像を出さない。

`content/world-stats.json`（省略可）は VRChat から取ったワールドの情報（`{ "fetchedAt": "…+09:00", "worlds": { "wrld_…": { "authorName", "description", "capacity", "recommendedCapacity", "visits", "favorites", "tags", "publicationDate", "updatedAt" } } }`）。GitHub Actions の `update-data.yml` が 1 日 1 回 `album world-stats --apply` で更新する（手で書かない）。キーは `worlds.json` にあること。ワールドのページの名前の下に「○年○月○日時点」と添えて出す。VRChat のユーザー ID（`usr_`）は持たない（作者は表示名だけ。ワールドの作者へのクレジット）。

## `content/tags.json`（タグのマスタ）

```json
{ "night": { "name": "夜景", "description": "（省略可）" } }
```

## Build が出す `data/`（アプリが読む。`/data/*` は noindex）

| ファイル | 中身 |
| --- | --- |
| `data/site.json` | `siteName`・`author`（サイトの作者名）・`imageBaseUrl`・`postsPerPage`（12） |
| `data/posts-index.json` | 公開済み記事の要約（slug・title・publishedAt・summary・cover{key,w,h,color}・tags[slug]・photoCount）。新しい順 |
| `data/archive.json` | `{ "2026": { "10": 12, "09": 8 } }` 公開月（JST）別の件数 |
| `data/posts/{slug}.json` | 本文 HTML・写真（`world.name` はマスタの名前。撮影者・写っている人は持たない） |
| `data/tags.json` | `[{ "slug", "name", "description", "postCount" }]`（記事が 1 件以上のタグだけ） |
| `data/worlds.json` | `[{ "id", "name", "photoCount", "cover" }]`（写真が 1 枚以上のワールドだけ） |
| `data/worlds/{uuid}.json` | `{ "id", "name", "photos": [{ "key", "w", "h", "color", "slug", "takenAt" }] }` |
| `data/outfits.json` | `[{ "id", "name", "shop", "photoCount", "cover" }]`（その服を着た写真が 1 枚以上の服だけ） |
| `data/outfits/{id}.json` | `{ "id", "name", "shop", "url", "photos": [{ "key", "w", "h", "color", "slug", "takenAt" }] }`（`url` は商品ページ。名前のリンク先） |

## 静的 HTML・その他

`/`・`/page/n`・`/posts/{slug}`・`/archive/{yyyy}/{MM}`（`/page/n`）・`/worlds`・`/worlds/{uuid}`（`/page/n`）・`/outfits`・`/outfits/{id}`（`/page/n`）・`/tags`・`/tags/{tag}`（`/page/n`）・`/privacy`・`/report`（noindex）・`404.html`・`feed.xml`（RSS 2.0・最新 20 件・noindex の記事は除く）・`sitemap.xml`（本番のみ）・`robots.txt`・`_headers`・`_redirects`。静的 HTML には人名も人物 ID も出ない。

- 一覧は 1 ページ 12 件（`PostsPerPage`）。ワールドのページは 12 枚。
- `feed.xml` の `<enclosure>` の `length` は 0（画像の実サイズは Build から分からない。M3 で実サイズが分かったら入れる）。
- 各ページの `og:image` は画像キーから導く（`{ImageBaseUrl}{key}_og.jpg`。記事はそのカバー、一覧系は載せる記事のうち最初のカバー）。`_og.jpg` は表紙の写真だけに作る（`album cover <key> --apply`）。

## `content/outfits.json`（服のマスタ）と `content/avatars.json`（アバターのマスタ）

服の名前（商品名）を写真のキャプションに「服: ○○」と出す。**服の名前は、サイト内の服のページ（`/outfits/{id}`）へのリンク**で、そのページの名前（見出し）が商品ページ（BOOTH など。`url` があるとき）へのリンク（新しいタブ）。同じように、**キャプションのワールド名は、サイト内のワールドのページ（`/worlds/{uuid}`）へのリンク**で、そのページの名前（見出し）が VRChat のワールドページへのリンク。服の一覧は `/outfits`（左のメニューの「服」）。アバター名は画面に出さない（紐づけに使うだけ）。
VRChat の写真のメタデータにはアバターも服も無い。写真ごとのアバターは、fara-vrc-photo-server が撮影時に写真へ追記した記録（faratag。v1.0.0 の旧形式 faraxml も読む）から、デスクトップ版が新しい記事の写真に自動で入れる。手で直すときは `album photo-avatar`。

- `outfits.json`: `{ "<服の id>": { "name": "表示する名前", "shop": "店名（任意）", "url": "https://… （任意。https だけ）", "note": "備考（任意）", "thumbnail": "https://…", "description": "…", "tags": ["…"], "publishedAt": "…", "updatedAt": "…", "fetchedAt": "…" } }`
  - `thumbnail`・`description`・`tags`・`publishedAt`・`updatedAt`・`fetchedAt` は、デスクトップ版の「フォルダから服を登録」ウィンドウの「インポート」（未登録の服の登録時、登録済みの服の差分・再インポート）が、BOOTH Library Manager の保存データから書く（手で書かなくてよい）。サイトの服の一覧・ページに出る（`thumbnail` は https の画像 URL。説明は 500 文字まで取り込む。**成人向けの商品は、取り込まない**）。BOOTH の画像は、BOOTH のサーバー（`https://booth.pximg.net/`。サイトの CSP と公開前の検査で許可している）から読む。
- `avatars.json`: `{ "<アバターの id>": { "name": "VRChat 上のアバター名", "aliases": ["表記ゆれ"], "outfitIds": ["服の id", …（順序つき）], "note": "（任意）" } }`
- id は小文字・数字・ハイフン（先頭は英数字・40 文字まで）。服は複数のアバターで共有できる。
- 記事の写真には任意で `"avatar": "<アバターの id>"` と `"outfits": ["服の id", …]` を書く。服の解決は、`outfits` があればそれ（空の配列は「服は無い」）、無ければアバターの `outfitIds`。
- 検査（ContentLoader）: 未登録の id は拒否、URL は https:// だけ、名前は表示名の規則、同じ服の重複は拒否。服を外すとき・アバターを外すときは、使われていれば拒否する。

## 文章（写真の前・おわり）

- 記事の `body` は、写真の前に出る「はじめの文章」（Markdown）。
- 写真の `text`（省略可・Markdown・10000 文字まで）は、その写真の前に入れる文章。あると、記事の写真の並びはその写真の前で区切られ、「文章 → 写真」の順に出る（写真の番号 `#photo-N` は記事全体の通し）。
- 記事の `afterword`（省略可・Markdown・10000 文字まで）は、写真のあとに出る「おわりの文章」。
- 公開データには Markdown を入れず、HTML（`textHtml`・`afterwordHtml`）だけを入れる。元データに `textHtml`・`wear` を書くとエラー。
- デスクトップ版の「記事の編集」で編集できる。
