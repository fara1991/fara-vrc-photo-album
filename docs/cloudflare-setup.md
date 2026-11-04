# Cloudflare・GitHub の初期設定（Fara Photo Album）

Fara Photo Album は Cloudflare Workers（静的アセットのみ + 小さな Worker）で公開し、写真は R2 の独自ドメインから配信します。ドメイン `fara-labs.com` は Cloudflare で管理済みで、他のサイト（pokemon・avatar・bot・portfolio）と同じアカウント・同じ API トークンを使えます。以下は **手作業の一覧（優先順）** です。Claude はトークン・ドメイン・Access の設定を行えません。

| 公開先 | 置き場所 | 公開のタイミング |
| --- | --- | --- |
| https://vrc-album.fara-labs.com/（本番） | Worker `fara-vrc-album` | 手元のアプリ／`tools/site.mjs deploy production`（**初回は、メンテナンス画面を経由せず、すぐ一般公開される**。下の「本番の最初の公開」） |
| https://vrc-album-dev.fara-labs.com/（確認用） | Worker `fara-vrc-album-dev` | 手元のアプリ／`tools/site.mjs deploy develop`（Cloudflare Access で守る） |
| https://vrc-photos.fara-labs.com/（写真） | R2 バケット `fara-vrc-photos` のカスタムドメイン | 画像のアップロード（M3 の `album` ツール）時 |

**公開は GitHub Actions では行わない**（2026-10-28 から。本物の記事 `content/` を Git に入れていないため）。ワークフローは `build.yml`（PR と、develop・master への push。サンプル記事でのビルドと検査と `wrangler deploy --dry-run` だけ）の 1 本。以下の表の A2・A3・A5・B3・D など、Actions の Environments・シークレットを使う手順は、**以前の構成の記録**（新しいリポジトリには Environments もシークレットも作っていない）。公開用のトークンは、デスクトップアプリの設定（`.env.local`）に入れる。

## 優先順

### A. develop の確認用サイトを出すまで（M0 の完了）

| # | 作業 | 場所 | 備考 |
| --- | --- | --- | --- |
| A1 | DNS に `vrc-album`・`vrc-album-dev`・`vrc-photos` の既存レコードが **無い** ことを確認 | Cloudflare → `fara-labs.com` → DNS → Records | あるとカスタムドメインの作成が失敗する |
| A2 | **Environments を作る**（`production` = Deployment branches を master のみ、`develop` = develop のみ）。その **Environment の secrets** に `CLOUDFLARE_API_TOKEN`（production・develop のそれぞれ）、**リポジトリの Variables** に `CLOUDFLARE_ACCOUNT_ID` を登録 | Settings → Environments / Secrets and variables → Actions | deploy.yml は `environment: production`、deploy-develop.yml は `environment: develop` を使うので、Environment の secrets を読める。build.yml（PR でも動く）は environment を使わず、シークレットを読まない。**既存サイトと同じトークンの値を使える**。権限: Workers Scripts Edit・Workers Routes Edit・Zone DNS Edit（カスタムドメインの自動作成）・Account Settings Read（`require-maintenance` が Worker の設定を読むため。**表記が実態と合うかは未確認**。Workers Scripts の Edit は Read を含む）。範囲は Zone = `fara-labs.com` なので新ホストの追加は不要 |
| A3 | （A2 で作った）Environments の保護の確認: `production`（Deployment branches = master のみ）・`develop`（= develop のみ） | Settings → Environments | avatar・portfolio（private）には同じ Environments があり、ブランチ制限（branch_policy）が使えることを API で確認済み。一方 **ブランチ保護（branch protection）は private + 無料プランでは設定できない**（403）。取り違えは deploy.yml・deploy-develop.yml の中の照合（ブランチ）・verify-site・require-maintenance が防ぐ |
| A4 | Cloudflare Access に `vrc-album-dev.fara-labs.com` を追加（既存の dev 用 Application の public hostname に追加し、同じ Allow ポリシー） | Zero Trust → Access → Applications | **`vrc-photos.fara-labs.com` は Access の対象にしない**（確認用サイトから画像が読めなくなる） |
| A5 | 手元のアプリ（または `node tools/site.mjs deploy develop`。develop のブランチで、作業ツリーが clean）で確認用へ公開 → https://vrc-album-dev.fara-labs.com/ を Access 越しに開く。**develop にマージしただけでは反映されない**（再公開が要る） | 手元のアプリ | 初回はカスタムドメインの作成に数分。権限エラーなら Workers → 該当 Worker → Settings → Domains & Routes から手動で 1 回追加して再実行（A2・A3 の Environments は、以前の構成の記録） |

