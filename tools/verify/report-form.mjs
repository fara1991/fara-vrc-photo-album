// /report の確認（Playwright）。  NEW=http://127.0.0.1:8787 [OUT=verify-out] node tools/verify/report-form.mjs
// 連絡先は Twitter のユーザー名（@○○）が必須（kit v0.4.0 の ContactRequired / ContactFormat）。Turnstile と送信先（/api/report）は route で代役にする。
// 確かめること: 必須の表示（見出し・例・aria-required）、検査（空 / @なし / 16 文字 / 全角＠ / 前後の空白 / 15 文字ちょうど）、送信の内容、CSP 違反 0。PC・スマホ × ダーク・ライト
import { mkdirSync } from "node:fs";
import { join } from "node:path";
import { VIEWPORTS, cspViolations, launch, newPage, reporter, waitBooted } from "./lib.mjs";

const NEW = (process.env.NEW || "http://127.0.0.1:8787").replace(/\/$/, "");
const OUT = process.env.OUT || "verify-out";
mkdirSync(OUT, { recursive: true });
const r = reporter();
const browser = await launch();

const TURNSTILE_STUB = "window.turnstile = { render: (el, o) => { setTimeout(() => o.callback('stub-token'), 50); return 1; }, reset() {}, remove() {} };";

async function open(vp, theme) {
  const p = await newPage(browser, { ...vp, theme });
  const ctx = p.context();
  p.posts = [];
  await ctx.route("**/api/report/config", (route) => route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ enabled: true, siteKey: "stub" }) }));
  await ctx.route("https://challenges.cloudflare.com/turnstile/v0/api.js*", (route) => route.fulfill({ status: 200, contentType: "application/javascript", body: TURNSTILE_STUB }));
  await ctx.route("**/api/report", (route) => {
    if (route.request().method() !== "POST") return route.continue();
    p.posts.push(JSON.parse(route.request().postData()));
    return route.fulfill({ status: 200, contentType: "application/json", body: JSON.stringify({ ok: true }) });
  });
  await p.goto(NEW + "/report", { waitUntil: "load" });
  await waitBooted(p);
  await p.waitForSelector("#report-contact");
  return p;
}

async function fill(p, contact) {
  if ((await p.inputValue("#report-category")) !== "bug") {
    await p.click(".mud-select .mud-input-control, .mud-select");
    await p.click(".mud-popover-open .mud-list-item:has-text('不具合')");
  }
  await p.fill("#report-message", "テストの報告です");
  await p.fill("#report-contact", contact);
}

async function send(p) {
  await p.click("button.mud-button-filled");
}

for (const [vpName, vp] of Object.entries(VIEWPORTS)) {
  for (const theme of ["dark", "light"]) {
    const tag = `${vpName}-${theme}`;
    const p = await open(vp, theme);
    r.check(`[${tag}] 見出し『Twitter のユーザー名（@○○）（必須）』`, (await p.locator("label[for=report-contact]").innerText()).trim() === "Twitter のユーザー名（@○○）（必須）");
    r.check(`[${tag}] 入力欄に aria-required と例『@example』`, (await p.getAttribute("#report-contact", "aria-required")) === "true" && (await p.getAttribute("#report-contact", "placeholder")) === "@example");
    r.check(`[${tag}] 説明に Discord は使わない旨`, (await p.locator(".fsk-form-group", { has: p.locator("#report-contact") }).innerText()).includes("Discord では受け付けていません"));
    await p.waitForFunction(() => !document.querySelector(".mud-alert-text-warning, .mud-alert-outlined-warning"), null, { timeout: 10000 }).catch(() => {});
    if (tag === "pc-dark" || tag === "sp-light") await p.screenshot({ path: join(OUT, `report-${tag}.png`), fullPage: true });

    const cases = [
      ["空", "", false, "を入力してください"],
      ["空白だけ", "   ", false, "を入力してください"],
      ["@ なし", "game_fara", false, "1〜15 文字"],
      ["16 文字(@ + 16)", "@1234567890123456", false, "1〜15 文字"],
      ["全角の＠", "＠game_fara", false, "1〜15 文字"],
      ["使えない文字", "@game-fara", false, "1〜15 文字"],
      ["メールアドレス", "me@example.com", false, "1〜15 文字"],
      ["15 文字ちょうど", "@123456789012345", true, ""],
      ["前後の空白", "  @game_fara  ", true, ""],
    ];
    for (const [label, value, ok, message] of cases) {
      const before = p.posts.length;
      await fill(p, value);
      await send(p);
      if (ok) {
        await p.waitForSelector("text=送信しました", { timeout: 10000 });
        const body = p.posts.at(-1);
        r.check(`[${tag}] ${label}: 送信できる。contact は前後の空白なしで送られる`, p.posts.length === before + 1 && body.contact === value.trim() && body.category === "bug" && body.turnstileToken === "stub-token", JSON.stringify(body));
        await p.click("text=もう 1 件送る");
        await p.waitForSelector("#report-contact");
      } else {
        await p.waitForSelector("[role=alert]", { timeout: 10000 });
        const text = await p.locator("[role=alert]").innerText();
        r.check(`[${tag}] ${label}: 送信されず、エラーが出る`, p.posts.length === before && text.includes(message) && text.includes("Twitter のユーザー名"), text.replace(/\s+/g, " "));
      }
    }
    r.check(`[${tag}] CSP 違反 0`, (await cspViolations(p)).length === 0, (await cspViolations(p)).join(" | "));
    await p.context().close();
  }
}

await browser.close();
r.finish();
