// wrangler.jsonc の形のテスト。  node --test tests/js/wrangler-config.test.mjs
// run_worker_first の配列（HTML・API・データは Worker を通し、起動に要る静的な部品だけ通さない）を固定する。
// 実際の動き（wrangler dev で、MAINTENANCE_MODE=true のとき HTML が 503・_framework が 200）は docs/cloudflare-setup.md の検証結果と tools/verify/m0-smoke.mjs。
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

/** JSONC を読む（文字列の中の // は消さない）。 */
function parseJsonc(text) {
  let out = "";
  let inString = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (inString) {
      out += c;
      if (c === "\\") out += text[++i];
      else if (c === '"') inString = false;
    } else if (c === '"') { inString = true; out += c; }
    else if (c === "/" && text[i + 1] === "/") { while (i < text.length && text[i] !== "\n") i++; out += "\n"; }
    else out += c;
  }
  return JSON.parse(out.replace(/,(\s*[}\]])/g, "$1"));
}

const config = parseJsonc(readFileSync(new URL("../../wrangler.jsonc", import.meta.url), "utf8"));
const develop = config.env.develop;

/** Cloudflare の run_worker_first の規則（* は任意の文字列。! で始まるものは除外で、除外が優先）で、Worker を先に通すか。 */
function throughWorker(patterns, path) {
  const toRegExp = (p) => new RegExp("^" + p.replace(/[.+?^${}()|[\]\\]/g, "\\$&").replace(/\*/g, ".*") + "$");
  const include = patterns.filter((p) => !p.startsWith("!")).some((p) => toRegExp(p).test(path));
  const exclude = patterns.filter((p) => p.startsWith("!")).some((p) => toRegExp(p.slice(1)).test(path));
  return include && !exclude;
}

test("本番・確認用とも run_worker_first は同じ配列（true にしない）", () => {
  assert.ok(Array.isArray(config.assets.run_worker_first));
  assert.deepEqual(develop.assets.run_worker_first, config.assets.run_worker_first);
});

test("HTML のページ（拡張子なしの URL を含む）・API・データ・404 は Worker を通る（メンテナンスの 503 が効く）", () => {
  for (const path of ["/", "/posts/first-post", "/posts/first-post/", "/page/2", "/privacy", "/report", "/api/maintenance", "/api/report", "/data/site.json", "/data/posts/first-post.json", "/index.html", "/robots.txt", "/sitemap.xml", "/no-such-page"]) {
    assert.ok(throughWorker(config.assets.run_worker_first, path), `${path} は Worker を通すはず`);
  }
});

test("起動に要る静的な部品だけ Worker を通さない（Workers の呼び出しを数えない）", () => {
  for (const path of ["/_framework/blazor.webassembly.js", "/_framework/dotnet.native.abc123.wasm", "/_content/MudBlazor/MudBlazor.min.css", "/_content/FaraBlazorComponent.Razor/js/boot-indicator.js", "/css/app.css", "/js/startup.js", "/images/header-43454dfe.webp", "/favicon.svg"]) {
    assert.ok(!throughWorker(config.assets.run_worker_first, path), `${path} は Worker を通さないはず`);
  }
});

test("data/ を除外しない（本番の初回はメンテナンス画面のまま作るので、記事の JSON もメンテナンス中は止める）", () => {
  assert.ok(!config.assets.run_worker_first.some((p) => p.startsWith("!/data")));
});

test("Worker 名・ドメイン・keep_vars・workers_dev", () => {
  assert.equal(config.name, "fara-vrc-album");
  assert.equal(develop.name, "fara-vrc-album-dev");
  assert.deepEqual(config.routes, [{ pattern: "vrc-album.fara-labs.com", custom_domain: true }]);
  assert.deepEqual(develop.routes, [{ pattern: "vrc-album-dev.fara-labs.com", custom_domain: true }]);
  assert.equal(config.keep_vars, true);
  assert.equal(config.workers_dev, false);
  assert.equal(develop.workers_dev, false);
  assert.equal(config.preview_urls, false);
  assert.equal(develop.preview_urls, false);
  assert.equal(config.main, "worker/index.js");
});

test("assets: dist・ASSETS・auto-trailing-slash・404-page（本番・確認用で同じ）", () => {
  for (const a of [config.assets, develop.assets]) {
    assert.equal(a.directory, "./dist");
    assert.equal(a.binding, "ASSETS");
    assert.equal(a.html_handling, "auto-trailing-slash");
    assert.equal(a.not_found_handling, "404-page");
  }
});

test("報告の回数制限の Durable Object（REPORT_LIMITER → ReportRateLimiter）は、本番と確認用の両方にある（環境は継承されない）", () => {
  for (const env of [config, develop]) {
    assert.deepEqual(env.durable_objects, { bindings: [{ name: "REPORT_LIMITER", class_name: "ReportRateLimiter" }] });
    assert.deepEqual(env.migrations, [{ tag: "v1", new_sqlite_classes: ["ReportRateLimiter"] }]);
  }
});

test("Worker は ReportRateLimiter を export している（バインディングの class_name と一致）", () => {
  const source = readFileSync(new URL("../../worker/index.js", import.meta.url), "utf8");
  assert.match(source, /export \{ ReportRateLimiter \} from "\.\.\/external\/fara-blazor-component\/worker\/report-rate-limit\.js";/);
});

test("package.json の wrangler は固定（^ や ~ を付けない）", () => {
  const pkg = JSON.parse(readFileSync(new URL("../../package.json", import.meta.url), "utf8"));
  assert.match(pkg.devDependencies.wrangler, /^\d+\.\d+\.\d+$/);
});
