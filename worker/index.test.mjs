// Fara Album の Worker（worker/index.js）のテスト: 報告の経路、メンテナンス中の 503、メンテナンスの状態（/api/maintenance）。  node --test worker/index.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import worker, { errorSummary } from "./index.js";

const HOST = "https://vrc-album.fara-labs.com";

/** 静的アセットの代わり。ここまで来たら 418 を返す（API の経路で、アセットに流れていないことを確かめる）。 */
const ASSETS = { fetch: async () => new Response("asset", { status: 418 }) };

/** 静的アセットの代わりに HTML を返す（お知らせの帯の挿入を確かめる）。 */
const HTML_PAGE = {
  fetch: async () => new Response('<!doctype html><html><body><header>H</header><div class="site-scroll"><main>本文</main></div></body></html>', {
    headers: { "Content-Type": "text/html; charset=utf-8" },
  }),
};

const run = (path, init = {}, env = {}, assets = ASSETS) => worker.fetch(new Request(`${HOST}${path}`, init), { ASSETS: assets, ...env });

test("報告の設定（config）は、設定が無いとき enabled: false を返す（シークレットは返さない）", async () => {
  const res = await run("/api/report/config");
  assert.equal(res.status, 200);
  assert.deepEqual(await res.json(), { enabled: false, siteKey: null });
});

test("報告の設定（config）は、サイトキーだけを返す", async () => {
  const res = await run("/api/report/config", {}, { TURNSTILE_SITE_KEY: "site-key", TURNSTILE_SECRET: "secret-value", DISCORD_WEBHOOK_URL: "https://discord.example/x" });
  const body = await res.json();
  assert.deepEqual(body, { enabled: true, siteKey: "site-key" });
  assert.ok(!JSON.stringify(body).includes("secret-value"));
});

test("メンテナンス中（MAINTENANCE_MODE=true）は、報告の受け口も 503", async () => {
  const res = await run("/api/report", { method: "POST", headers: { "Content-Type": "application/json" }, body: "{}" }, { MAINTENANCE_MODE: "true" });
  assert.equal(res.status, 503);
  assert.equal(res.headers.get("Cache-Control"), "no-store");
});

test("メンテナンス中でも、報告の設定（config）は 503（API はまとめて止まる）", async () => {
  const res = await run("/api/report/config", {}, { MAINTENANCE_MODE: "true" });
  assert.equal(res.status, 503);
});

test("メンテナンス中のページは、HTML のメンテナンス画面を 503 で返す（アセットには流さない）", async () => {
  const res = await run("/posts/no-such-post-0000", { headers: { Accept: "text/html" } }, { MAINTENANCE_MODE: "true" }, HTML_PAGE);
  assert.equal(res.status, 503);
  assert.equal(res.headers.get("X-Robots-Tag"), "noindex");
  assert.match(await res.text(), /ただいまメンテナンス中です/);
});

test("報告以外の /api/ は、404（静的アセットには流さない）", async () => {
  const res = await run("/api/something-else");
  assert.equal(res.status, 404);
});

test("通常のとき、ページはそのままアセットから返す", async () => {
  const res = await run("/", {}, {}, HTML_PAGE);
  assert.equal(res.status, 200);
  assert.ok(!(await res.text()).includes("fsk-notice"));
});

test("お知らせ（MAINTENANCE_MODE=notice）のときも、ページの HTML には帯を入れない（アプリが /api/maintenance を読んで出す）", async () => {
  const res = await run("/", {}, { MAINTENANCE_MODE: "notice", MAINTENANCE_MESSAGE: "10月25日（土）20:00〜21:00 ごろ、サイトを更新します" }, HTML_PAGE);
  assert.equal(res.status, 200);
  assert.ok(!(await res.text()).includes("fsk-notice"));
});

test("/api/maintenance は、いまの状態（kit の MaintenanceInfo が読む形）を返す", async () => {
  for (const [env, state] of [[{}, "off"], [{ MAINTENANCE_MODE: "notice", MAINTENANCE_MESSAGE: "10月25日に更新します" }, "notice"], [{ MAINTENANCE_MODE: "true" }, "on"]]) {
    const res = await run("/api/maintenance", {}, env);
    assert.equal(res.status, 200);
    assert.equal(res.headers.get("Cache-Control"), "no-store");
    assert.equal((await res.json()).state, state);
  }
});

test("報告の受け口の例外は、ログに名前とメッセージだけを出す（URL・Bearer・token= は伏せる）。応答には詳細を返さない", async () => {
  const secret = new Error("boom https://discord.example/hook/abc Bearer s3cret-value token=t0ken-value");
  secret.name = "TypeError";
  const env = { ASSETS, get TURNSTILE_SITE_KEY() { throw secret; } };
  const logs = [];
  const original = console.error;
  console.error = (...args) => logs.push(args.join(" "));
  try {
    const res = await worker.fetch(new Request(`${HOST}/api/report/config`), env);
    assert.equal(res.status, 500);
    const body = await res.text();
    assert.ok(!body.includes("s3cret") && !body.includes("discord.example"));
  } finally {
    console.error = original;
  }
  const line = logs.join("\n");
  assert.match(line, /TypeError: boom \[url\] Bearer \[redacted\] token=\[redacted\]/);
  assert.ok(!line.includes("s3cret-value") && !line.includes("t0ken-value") && !line.includes("discord.example"));
});

test("errorSummary: 例外の名前とメッセージを出し、URL（Webhook の秘密を含む）は外す。長さも切る", () => {
  assert.equal(errorSummary(new TypeError("bad input")), "TypeError: bad input");
  const s = errorSummary(new Error("failed to fetch https://discord.example/api/webhooks/123/secret-token now"));
  assert.equal(s, "Error: failed to fetch [url] now");
  assert.ok(!s.includes("secret-token"));
  assert.ok(errorSummary(new Error("x y ".repeat(500))).length <= "Error: ".length + 200);
  assert.equal(errorSummary("plain"), "Error: plain");
});

test("errorSummary: URL 以外の秘密（Bearer・token=・token:・長い英数字）も取り除く", () => {
  assert.equal(errorSummary(new Error("auth failed Bearer abc.def-123 for user")), "Error: auth failed Bearer [redacted] for user");
  assert.equal(errorSummary(new Error("bad request token=sk_live_xyz9 in body")), "Error: bad request token=[redacted] in body");
  assert.equal(errorSummary(new Error("TOKEN: hunter2 rejected")), "Error: token=[redacted] rejected");
  // Webhook の token のような、長い英数字の連続（ハイフン・アンダースコアを含む）
  const webhookToken = "AbCdEf123456_ghijkLMNOP-qrstuvWXYZ0123";
  const s = errorSummary(new Error(`sent to ${webhookToken} failed`));
  assert.equal(s, "Error: sent to [redacted] failed");
  assert.ok(!s.includes(webhookToken));
  // 短い語（TypeError など）は残す
  assert.equal(errorSummary(new TypeError("cannot read properties of undefined")), "TypeError: cannot read properties of undefined");
});

test("errorSummary: 制御文字は空白にする", () => {
  assert.equal(errorSummary(new Error("a\nb")), "Error: a b");
});
