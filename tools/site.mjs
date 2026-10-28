// 公開物の作成と確認（手元用。CI の build.yml は同じ手順を個別のステップで行うが、公開はしない）。Windows でも WSL でも同じ手順になるよう Node で書いている。
//
//   node tools/site.mjs clean                      publish / site-* / dist / 各プロジェクトの bin・obj を消す
//   node tools/site.mjs build [production|develop] dotnet publish → 環境ごとの出力（site-production / site-develop）→ Build → verify-site（省略は両方）
//   node tools/site.mjs check                      build（両方）→ 環境ごとに dist へ置いて wrangler deploy --dry-run（公開はしない）
//   node tools/site.mjs deploy <production|develop>  build → dist に置いて wrangler deploy（実際に公開する。CLOUDFLARE_API_TOKEN・CLOUDFLARE_ACCOUNT_ID が要る。アプリの「公開」ボタンが使う）
//   node tools/site.mjs preview [production|develop]  build → dist に置いて wrangler dev（Cloudflare と同じ動きで http://localhost:8787。省略は production）
//
// 環境変数 ALBUM_CONTENT_DIR があれば、記事の中身をそのフォルダから読む（Build の --content。省略はリポジトリの content）
// 出力: publish/（dotnet publish）、site-production/・site-develop/（Build が書き足した、環境ごとの公開物）、dist/（wrangler の assets。公開先の環境のものだけを置く）
import { spawnSync } from "node:child_process";
import { cpSync, existsSync, readdirSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { PUBLISH_ATTEMPTS, isIllinkCrash } from "./publish-retry.mjs";
import { checkPublishBranch, checkWorkTree } from "./publish-guard.mjs";
import { fileURLToPath } from "node:url";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const ENVS = ["production", "develop"];
// wrangler の --env。本番はトップレベルの設定（--env ""）、確認用は env.develop
const WRANGLER_ENV = { production: "", develop: "develop" };

/** 失敗（手順の失敗・使い方の誤り）。投げて、最後に 1 か所で終了コードを決める（途中の finally の掃除を飛ばさないため）。 */
class StepError extends Error {
  constructor(message, exitCode = 1) {
    super(message);
    this.exitCode = exitCode;
  }
}

function run(command, args, options = {}) {
  console.log(`> ${[command, ...args].join(" ")}`);
  const result = spawnSync(command, args, { cwd: root, stdio: "inherit", ...options });
  if (result.error) throw result.error;
  if (result.status !== 0) {
    throw new StepError(`失敗しました（終了コード ${result.status}）: ${command} ${args.join(" ")}`, result.status ?? 1);
  }
}

// dotnet publish。ILLink が環境によってたまに落ちる（Internal CLR error など。再実行で通る）ので、ILLink が原因の失敗だけ、最大 PUBLISH_ATTEMPTS 回までやり直す。
// 原因は MSBuild のファイルログ（-flp）の文で決める（画面の出力はそのまま流す）。ふつうのコンパイルエラーは、やり直さずにすぐ止める。
function publishWeb() {
  const args = ["publish", "src/FaraVrcAlbum.Web", "-c", "Release", "--nologo", "-o", "publish"];
  const log = join(tmpdir(), `album-publish-${process.pid}.log`);
  for (let attempt = 1; ; attempt++) {
    console.log(`> dotnet ${args.join(" ")}`);
    const result = spawnSync("dotnet", [...args, `-flp:logfile=${log};verbosity=minimal`], { cwd: root, stdio: "inherit" });
    if (result.error) throw result.error;
    const text = existsSync(log) ? readFileSync(log, "utf8") : "";
    rmSync(log, { force: true });
    if (result.status === 0) return;
    if (attempt < PUBLISH_ATTEMPTS && isIllinkCrash(text)) {
      console.log(`ILLink（サイズを小さくする処理）が落ちました。環境の一時的な問題なので、やり直します（${attempt + 1}/${PUBLISH_ATTEMPTS}）`);
      continue;
    }
    throw new StepError(`失敗しました（終了コード ${result.status}）: dotnet ${args.join(" ")}`, result.status ?? 1);
  }
}

function wranglerBin() {
  return join(root, "node_modules", "wrangler", "bin", "wrangler.js");
}

// wrangler は package.json で版を固定している。node_modules が無ければ（初回・clone 直後）、npm ci で入れる
function ensureWrangler() {
  if (existsSync(wranglerBin())) return;
  console.log("wrangler が入っていません。npm ci で入れます（初回だけ）");
  run("npm", ["ci"], { shell: true });
  if (!existsSync(wranglerBin())) throw new StepError("npm ci のあとも wrangler が見つかりません（node_modules/wrangler）。");
}

function clean() {
  for (const p of ["publish", "site-production", "site-develop", "dist"]) rmSync(join(root, p), { recursive: true, force: true });
  for (const base of ["src", "tests"]) {
    if (!existsSync(join(root, base))) continue;
    for (const project of readdirSync(join(root, base))) {
      for (const sub of ["bin", "obj", "bin.unix", "obj.unix"]) rmSync(join(root, base, project, sub), { recursive: true, force: true });
    }
  }
}

function build(envs) {
  // dotnet publish の ILLink が WSL でたまに落ちる（再実行で通る）ので、clean は bin/obj まで消す npm run clean に任せ、ここでは出力だけ作り直す
  for (const p of ["publish", ...ENVS.map((e) => `site-${e}`)]) rmSync(join(root, p), { recursive: true, force: true });
  publishWeb();
  for (const env of envs) {
    // テンプレート（publish/wwwroot/index.html）は、出力先（site-<env>）とは別のフォルダに置く（kit の build-pages が上書きを拒否する）
    cpSync(join(root, "publish", "wwwroot"), join(root, `site-${env}`), { recursive: true });
    run("dotnet", ["run", "--project", "src/FaraVrcAlbum.Build", "-c", "Release", "--no-launch-profile", "--",
      "--template", "publish/wwwroot/index.html", "--out", `site-${env}`, "--env", env,
      ...(process.env.ALBUM_CONTENT_DIR ? ["--content", process.env.ALBUM_CONTENT_DIR] : [])]);
    run(process.execPath, [".github/scripts/verify-site.mjs", `site-${env}`, env]);
  }
}

function putDist(env) {
  rmSync(join(root, "dist"), { recursive: true, force: true });
  cpSync(join(root, `site-${env}`), join(root, "dist"), { recursive: true });
}

function parseEnv(value, fallback) {
  if (value === undefined) return fallback;
  if (!ENVS.includes(value)) throw new StepError(`環境は production か develop です: ${value}`, 2);
  return [value];
}

/** いまのブランチ名（detached HEAD や Git が使えないときは空）。 */
function currentBranch() {
  const result = spawnSync("git", ["rev-parse", "--abbrev-ref", "HEAD"], { cwd: root, encoding: "utf8" });
  return result.status === 0 ? result.stdout.trim() : "";
}

/** git の出力（失敗・使えないときは空）。 */
function gitOut(args) {
  const result = spawnSync("git", args, { cwd: root, encoding: "utf8" });
  return result.status === 0 ? result.stdout.trim() : "";
}

/** 公開の前に見る作業ツリーとコミットの状態（publish-guard の checkWorkTree に渡す）。ignore 済み（content/・local/・publish/・dist/ など）は porcelain に出ない。 */
function gitState() {
  const porcelain = spawnSync("git", ["status", "--porcelain"], { cwd: root, encoding: "utf8" });
  return {
    porcelain: porcelain.status === 0 ? porcelain.stdout : "failed",                  // 読めないときは「変更あり」として止める
    head: gitOut(["rev-parse", "HEAD"]),
    remoteHead: gitOut(["rev-parse", "--verify", "--quiet", "origin/master"]),
  };
}

function main(argv) {
  const [command, arg] = argv;
  switch (command) {
    case "clean":
      clean();
      break;
    case "build":
      build(parseEnv(arg, ENVS));
      break;
    case "check":
      ensureWrangler();
      build(ENVS);
      for (const env of ENVS) {
        putDist(env);
        run(process.execPath, [wranglerBin(), "deploy", "--dry-run", `--env=${WRANGLER_ENV[env]}`]);
      }
      rmSync(join(root, "dist"), { recursive: true, force: true });
      break;
    case "deploy": {
      const [env] = parseEnv(arg, []);
      if (!env) throw new StepError("公開先を指定してください: node tools/site.mjs deploy <production|develop>", 2);
      // 本番は master・確認用は develop からだけ。作業ツリーがクリーンであること（本番は HEAD が origin/master と同じこと）も、ビルドの前に確かめる
      checkPublishBranch(env, currentBranch());
      checkWorkTree(env, gitState());
      // 公開では、サンプルの記事（ALBUM_ALLOW_SAMPLE）の関門を外さない（CI のビルドだけ使う印）
      delete process.env.ALBUM_ALLOW_SAMPLE;
      ensureWrangler();
      build([env]);
      putDist(env);
      try {
        run(process.execPath, [wranglerBin(), "deploy", `--env=${WRANGLER_ENV[env]}`]);
      } finally {
        rmSync(join(root, "dist"), { recursive: true, force: true });
      }
      break;
    }
    case "preview": {
      const [env] = parseEnv(arg, ["production"]);
      ensureWrangler();
      build([env]);
      putDist(env);
      run(process.execPath, [wranglerBin(), "dev", `--env=${WRANGLER_ENV[env]}`]);
      break;
    }
    default:
      throw new StepError("使い方: node tools/site.mjs clean | build [production|develop] | check | deploy <production|develop> | preview [production|develop]", 2);
  }
}

try {
  main(process.argv.slice(2));
} catch (e) {
  if (!(e instanceof StepError)) throw e;
  console.error(e.message);
  process.exitCode = e.exitCode;
}
