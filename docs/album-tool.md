# アップロードツール `album`（M3）

写真を変換して R2（`vrc-photos.fara-labs.com`）にアップロードし、記事の下書き・削除を行う手元のツール。
実行は手元だけ（CI では動かさない）。元写真（既定は `C:\Users\{ユーザー}\Pictures\VRChat`。VRChat が写真を保存する場所）は読み取りだけで、書き換え・移動・削除はしない。

## 起動

```powershell
npm run album -- help            # 使い方
npm run album -- scan            # 元写真の月別件数（読み取りのみ）
```

`npm run album -- <コマンド> …` は `dotnet run --project src/FaraVrcAlbum.Tools` と同じ。`dotnet build` 済みなら `src/FaraVrcAlbum.Tools/bin/Debug/net10.0/album.exe` を直接呼んでもよい。

## 設定（手元だけ・Git 管理外）

| 場所 | 中身 | 値の例 |
| --- | --- | --- |
| `.env.local`（リポジトリ直下。Git 管理外） | R2 の認証情報 | 下の 4 項目 |
| `local/album.local.json`（任意） | アップロード用フォルダ・R2 のバケット名（`bucket`）・記事の中身のフォルダ（`contentDir`。既定はリポジトリの `content`）・記事の中身の控え用のバケット名（`backupBucket`） | `{ "uploadRoot": "C:\\Users\\{ユーザー}\\Pictures\\VRChatAlbum", "sourceRoot": "C:\\Users\\{ユーザー}\\Pictures\\VRChat" }`（省略時はこの既定。ユーザーの「ピクチャ」フォルダの下） |

`.env.example`（Git 管理）をコピーして `.env.local` を作り、値を入れる（`Copy-Item .env.example .env.local`）。項目名（値は書かない・表示しない）:

| 項目名 | 中身 | 作り方（cloudflare-setup.md C4） |
| --- | --- | --- |
| `R2_ACCOUNT_ID` | Cloudflare のアカウント ID | ダッシュボードの右側 |
| `R2_ACCESS_KEY_ID` | R2 API トークンの Access Key ID | R2 → Manage API tokens（Object Read & Write・対象は本番と試験用のバケットだけ） |
| `R2_SECRET_ACCESS_KEY` | 同 Secret | 同上（作成時にしか表示されない） |
| `R2_BUCKET` | バケット名（省略時 `fara-vrc-photos`） | 試験時は `fara-vrc-photos-test` |

足りない項目があると、その名前だけを表示して止まる。

## フォルダ規約（計画書 §5.5.1）

```
C:\Users\{ユーザー}\Pictures\VRChatAlbum\
├─ inbox\2026-10\   ← 掲載してよい PNG を、撮影月のフォルダにコピーする（これが掲載確認の記録）
├─ uploaded\2026-10\ ← アップロードが済んだ元ファイル（自動で移る。--keep で移さない）
├─ _work\           ← 作業用（消してよい）
```

- 元写真（既定は `C:\Users\{ユーザー}\Pictures\VRChat`。VRChat が写真を保存する場所）から **コピー** して入れる。元は触らない。
- フォルダの月と撮影月がずれたら警告する（アップロード先は撮影月。フォルダは直さない）。
- 5 秒以内に更新されたファイルは書き込み中とみなして飛ばす。

## コマンド

