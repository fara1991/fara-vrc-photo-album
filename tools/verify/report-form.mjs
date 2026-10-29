// /report の確認（Playwright）。  NEW=http://127.0.0.1:8787 [OUT=verify-out] node tools/verify/report-form.mjs
// 連絡先は任意（kit の既定）。Turnstile と送信先（/api/report）は route で代役にする。
// 確かめること: 任意の表示（見出し・aria-required なし）、連絡先が空・何を書いても送れること（前後の空白は除く）、送信の内容、CSP 違反 0。PC・スマホ × ダーク・ライト
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
    r.check(`[${tag}] 見出し『連絡先（任意）』`, (await p.locator("label[for=report-contact]").innerText()).trim() === "連絡先（任意）");
    r.check(`[${tag}] 入力欄が aria-required="true" ではない`, (await p.getAttribute("#report-contact", "aria-required")) !== "true");
    r.check(`[${tag}] 説明に『書かなくても送れます』`, (await p.locator(".fbc-form-group", { has: p.locator("#report-contact") }).innerText()).includes("書かなくても送れます"));
    await p.waitForFunction(() => !document.querySelector(".mud-alert-text-warning, .mud-alert-outlined-warning"), null, { timeout: 10000 }).catch(() => {});
    if (tag === "pc-dark" || tag === "sp-light") await p.screenshot({ path: join(OUT, `report-${tag}.png`), fullPage: true });

    const cases = [
      ["空", "", ""],
      ["空白だけ", "   ", ""],
      ["X のユーザー名", "  @game_fara  ", "@game_fara"],
      ["メールアドレス", "me@example.com", "me@example.com"],
    ];
    for (const [label, value, expected] of cases) {
      const before = p.posts.length;
      await fill(p, value);
      await send(p);
      await p.waitForSelector("text=送信しました", { timeout: 10000 });
      const body = p.posts.at(-1);
      r.check(`[${tag}] ${label}: 送信できる。contact は前後の空白なしで送られる`, p.posts.length === before + 1 && (body.contact ?? "") === expected && body.category === "bug" && body.turnstileToken === "stub-token", JSON.stringify(body));
      await p.click("text=もう 1 件送る");
      await p.waitForSelector("#report-contact");
    }
    r.check(`[${tag}] CSP 違反 0`, (await cspViolations(p)).length === 0, (await cspViolations(p)).join(" | "));
    await p.context().close();
  }
}

await browser.close();
r.finish();