### B. 本番を出す前に（M4）

| # | 作業 | 場所 | 備考 |
| --- | --- | --- | --- |
| B1 | Turnstile のウィジェット（既存の `fara sites report`）にホスト名 `vrc-album.fara-labs.com`・`vrc-album-dev.fara-labs.com` を追加 | Cloudflare → Turnstile | `/report` 用 |
| B2 | 初回公開後、Worker `fara-vrc-album`・`fara-vrc-album-dev` に変数・シークレットを登録: `TURNSTILE_SITE_KEY`・`TURNSTILE_SECRET`・`DISCORD_WEBHOOK_URL`（**シークレット**）、`MAINTENANCE_MODE`・`MAINTENANCE_MESSAGE`（**テキスト**） | Workers & Pages → 各 Worker → 設定 → 変数とシークレット | 手順は portfolio の `docs/report-and-maintenance.md` の 1-3・2-2 と同じ。`keep_vars` により公開では消えない |
| B3 | 本番の最初の公開。**master にマージしただけでは公開されない**。手元のアプリ／`tools/site.mjs deploy production` から行う。**そのまま初回の公開をすると、メンテナンス画面を経由せず、すぐ一般公開される**。メンテナンス画面のまま出すなら、下の「本番の最初の公開」の手順 | 手元 | 本番の公開前ゲート（`MAINTENANCE_MODE` が `notice` か `true`）は、Worker がまだ無い初回だけ `ALLOW_MISSING_WORKER=1` で通る（このとき変数は入らない） |
| B4 | レート制限ルール（`/api/report` の POST）は既存のゾーン規則で足りる（確認のみ） | WAF | |
| B5 | Web Analytics: 新ホストを許可（ビーコンはゾーンの設定で自動挿入）。CSP は `tools/site.json` で許可済み | Cloudflare → Analytics | 他サイトと同じ。privacy に記載する |

### C. 写真の配信（M3 の前まで）

| # | 作業 | 場所 | 備考 |
| --- | --- | --- | --- |
| C1 | R2 を有効化（支払い方法の登録を求められることがある。無料枠内なら請求なし） | Cloudflare → R2 | |
| C2 | バケット `fara-vrc-photos` を作成。試験用に `fara-vrc-photos-test`（公開ドメインなし） | R2 → Create bucket | |
| C3 | `fara-vrc-photos` に **カスタムドメイン `vrc-photos.fara-labs.com`** を追加。`r2.dev` の公開 URL は **無効のまま** | バケット → Settings → Custom Domains | 証明書は自動 |
| C4 | **R2 API トークン**: 権限 = Object Read & Write、対象 = 上の 2 バケットだけ。Access Key ID / Secret は手元の環境変数（`R2_ACCOUNT_ID`・`R2_ACCESS_KEY_ID`・`R2_SECRET_ACCESS_KEY`・`R2_BUCKET`）に保存。**GitHub には置かない** | R2 → Manage API tokens | 紛失したら作り直し |
| C5 | （任意）Tiered Cache・Cache Rule、キャッシュ削除用トークン（Zone → Cache Purge） | ゾーン → Caching / My Profile → API Tokens | 削除依頼の対応を速くしたいとき |
| C6 | R2 の使用量確認の習慣づけ（月 1 回） | R2 → Metrics | |

### E. 記事の中身の控え用のバケット（任意。デスクトップ版の「R2 に控えを作る」を使うとき）

記事・マスタ・台帳の JSON（content/）の控え（スナップショット）を置くバケットです。**写真のバケットには置きません**（写真のバケットは独自ドメイン `vrc-photos.…` で公開されていて、控えが誰にでも読めてしまうため。台帳には元のファイル名が入っています）。

1. Cloudflare のダッシュボード → R2 → 「バケットを作成」で、新しいバケットを作る（例: `fara-content-backup`。名前は小文字の英数字とハイフンで 3〜63 文字）
2. **公開しない**: 「パブリックアクセス」（r2.dev のサブドメイン）は無効のまま、「カスタムドメイン」も付けない
3. R2 の API トークン（C4 で作ったもの）が、このバケットも読み書きできるようにする。トークンを写真のバケットだけに絞ってある場合は、トークンの設定でこのバケットを足す（またはバケットを指定しない「Object Read & Write」にする）
4. デスクトップ版の設定の「記事の中身の控え用のバケット名」に、そのバケット名を入れて保存する（写真のバケットと同じ名前は拒否される）

