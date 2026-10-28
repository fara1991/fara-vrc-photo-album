# 動作確認（Playwright）

CI では動かさない（ブラウザが要る）。`playwright-core` は、リポジトリの外の作業用フォルダに入れて使う（リポジトリには入れない）。

```bash
# 作業用フォルダで 1 回
npm i playwright-core@1.64.1
# 配信（Cloudflare と同じ動き。Worker と _headers の CSP が効く）
npm run preview                       # http://localhost:8787（本番用の出力。確認用は npm run preview:develop）
```

- `report-form.mjs` … `/report` の確認: 連絡先（Twitter のユーザー名）の必須表示・検査（空 / @なし / 16 文字 / 全角＠ / 前後の空白）・送信の内容。Turnstile と /api/report は route で代役。
- `lib.mjs` … 上の確認の共通部分（ブラウザの起動・ビューポート・CSP 違反の収集）。
- `image-diff.py` … 画像の比較（Pillow が要る）。

## 削除したもの（サンプル記事に依存していたため）

`m1-smoke.mjs`・`m1-cors.mjs`・`m2-check.mjs` は、サンプル記事（`sample-*`）・合成画像（`tools/dev-photos`）を前提にしていた。サンプルを削除し、確認用も本番の R2 の写真を見るようになったので、そのままでは動かない。
