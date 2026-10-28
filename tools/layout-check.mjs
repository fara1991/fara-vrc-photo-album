// レイアウトの自動計測（npm run layout-check）。
//
//   node tools/layout-check.mjs              サンプルの記事で静的サイト（site-production）を作り、配って計測する
//   node tools/layout-check.mjs --no-build   作成済みの site-production をそのまま計測する
//
// 手順: (1) 静的サイトを作る（tools/site.mjs build production。記事はサンプル samples/content。CI の検査と同じ印）
//       (2) kit の serve-static（startStaticServer）で site-production を空いている 8850〜 のポートで配る
//       (3) kit の runLayoutCheck で計測する（設定は layout-check.config.mjs。baseURL だけ、配った先に差し替える）
//       (4) 配信を閉じる（この関数が開いたサーバーだけ。他のプロセスには触らない）
// 終了コード: 0 = 違反なし / 1 = 違反あり / 2 = 計測できなかった（ビルド・設定・ブラウザ・ページ）。
import { spawnSync } from "node:child_process";
import { existsSync } from "node:fs";
import { createServer } from "node:http";
import { dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { createStaticServer } from "../external/fara-blazor-component/tools/layout-check/serve-static.mjs";
import { formatViolation, runLayoutCheck } from "../external/fara-blazor-component/tools/layout-check/index.mjs";

const root = join(dirname(fileURLToPath(import.meta.url)), "..");
const SITE_DIR = join(root, "site-production");
const FIRST_PORT = 8850;
const PORT_TRIES = 10;

/** パスに「<パス>.html」があれば真（拡張子の付いたパス・/ は対象外）。書式が壊れたパスは false（kit が 400 で返す）。 */
function htmlFor(pathname) {
  if (pathname === "/" || /\.[a-z0-9]+$/i.test(pathname)) return false;
  let decoded;
  try {
    decoded = decodeURIComponent(pathname);
  } catch {
    return false;
  }
  return existsSync(join(SITE_DIR, `${decoded}.html`));
}

/**
 * kit の serve-static（createStaticServer）で SITE_DIR を配る。
 * 違い: /worlds や /tags のように「同じ名前のフォルダ」があるパスは、kit は フォルダの index.html を探して 404 になる。
 * Cloudflare（と docs の約束）は .html を先に見るので（/worlds → worlds.html）、.html があるときは、そちらを kit に渡す。
 * 配るのは kit の処理（パスの検査・Content-Type）のまま。空いているポートを FIRST_PORT から探す。
 * @returns {Promise<{ origin: string, close: () => Promise<void> }>}
 */
async function serveSite() {
  const kit = createStaticServer(SITE_DIR);
  const server = createServer((req, res) => {
    const url = new URL(req.url ?? "/", "http://localhost");
    if (htmlFor(url.pathname)) req.url = `${url.pathname}.html${url.search}`;
    kit.emit("request", req, res);
  });
  for (let port = FIRST_PORT; port < FIRST_PORT + PORT_TRIES; port += 1) {
    const listened = await new Promise((resolveListen) => {
      const onError = () => resolveListen(false);
      server.once("error", onError);
      server.listen(port, "127.0.0.1", () => {
        server.off("error", onError);
        resolveListen(true);
      });
    });
    if (listened) {
      return {
        origin: `http://127.0.0.1:${port}`,
        close: () => new Promise((resolveClose) => server.close(() => resolveClose())),
      };
    }
  }
  throw new Error(`${FIRST_PORT} から ${PORT_TRIES} 個のポートがすべて使われている`);
}

/** 静的サイトを作る（サンプルの記事）。失敗したら投げる（終了コード 2）。 */
function buildSite() {
  const result = spawnSync(process.execPath, [join(root, "tools", "site.mjs"), "build", "production"], {
    cwd: root,
    stdio: "inherit",
    env: { ...process.env, ALBUM_CONTENT_DIR: "samples/content", ALBUM_ALLOW_SAMPLE: "1" },
  });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(`静的サイトの作成に失敗しました（終了コード ${result.status}）`);
}

async function loadConfig() {
  const mod = await import(pathToFileURL(join(root, "layout-check.config.mjs")).href);
  return mod.default;
}

async function main(argv) {
  const noBuild = argv.includes("--no-build");
  if (argv.some((arg) => arg !== "--no-build")) {
    console.error("使い方: node tools/layout-check.mjs [--no-build]");
    return 2;
  }
  if (!noBuild) buildSite();
  if (!existsSync(SITE_DIR)) {
    console.error(`静的サイトが無い: ${SITE_DIR}（--no-build を外して作る）`);
    return 2;
  }

  const config = await loadConfig();
  const server = await serveSite();
  try {
    const { violations, runs, config: normalized } = await runLayoutCheck({ ...config, baseURL: server.origin });
    const enabled = Object.entries(normalized.rules).filter(([, on]) => on).map(([name]) => name);
    console.log(`レイアウト計測: ${normalized.pages.length} ページ × ${normalized.viewports.length} 幅（${normalized.viewports.join(", ")}px）= ${runs} 回（${server.origin}）`);
    console.log(`ルール: ${enabled.join(", ")}`);
    if (violations.length === 0) {
      console.log("違反なし");
      return 0;
    }
    for (const v of violations) console.log(formatViolation(v));
    console.log(`違反 ${violations.length} 件`);
    return 1;
  } finally {
    await server.close();
  }
}

try {
  process.exitCode = await main(process.argv.slice(2));
} catch (error) {
  console.error(`計測できなかった: ${error?.message ?? error}`);
  process.exitCode = 2;
}
