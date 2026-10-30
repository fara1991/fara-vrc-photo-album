# 動作確認（Playwright）

CI では動かさない（この確認は手元で行う）。`playwright-core` は devDependency（1.64.1 に固定。kit の layout-check と同じ版。`npm ci` で入る）。レイアウトの自動計測（`npm run layout-check`・CI の layout-check job）は [docs/static-checks.md](../../docs/static-checks.md)。

```bash
# 配信（Cloudflare と同じ動き。Worker と _headers の CSP が効く）
npm run preview                       # http://localhost:8787（本番用の出力。確認用は npm run preview:develop）
```

- `report-form.mjs` … `/report` の確認: 連絡先が任意であること（空でも送れる・前後の空白は除く）・送信の内容。Turnstile と /api/report は route で代役。
- `lib.mjs` … 上の確認の共通部分（ブラウザの起動・ビューポート・CSP 違反の収集）。
- `image-diff.py` … 画像の比較（Pillow が要る）。

## 削除したもの（サンプル記事に依存していたため）

`m1-smoke.mjs`・`m1-cors.mjs`・`m2-check.mjs` は、サンプル記事（`sample-*`）・合成画像（`tools/dev-photos`）を前提にしていた。サンプルを削除し、確認用も本番の R2 の写真を見るようになったので、そのままでは動かない。