| コマンド | 動作 | 書き込み |
| --- | --- | --- |
| `album prepare [--from 2025-11] [--to 2026-10]` | `inbox\yyyy-mm` を作る（空のフォルダだけ） | フォルダのみ |
| `album scan [フォルダ]` | 元写真を走査し、月ごとの件数・XMP の有無・台帳の状態を表示。ファイル名やワールド名は表示しない | ハッシュのキャッシュ（`local\cache`）のみ |
| `album upload` | 計画のみ（既定）。R2 に**読み取り**で存在確認はする | なし |
| `album upload --apply` | 変換 → R2 に不足分だけ PUT → 台帳に登録 → `uploaded\` へ移動。**ワールド名**は、取り込みでは変えない（`draft` で変える）。撮影者・写っている人は扱わない。 | R2・台帳・フォルダ |
| `album draft --post <slug> --title 題 --since 2026-10-01` | 台帳の uploaded から `content/posts/<slug>.json`（draft: true）の photos[] を作る。撮影日時の昇順。`--keys k1,k2` で選ぶこともできる。**既定はプレビュー**（追加される写真とワールドの変更だけ表示）。`--apply` で書く。**ワールド名**: 同じ `wrld_` の写真の XMP のワールド名が、マスタの `name` と違い、その写真の撮影日時がマスタの `nameAt`（名前を観測した日時。既存データは未設定＝どの写真でも更新可）以降のときだけ、`name` を新しい名前に変え、旧名を `aliases` に残し、`nameAt` を更新する。古い写真の名前では `name` を変えず `aliases` に足すだけ。ワールドの `id`（URL）は変わらない | `--apply` のときだけ 記事 JSON・`worlds.json`（Git の公開データ。R2 は使わない） |
| `album draft --post <slug> --keys … --append [--apply]` | 既存の記事の末尾に、まだ入っていない写真を足す（既定はプレビュー） | 同上 |
| `album cover <key> [--apply]` | 表紙にする写真の OGP（`_og.jpg`・1200×630）を作って R2 に置く。元の PNG は `uploaded\` か `inbox\` から台帳のハッシュで探す。**og はカバー写真だけ**（登録時には作らない） | 既定はドライラン。`--apply` で R2 と台帳 |
| `album worlds [--apply] [--refresh]` | ワールドの画像を VRChat の公開 API（`https://api.vrchat.cloud/api/1/worlds/{wrld_id}` の `imageUrl`）から取り、WebP（512・1024 幅）にして R2 の `worlds/{uuid}/file_…_{版}_512.webp`・`_1024.webp` に置き、`content/worlds.json` の `image`（key・w・h）に書く。既定は画像の無いワールドだけ・プレビュー（VRChat の情報は読むが、画像は読まない・R2 と content/ に書かない）。`--refresh` で全部を見直す（作者が画像を変えると版が変わり、新しいキーで置く）。**アプリでは、「公開」タブの「画像・情報を取得する」ボタンで、ワールドの画像と情報を、足りない分だけまとめて取る**（同じボタンで、服の画像・情報も BOOTH Library Manager から取り込む）（公開とは別の操作。公開（dev・本番）は取得をしない。VRChat への問い合わせはワールドごとに 1 回で、画像と情報を同じ応答から取る。R2 の設定は、VRChat に問い合わせる前に確かめる。取れなかったワールドは `local/world-fetch-state.json` に記録し、24 時間は問い合わせない（「前に取れなかったワールドも、もう一度取得する」で取れる）。VRChat や R2 に繋がらなくても公開は続け、続きは次の公開で取る）。CLI では、新しいワールドを `draft --apply` で足したら実行する（無くてもビルドは通り、一覧では撮った写真を代わりに出す）。本番のバケットだけ | `--apply` のとき R2・`worlds.json` |
| `album world-stats [--apply]` | 全ワールドの情報（作者名・説明・定員・訪問数・お気に入り・タグ・公開日・更新日）を `content/world-stats.json` に書く。今いる人数は取らない。**GitHub Actions の `update-data.yml` が 1 日 1 回実行して develop に commit する**（手元で実行してもよい）。アプリの「画像・情報を取得する」は、**情報のまだ無いワールドの分だけ**取る（取得日時は、前に全体を取った日時のまま）（新しいワールドのページに、すぐ訪問数・説明が出る） | `--apply` のとき `world-stats.json` |

VRChat の API の使い方（`worlds`・`world-stats` 共通）: User-Agent は `FaraVrcAlbum/1.0 (Fara; game_fara@outlook.com)`。VRChat へのリクエスト（情報・画像とも）は約 60 秒おき（＋0〜5 秒のゆらぎ。最初の 1 回は待たない）。429 が返ったらそこで止める（取れた分は書く。再実行すると、`worlds` は画像の無いワールドだけを読む＝続きから）。ビルド・公開では VRChat を読まず、閲覧者のブラウザも VRChat に接続しない。

| `album verify [--ledger]` | 公開 URL（`vrc-photos`）の画像が、記事の JSON と合っているか（200・Content-Type・Cache-Control・WebP の寸法） | なし（公開 URL を読む） |

| `album audit [--bucket]` | R2 の写真のオブジェクトと記事・台帳を突き合わせ、孤児（参照なし・表紙でなくなった写真の `_og.jpg` など）と欠け（記事にあるのに `_2048.webp` が無い、表紙なのに `_og.jpg` が無い）を表示。| なし（読み取りのみ） |
| `album remove <key> [--apply]` | 写真の削除。順序: (1) 記事の photos[] から外して content/ を検査 → (2) R2 の削除（画像 4 件） → (3) 台帳を removed（ハッシュは残す）。表紙（cover、省略時は先頭の写真）・本文に残っていれば止める。(1) か (2) で失敗したら記事を元に戻し、台帳は uploaded のまま（やり直せる）。(2) の途中で止まったら、同じコマンド（`album remove <key> --apply`）を再実行すると続きから完了する（削除は冪等）。作業ツリーが origin/develop と一致しないときは警告を出す | 既定はドライラン |

注: 対象は記事の写真（非公開の記事も含む。確認用に出るため）。`--ledger` で記事に無い台帳（uploaded）の写真も加える。対象が 0 件なら理由を出す。画面版は docs/album-desktop.md。

共通: `--repo <dir>`（リポジトリの場所。既定はカレントから `FaraVrcAlbum.sln` を探す）、`--upload-root <dir>`、`--bucket <name>`。

### 削除・下書きの扱い

- 削除（`remove`）の途中で R2 の削除が止まった（写真の一部だけ消えた）ときは、表示に「R2 から N/M 件を削除済み」と出る。記事と台帳は元のまま（写真は uploaded）。**同じコマンドを再実行すると続きから完了する**（R2 の削除は冪等）。その間は `album verify` / `album audit` で欠けが分かる（表示が欠けることがある）。