控えは `snapshots/{yyyyMMdd-HHmmss}/…` に、毎回新しく作る（古い控えは消さない・上書きしない）。各ファイルの SHA-256 を目録（`manifest.json`）に控え、戻すときに照合する。

### F. アプリからの公開に使うトークン（任意。デスクトップ版の「公開」タブを使うとき）

1. Cloudflare のダッシュボード → 右上のプロフィール → 「API トークン」→「トークンを作成」→「Cloudflare Workers を編集する」のテンプレート（以前の GitHub Actions の公開に使っていたものと同じ権限）
2. 作ったトークンの値を、デスクトップ版の設定の「Cloudflare の API トークン（サイトの公開用）」に入れて保存する（アカウント ID は、R2 の設定の「Cloudflare R2 のアカウント ID」を共通で使う）（`.env.local` に入る。Git には入らない）
3. 本番の公開では、Worker の変数 `MAINTENANCE_MODE` を読む権限（Workers Scripts の読み取り）も使う。テンプレートに含まれる

### 注意（kit の一括メンテ切替）

**公開してよくなるまで、kit の一括メンテ切替（`set-maintenance-all.yml` / `tools/maintenance.mjs set`）を master（本番）向けに実行しない。** 本番の album の `MAINTENANCE_MODE` は、「本番の最初の公開」の手順で `true` にしておく運用のあいだ、本番を含む一括切替（`notice` / `false`）が、それを意図せず公開してしまう（手順を踏まずに初回を公開した場合は、最初から公開状態になる）。このため kit の `maintenance-sites.json` には album の **develop（確認用）だけ**を載せてあり、production は公開準備ができてから足す（手順は kit の `docs/maintenance-cli.md`）。本番の album の `MAINTENANCE_MODE` は、Cloudflare のダッシュボード（Workers & Pages → `fara-vrc-album` → 設定 → 変数とシークレット）で切り替える（`keep_vars` により、手元の公開では消えない）。

### 本番の最初の公開（要点）

現状のコード（`tools/site.mjs`・`SitePublishService`）の動きは次のとおり（コードの読み取りで確かめたもの。本番では実行していない）。

- 公開の前提: **master に develop をマージ済み**で、手元の master が最新（HEAD が `origin/master` と一致・作業ツリーが clean。`tools/publish-guard.mjs`）。確認用（dev）に今の記事の中身を公開済みで、content は「同期」タブで R2 と揃えてある（content は Git の外なので、ガードは見ない）。
- Worker がまだ無いと、公開の前の確認（`require-maintenance.mjs`）が 404 で止まる。`ALLOW_MISSING_WORKER=1` で通すと、`wrangler deploy` は変数なしで実行される。**`MAINTENANCE_MODE` が入らないので、メンテナンス画面を経由せず、すぐ一般公開される。**
- Worker が既にあれば、確認は `MAINTENANCE_MODE` が `notice` か `true` のときだけ通る。

**メンテナンス画面のまま出したいとき**は、先に Worker を `MAINTENANCE_MODE=true` つきで作る。そうすれば、そのあとのアプリの公開は `ALLOW_MISSING_WORKER` なしで通る。

```bash
# master を最新にして、作業ツリーが clean の状態で。CLOUDFLARE_API_TOKEN・CLOUDFLARE_ACCOUNT_ID を環境変数に入れておく
node tools/site.mjs build production                 # site-production を作る（verify-site も走る）
cp -r site-production dist                           # PowerShell なら Copy-Item -Recurse
node node_modules/wrangler/bin/wrangler.js deploy --env= --var MAINTENANCE_MODE:true
rm -rf dist
```

- これは `publish-guard` と `require-maintenance` を通らない手元の直接の実行。**master の最新・content が R2 と同じであること・Worker 名（`fara-vrc-album`）を、自分で確かめてから行う**。
- **要確認**: `--var` で入れた変数が、その後のアプリの公開（`keep_vars: true`）で消えずに残るか。公開のあと、ダッシュボードで `MAINTENANCE_MODE` が **テキスト**の `true` になっていること、本番の URL が 503（メンテナンス画面）になることを確かめる（確かめるまでは、アプリで本番を公開しない）。残らなければ、ダッシュボードで `MAINTENANCE_MODE`（テキスト）を足す。
- **要確認**: 初回の公開でカスタムドメイン（`vrc-album.fara-labs.com`）が自動で作られるか（確認用では A5 のとおり、数分かかる・権限エラーのときは手動）。
- 公開してよくなったら、ダッシュボードで `MAINTENANCE_MODE` を切り替える（`notice` で予告 → `false`）。`false` にしたあとは、次のアプリの公開が止まる（`notice` か `true` に戻してから公開する決まり）。
- `B2` の変数（`TURNSTILE_*`・`DISCORD_WEBHOOK_URL`）も、公開を解く前に入れる。

