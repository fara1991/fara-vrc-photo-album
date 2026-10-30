# 公開前の手動確認（チェックリスト）

自動の関門は `npm test`（verify-site のテスト・Worker・JS）と `npm run check`（publish → 本番・確認用の出力 → verify-site → wrangler dry-run）。
以下は自動では確かめられないもの。確認用（https://vrc-album-dev.fara-labs.com/）と本番（https://vrc-album.fara-labs.com/）で、公開の前に手で行う。
確認用も本番も同じ R2 の写真を見る。ローカルで見るときは `npm run preview:develop`（http://localhost:8787）。

## 1. 人名が出ていないこと

撮影者・写っている人の名前と人物 ID は出さない（`npm run check` の verify-site が静的 HTML・data・feed・sitemap を検査する）。ワールド名は出す。ブラウザの確認は下の 3 を見る。

## 2. ホットリンク（写真の参照元の制限）

写真は `<img>` で読むので CORS は要らない。R2 側に参照元（Referer）の制限を付けていないかを確かめる:

```bash
curl -sI https://vrc-photos.fara-labs.com/2025-12/20251221-204348-957_8a84fce1_1280.webp
curl -sI -H "Referer: https://vrc-album-dev.fara-labs.com/" https://vrc-photos.fara-labs.com/2025-12/20251221-204348-957_8a84fce1_1280.webp
# 期待: どちらも 200、content-type: image/webp、cache-control: public, max-age=31536000, immutable
curl -sI -H "Referer: https://example.invalid/" https://vrc-photos.fara-labs.com/2025-12/20251221-204348-957_8a84fce1_1280.webp
# 参照元の制限を付けているなら 403 になる。付けていないなら 200（どちらも確認用・本番の表示には影響しない場合がある）
```

- 制限を付けたときは、確認用と本番の両方の Origin（Referer）を許可しているか。許可していないと確認用の写真が出ない。
- `_og.jpg`（SNS 用）は表紙の写真だけ。`album cover <key> --apply` の後に、同じ要領で 200 を確かめる。

## 3. ブラウザ（PC 1280px / スマホ 390px × ダーク・ライト）

| 項目 | 確かめること |
| --- | --- |
| ヘッダー | 高さは 56px（PC・スマホとも）。名前は文字『Fara Photo Album』（サイト名と同じ）。画像の『Fara』はサイドバーのプロフィールカード（バナー 4:1・丸いアバター）に出る（静的 HTML には無く、起動の後に出る） |
| 一覧・記事・アーカイブ・ワールド・タグ | 全ページ種が開く。本文が入っている。写真の並び（枚数に応じたギャラリー） |
| ライトボックス | 写真をクリックで開く。← → で移動、Esc で閉じる。URL が `#photo-N` になる（記事を離れる Back では閉じず、記事に戻る） |
| ナビ | ワールド・タグ・アーカイブが再読み込みなしで動く |
| 横スクロール | スマホ幅で横にはみ出さない |
| 予約公開 | 本番には公開日が未来の記事が出ない（一覧・記事のページ・アーカイブとも） |

## 4. アクセシビリティ（axe）

Chrome の拡張「axe DevTools」（または Lighthouse のアクセシビリティ）で、次のページを PC/スマホ × ダーク/ライトで確かめる。違反は 0 にする:

- `/`、`/posts/{slug}`、`/archive/yyyy/MM`、`/worlds`、`/worlds/{uuid}`、`/outfits`、`/outfits/{id}`、`/tags`、`/tags/{tag}`、`/privacy`、`/report`

## 5. 記事の内容（公開前）

- `content/posts/{slug}.json` の `draft`・`publishedAt`・`cover`・`tags` を確かめる（draft: false・未来でない・表紙がその記事の写真）。
- `album verify`（手元。`.env.local` が要る）で、公開 URL と記事 JSON の寸法・Content-Type・Cache-Control を照合する。

## 6. 公開の後

- `feed.xml` と `sitemap.xml`（本番のみ）が開く。
- 本番の `/` に確認用（dev）の記事が混ざらない（`/` の一覧の件数を確認用と比べる）。