- `remove` は **本番バケット（`fara-vrc-photos`）だけ**に対して行う。試験用バケットに使えるのは `upload` と `audit`。
- 記事を書いたあとは、必ず `ContentLoader`（ビルドと同じ検査）を通す。失敗したら書いた内容を元に戻す。
- `draft` は `draft: true`（非公開）で作る（確認用にも本番にも出ない）。公開するときは、アプリの「記事」画面の「公開する」で `draft` を外し、確認用 → 本番の順に公開する。
- ワールド名は `content/worlds.json` のマスタを正にする。XMP の名前と違えば `aliases` に足すだけで、`name` は変えない。
- 表紙（`cover`、省略時は先頭の写真）の `_og.jpg` は `album cover <key> --apply` で作る。`verify` と `audit` は表紙の写真だけを見る。
- `--bucket` は `^[a-z0-9][a-z0-9-]{2,62}$` の名前だけを受ける（`..` や `/` を含むものは拒否）。
- エラーの表示は、R2 の値と認証情報の形（アクセスキー ID など）を消してから出す。値は表示しない。

## 初回の取り込み（計画書 §5.5.7）

1. `album scan` で月ごとの件数を見る（読み取りのみ）。
2. 掲載してよい写真を、少しずつ `inbox\yyyy-mm` にコピーする（5 枚 → 50 枚 → 月単位）。
3. `album upload` で計画を確認 → `album upload --apply`。
4. `album verify` は、記事に入れた後に実行する（`uploaded` だけでは見ない）。
5. `album draft --post <slug> …` で下書きを作る → 本文・タグを手で書く → PR（develop 向け）→ 確認用で確認。

2025 年 11 月より前の写真は取り込まない（ワールド情報が無いため。`album upload` が拒否する）。

## 安全装置

- 既定はドライラン・プレビュー（`upload`・`remove`・`draft`・`cover`）。`--apply` が無いと書かない。
- 台帳の `removed` のハッシュは再登録を拒否する（`--force` でのみ解除）。
- 途中で失敗したら台帳には登録しない。次の実行では HEAD で既存を確かめ、不足分だけ PUT する。
- 同時実行は `local\album.lock` で防ぐ（残っていれば、確認してから消す）。
- 台帳（`content/catalog/photos.json`）は `content/` の一部で、Git 管理外（元ファイル名の控えを含むため）。Build は読まない。
- ヘッダーは `Cache-Control: public, max-age=31536000, immutable`（写真）。

## 試験の手順（実バケットに書く前）

1. `.env.local` の `R2_BUCKET` を `fara-vrc-photos-test`（公開ドメインなし）にして、`album upload --apply` を試す。台帳は `local\catalog-fara-vrc-photos-test.json` に分かれ、`uploaded\` へは移らない。
2. 試験用のアップロードは、テストの写真（合成画像）だけで行う。実写真は試験に使わない。
3. 本番バケットへ切り替えてから、少数（5 枚）で `--apply` → `album verify`。

## 未確認の範囲

- R2 への実通信（S3 互換 API・`DisablePayloadSigning` などの設定）は、実バケットでは試していない（CI とテストは `MemoryObjectStore`）。最初の実行は試験用バケットで行う。
- VRCX からのワールド補完（任意機能）は未実装。
- `album reencode`（変換設定の世代の作り直し）は未実装。

## アバターと服（`album avatar` `album outfit` `album photo-avatar`）

マスタ（`content/avatars.json`・`content/outfits.json`）と、記事の写真への付与は Git のファイルだけを書く（R2 には触らない）。既定はプレビュー（検査を通して元に戻す）。`--apply` で書く。

| コマンド | 動作 |
| --- | --- |
| `album outfit add <id> --name 名前 [--shop 店名] [--url https://…] [--note 備考]` | 服を登録（id が既にあれば拒否） |
| `album outfit edit <id> [--name …] [--shop …] [--url …] [--note …]` | 項目を直す（空の値で項目を消す） |
| `album outfit list` ／ `album outfit remove <id>` | 一覧 ／ 外す（使われていれば拒否） |
| `album avatar add <id> --name アバター名 [--aliases a,b] [--note …]` | アバターを登録 |
| `album avatar link <id> --outfits id,id` | そのアバターに着せている服（順序どおり）を置き換える |
| `album avatar list` ／ `album avatar edit <id> …` ／ `album avatar remove <id>` | 一覧（手元の確認用にアバター名を表示）／直す／外す（写真に使われていれば拒否） |
| `album photo-avatar <写真のキー> <アバター> [--outfits id,id]` | 記事の写真にアバターを付ける（`--outfits` で、その写真だけ服を上書き） |
| `album draft … --avatar <id> [--outfits id,id]` | 新しく入れる写真にアバター（と服）を付ける |

最初の登録の流れ: `album outfit add` で服を登録 → `album avatar add` でアバターを登録（VRChat に表示されるアバター名を `--name` に貼る）→ `album avatar link` で服を紐づけ → `album photo-avatar` で写真に付ける（または `album draft --avatar`）→ 検査を通ったら `--apply`。差分を確かめて PR（develop 向け）。