### D. kit 側（Claude が PR を出す。マージ・タグ・設定はユーザー）

- kit の `tools/maintenance-sites.json` への本サイトの追加 PR（develop のみ）は、**本サイトの `deploy-develop.yml` が develop に入ってからマージする**（kit が起動するのは確認用の `deploy-develop.yml`）（kit の PR 本文にも書いてある）。
- `SITES_DISPATCH_TOKEN`（PAT）の対象リポジトリに `fara-vrc-photo-album` を追加（kit の `set-maintenance-all.yml` が使う）。

### G. 報告の 1 日の送信回数の制限（kit v1.0.0・opt-in）

- 報告（`/api/report`）は、送信元（`CF-Connecting-IP`）ごとに **1 日 3 回まで**。4 回目は 429 で、報告ページに上限の文を出す。
- 仕組み: `worker/index.js` が `ReportRateLimiter`（kit の `worker/report-rate-limit.js`・Durable Object）を export し、`wrangler.jsonc` の `durable_objects`（バインディング `REPORT_LIMITER`）と `migrations`（`new_sqlite_classes`）が指す。**本番（トップレベル）と `env.develop` の両方に書く**（環境は継承されない）。
- 保存は、送信元の識別子（秘密鍵つきのハッシュ）と回数だけ。JST の 0 時に消す。プライバシーポリシー（`PrivacyView`・`docs/privacy-and-takedown.md`）に書いてある。
- **秘密鍵（`TURNSTILE_SECRET`）を変えると、計数はリセットされる**（識別子がすべて変わる）。
- 確認: `wrangler deploy --dry-run`（本番と develop）。確認用（vrc-album-dev）で、同じ IP から 4 回送って、4 回目が 429 になるかを見る。
- 外すとき: `worker/index.js` の export と `REPORT_LIMITER` のバインディングを消すと、制限は働かなくなる。migrations は消さない（削除の migration は Durable Object のデータを消すため、Cloudflare の docs を確かめてから行う）。

## `run_worker_first` の配列指定（M0 の検証結果）

既存の 4 サイトは `run_worker_first = true`（全リクエストが Worker を通る。Workers 無料枠は 1 日 10 万）。本サイトは **配列指定** にした。

```jsonc
"run_worker_first": ["/*", "!/_framework/*", "!/_content/*", "!/css/*", "!/js/*", "!/images/*", "!/favicon.svg"]
```

- 計画書の例（`["/api/*", "/*.html", "/", "!…"]`）は **使えない**: `/posts/foo` のような拡張子なしの URL に `/*.html` は一致せず、メンテナンス中でも HTML が 503 にならない。`/*` に除外を足す形にした。
- `/data/*` は除外しない（メンテナンス中は、記事の JSON も止める）。
- 検証（wrangler 4.152.0、`wrangler dev --var MAINTENANCE_MODE:true`、2026-10-24）: `/`・`/posts/…`・``/privacy`・`/data/*.json`・`/nonexist`・`/robots.txt` = **503**（Worker を通る）、`/api/maintenance` = 200、`/_framework/*`・`/_content/*`・`/css/*`・`/js/*`・`/images/*`・`/favicon.svg` = **200**（Worker を通さない）。通常時は、HTML・静的部品の両方に `Content-Security-Policy` が付く（`_headers` は Worker 経由でも直接でも効く）。`wrangler deploy --dry-run` も本番・develop で通る。
- **未確認**: 実際の Cloudflare 上での課金カウント（静的部品が Workers の呼び出しに数えられないこと）。公開後に Workers の使用量（ダッシュボード）で確認する。ローカルの wrangler dev で除外が効くことまでは確認済み。効いていなければ `true` に戻せばよい（設定 1 か所）。
- 設定の形は `tests/js/wrangler-config.test.mjs` が固定している。

## ローカルで Cloudflare と同じ動きを確認する

```bash
npm ci
npm run check     # dotnet publish → 本番・確認用の出力 → verify-site → wrangler deploy --dry-run（両環境。Cloudflare にはつながない）
npm run preview   # 本番用の出力で wrangler dev（http://localhost:8787）。確認用は preview:develop
```
