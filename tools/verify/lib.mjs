// tools/verify の共通部分（Playwright。CI では動かさない）。playwright-core は、リポジトリの外の作業用フォルダに入れて使う（tools/verify/README.md）。
import { createRequire } from "node:module";
import { join } from "node:path";

// PLAYWRIGHT_CORE_DIR=<playwright-core を入れたフォルダ（node_modules の親）>。省略時は、この場所から普通に探す
const require = createRequire(process.env.PLAYWRIGHT_CORE_DIR ? join(process.env.PLAYWRIGHT_CORE_DIR, "x.js") : import.meta.url);
const { chromium } = require("playwright-core");

/** ブラウザを起動する。CHROME_PATH（Chromium の実行ファイル）で指定できる。稀に起動が落ちるので数回試す。 */
export async function launch(extra = {}) {
  for (let i = 1; ; i++) {
    try {
      return await chromium.launch({ ...(process.env.CHROME_PATH ? { executablePath: process.env.CHROME_PATH } : {}), ...extra });
    } catch (e) {
      if (i >= 6) throw e;
      await new Promise((r) => setTimeout(r, 2000));
    }
  }
}

// 1x1 の WebP（R2 の画像の代わり。M0 の写真はダミーで実在しない。外には出さない）
const TINY_WEBP = Buffer.from("UklGRh4AAABXRUJQVlA4TBEAAAAvAAAAAAdQlHqUp/+BiOh/AAA=", "base64");

export const VIEWPORTS = {
  pc: { width: 1280, height: 800 },
  sp: { width: 390, height: 844, mobile: true },
};

/**
 * ページを作り、コンソールのエラー/警告・CSP 違反・ページのエラー・失敗したリクエストを集める。
 * 画像（R2 の https://vrc-photos.fara-labs.com）は M0 では存在しないので、外には出さず、1x1 の WebP を返す（コンソールにエラーを出さないため）。
 */
export async function newPage(browser, { width = 1280, height = 800, mobile = false, theme = "dark", javaScriptEnabled = true, seed = true, mockR2 = true, ignoreHTTPSErrors = false } = {}) {
  const ctx = await browser.newContext({ viewport: { width, height }, deviceScaleFactor: 1, colorScheme: theme, hasTouch: mobile, isMobile: mobile, javaScriptEnabled, ignoreHTTPSErrors });
  if (javaScriptEnabled) {
    await ctx.addInitScript(({ theme, seed }) => {
      try {
        // テーマの保存（初回のみ）。seed=false なら何も入れず、既定（ダーク）の動きを見る
        if (seed && !sessionStorage.getItem("__seeded")) {
          sessionStorage.setItem("__seeded", "1");
          localStorage.setItem("fara-vrc-album.settings", JSON.stringify({ Theme: theme }));
        }
      } catch { /* 保存が使えない */ }
      window.__csp = [];
      document.addEventListener("securitypolicyviolation", (e) => window.__csp.push(`${e.violatedDirective} ${e.blockedURI}`));
    }, { theme, seed });
  }
  // R2。画像は外に出さず 1x1 の WebP を返す。必要な確認は、あとから ctx.route で上書きする（後から足した方が優先）
  if (mockR2) {
    await ctx.route("https://vrc-photos.fara-labs.com/**", (route) => route.fulfill({ status: 200, contentType: "image/webp", body: TINY_WEBP }));
  }
  const page = await ctx.newPage();
  page.problems = [];
  page.on("console", (m) => { if (m.type() === "error" || m.type() === "warning") page.problems.push(`${m.type()}: ${m.text()}`); });
  page.on("pageerror", (e) => page.problems.push("pageerror: " + e.message));
  // net::ERR_ABORTED は、.NET の HttpClient（WebAssembly）が、成功以外の応答（404 など）を読まずに破棄したときの fetch の中断の記録。応答自体は届いている（コンソールの "Failed to load resource" が別に出る）ので数えない
  page.on("requestfailed", (r) => { if (!r.url().startsWith("https://vrc-photos.fara-labs.com/") && r.failure()?.errorText !== "net::ERR_ABORTED") page.problems.push("requestfailed: " + r.url() + " " + (r.failure()?.errorText ?? "")); });
  return page;
}

export const cspViolations = (page) => page.evaluate(() => window.__csp || []);

/** 起動の完了を待つ（kit の boot-indicator.js が #boot-indicator を外し、SiteLayout が描かれる）。 */
export async function waitBooted(page, timeout = 60000) {
  await page.waitForSelector(".fbc-page", { timeout });
  await page.waitForFunction(() => !document.getElementById("boot-indicator"), null, { timeout });
}

/** 結果の集計。check(名前, 真偽, 詳細)。最後に finish() で終了コードを決める。 */
export function reporter() {
  const rows = [];
  return {
    check(name, ok, detail = "") {
      rows.push({ name, ok: !!ok, detail });
      console.log(`${ok ? "OK  " : "NG  "} ${name}${detail ? "  " + detail : ""}`);
    },
    finish() {
      const ng = rows.filter((r) => !r.ok);
      console.log(`\n${rows.length - ng.length} / ${rows.length} OK`);
      if (ng.length > 0) { for (const r of ng) console.log(`NG: ${r.name} ${r.detail}`); process.exitCode = 1; }
    },
  };
}
