// 公開物の取り違えを止める関門（verify-site.mjs）のテスト。  node --test .github/scripts/verify-site.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import { join, dirname } from "node:path";
import { verifySite, headersBlock, ORIGINS } from "./verify-site.mjs";

const IMAGE_ORIGIN = "https://vrc-photos.fara-labs.com";
const CSP = `  Content-Security-Policy: default-src 'self'; img-src 'self' ${IMAGE_ORIGIN}; connect-src 'self' ${IMAGE_ORIGIN}\n`;
const DATA_RULES = "/data/*\n  X-Robots-Tag: noindex\n";

/** 環境ごとの、Build の出力に近い最小の公開物を作る */
function site(env, { noindex = env === "develop", sitemap = env === "production", canonical = ORIGINS[env] + "/", extra = {}, posts = [], imageBaseUrl = IMAGE_ORIGIN + "/" } = {}) {
  const dir = mkdtempSync(join(tmpdir(), "verify-site-"));
  const put = (name, text) => { mkdirSync(dirname(join(dir, name)), { recursive: true }); writeFileSync(join(dir, name), text); };
  put("index.html", `<html><head><link rel="canonical" href="${canonical}" /></head></html>`);
  put("404.html", "<html></html>");
  put("privacy.html", "<html></html>");
  put("report.html", "<html></html>");
  put("_redirects", "/page/1 / 301\n");
  put("_headers", `/*\n${CSP}${noindex ? "  X-Robots-Tag: noindex\n" : ""}\n${DATA_RULES}`);
  put("robots.txt", `User-agent: *\n${env === "production" ? `\nSitemap: ${ORIGINS.production}/sitemap.xml\n` : "Disallow: /\n"}`);
  put("feed.xml", "<rss/>");
  put("data/site.json", JSON.stringify({ imageBaseUrl }));
  put("data/posts-index.json", JSON.stringify(posts));
  put("data/archive.json", "{}");
  put("data/tags.json", "[]");
  put("data/worlds.json", "[]");
  put("data/outfits.json", "[]");
  put("_framework/blazor.webassembly.js", "");
  if (sitemap) put("sitemap.xml", "<urlset/>");
  for (const [name, text] of Object.entries(extra)) put(name, text);
  return dir;
}
const check = (env, claimed, options) => {
  const dir = site(env, options);
  try { return verifySite(dir, claimed); } finally { rmSync(dir, { recursive: true, force: true }); }
};

test("headersBlock: パスごとのルールを取り出す", () => {
  const text = "# コメント\n/*\n  A: 1\n/data/*\n  X-Robots-Tag: noindex\n";
  assert.equal(headersBlock(text, "/*"), "  A: 1");
  assert.match(headersBlock(text, "/data/*"), /X-Robots-Tag/);
  assert.equal(headersBlock(text, "/none"), null);
});

test("本番用の公開物は本番として通り、確認用として止まる", () => {
  assert.deepEqual(check("production", "production"), []);
  const asDevelop = check("production", "develop");
  assert.deepEqual(asDevelop, [
    "確認用のはずが、_headers の /* に X-Robots-Tag: noindex がありません（検索に載ってしまいます）",
    "確認用のはずが、sitemap.xml があります（本番用のビルドです）",
    `index.html の canonical が ${ORIGINS.develop}/ ではありません（${ORIGINS.production}/）`,
    `別の環境のホスト名（${ORIGINS.production}）が含まれています: index.html, robots.txt`,
  ]);
});

test("確認用の公開物は確認用として通り、本番として止まる", () => {
  assert.deepEqual(check("develop", "develop"), []);
  const asProduction = check("develop", "production");
  assert.deepEqual(asProduction, [
    "本番用のはずが、_headers の /* に X-Robots-Tag があります（確認用のビルドです）",
    "本番用のはずが、sitemap.xml がありません",
    `robots.txt に Sitemap: ${ORIGINS.production}/sitemap.xml がありません`,
    `robots.txt が全体を禁止しています`,
    `index.html の canonical が ${ORIGINS.production}/ ではありません（${ORIGINS.develop}/）`,
    `別の環境のホスト名（${ORIGINS.develop}）が含まれています: index.html`,
  ]);
});

test("本番に noindex が混ざる・canonical が違う・別の環境のホスト名が混ざると止まる", () => {
  assert.ok(check("production", "production", { noindex: true }).some((p) => p.includes("X-Robots-Tag")));
  assert.ok(check("production", "production", { canonical: "https://vrc-album-dev.fara-labs.com/" }).some((p) => p.includes("canonical")));
  assert.ok(check("production", "production", { extra: { "404.html": "<a href='https://vrc-album-dev.fara-labs.com/'>" } }).some((p) => p.includes("別の環境のホスト名")));
  assert.ok(check("production", "production", { extra: { "feed.xml": "<link>https://vrc-album-dev.fara-labs.com/</link>" } }).some((p) => p.includes("feed.xml")));
  assert.ok(check("production", "production", { extra: { "robots.txt": "User-agent: *\nDisallow: /\n" } }).length > 0);
});

test("/data/* の noindex が無いと、どちらの環境でも止まる（メッセージで特定する）", () => {
  for (const env of ["production", "develop"]) {
    const only = "/*\n" + CSP + (env === "develop" ? "  X-Robots-Tag: noindex\n" : "") ;
    const problems = check(env, env, { extra: { "_headers": only } });
    assert.deepEqual(problems, ["_headers に /data/* の X-Robots-Tag: noindex がありません"]);
  }
});

test("必要なファイルが欠けている・CSP がないと止まる", () => {
  const dir = site("production");
  try {
    rmSync(join(dir, "_framework"), { recursive: true });
    assert.ok(verifySite(dir, "production").some((p) => p.includes("_framework")));
    rmSync(join(dir, "feed.xml"));
    assert.ok(verifySite(dir, "production").some((p) => p.includes("feed.xml")));
  } finally { rmSync(dir, { recursive: true, force: true }); }
  assert.ok(check("production", "production", { extra: { "_headers": `/*\n  X-Frame-Options: DENY\n${DATA_RULES}` } }).some((p) => p.includes("Content-Security-Policy")));
});

test("環境の指定が不正なら止まる", () => {
  const dir = site("production");
  try {
    assert.ok(verifySite(dir, "staging").length > 0);
    assert.ok(verifySite(dir, "").length > 0);
    assert.ok(verifySite(dir, undefined).length > 0);
  } finally { rmSync(dir, { recursive: true, force: true }); }
});

// ---- 人物データ（名前は Git にも静的 HTML にも入れない。実行時に R2 から取得する） ----

test("人物データ（登録簿・名前の索引・記事の人物）は、どの出力にも出さない。site.json に peopleBaseUrl も無い", () => {
  for (const env of ["production", "develop"]) {
    assert.deepEqual(check(env, env), []);
    assert.ok(check(env, env, { extra: { "data/people/00.json": "{}" } }).some((p) => p.includes("人物データのファイル")));
    assert.ok(check(env, env, { extra: { "data/dev-people/people.json": "{}" } }).some((p) => p.includes("人物データのファイル")));
    assert.ok(check(env, env, { extra: { "data/n/ab.json": "{}" } }).some((p) => p.includes("人物データのファイル")));
    assert.ok(check(env, env, { extra: { "data/site.json": JSON.stringify({ imageBaseUrl: IMAGE_ORIGIN + "/", peopleBaseUrl: IMAGE_ORIGIN + "/people/" }) } }).some((p) => p.includes("peopleBaseUrl")));
  }
});

test("人物 ID（p_…）は、静的 HTML にも data の JSON にも出さない。p_ で始まらない語は誤検出しない", () => {
  for (const env of ["production", "develop"]) {
    assert.ok(check(env, env, { extra: { "posts/a.html": `<a data-id="p_8f3a1c0d2e4b">x</a>` } }).some((p) => p.includes("人物 ID")));
    assert.ok(check(env, env, { extra: { "data/misc.json": `{"photos":["p_8f3a1c0d2e4b"]}` } }).some((p) => p.includes("人物 ID")));
    assert.deepEqual(check(env, env, { extra: { "other.html": "<p>help_me_please_0123456789ab</p>" } }), []);
  }
});



test("本番に公開日が未来の記事があると止まる（予約公開は本番に出さない）。確認用は出してよい", () => {
  const posts = [{ slug: "later", publishedAt: "2099-01-01T00:00:00+09:00" }, { slug: "now", publishedAt: "2020-01-01T00:00:00+09:00" }];
  assert.ok(check("production", "production", { posts }).some((p) => p.includes("公開日が未来") && p.includes("later")));
  assert.deepEqual(check("develop", "develop", { posts }), []);
  assert.deepEqual(check("production", "production", { posts: [posts[1]] }), []);
  assert.ok(check("production", "production", { posts: [{ slug: "bad", publishedAt: "not a date" }] }).some((p) => p.includes("bad")));       // 読めない日付は通さない
});

// ---- 予約公開・集計と参照の整合・usr_id（本番の出力。変異させると落ちる）----

const PAST = "2020-01-01T00:00:00+09:00";
const FUTURE = "2099-01-01T00:00:00+09:00";
const U = "00000000-0000-4000-8000-000000000001";
/** 整合のとれた本番の出力（記事 a が 1 件、タグ night 1 件、ワールド 1 つ）に、変異を足す */
const consistent = (extra = {}) => ({
  posts: [{ slug: "a", publishedAt: PAST, tags: ["night"] }],
  extra: {
    "data/posts/a.json": JSON.stringify({ slug: "a", publishedAt: PAST }),
    "posts/a.html": "<html></html>",
    "data/tags.json": JSON.stringify([{ slug: "night", name: "夜景", postCount: 1 }]),
    "data/worlds.json": JSON.stringify([{ id: "wrld_" + U, name: "W", photoCount: 1 }]),
    [`data/worlds/${U}.json`]: JSON.stringify({ id: "wrld_" + U, name: "W", photos: [{ key: "k", slug: "a" }] }),
    "feed.xml": "<rss><channel><item><link>https://vrc-album.fara-labs.com/posts/a</link><pubDate>Wed, 01 Jan 2020 00:00:00 +0900</pubDate></item></channel></rss>",
    "sitemap.xml": "<urlset><url><loc>https://vrc-album.fara-labs.com/posts/a</loc><lastmod>2020-01-01</lastmod></url></urlset>",
    ...extra,
  },
});
const prod = (opts) => check("production", "production", opts);

test("整合のとれた本番の出力は通る", () => {
  assert.deepEqual(prod(consistent()), []);
});

test("本番: 未来の記事が、記事 JSON・HTML のファイル名・フィード・sitemap のどこかに残っていると止まる", () => {
  const c = consistent;
  const base = c();
  const run = (extra, posts = base.posts) => prod({ posts, extra: { ...base.extra, ...extra } });
  assert.ok(run({ "data/posts/later.json": JSON.stringify({ slug: "later", publishedAt: FUTURE }) }).some((p) => p.includes("data/posts/later.json")));
  assert.ok(run({ "data/posts/a.json": JSON.stringify({ slug: "a", publishedAt: FUTURE }) }).some((p) => p.includes("公開日が未来")));
  assert.ok(run({ "posts/later.html": "<html></html>" }).some((p) => p.includes("posts/later.html")));
  assert.ok(run({ "feed.xml": base.extra["feed.xml"].replace("Wed, 01 Jan 2020", "Thu, 01 Jan 2099") }).some((p) => p.includes("feed.xml") && p.includes("未来")));
  assert.ok(run({ "feed.xml": base.extra["feed.xml"].replace("/posts/a<", "/posts/later<") }).some((p) => p.includes("feed.xml") && p.includes("later")));
  assert.ok(run({ "sitemap.xml": base.extra["sitemap.xml"].replace("2020-01-01", "2099-01-01") }).some((p) => p.includes("sitemap.xml") && p.includes("lastmod")));
  assert.ok(run({ "sitemap.xml": base.extra["sitemap.xml"].replace("/posts/a<", "/posts/later<") }).some((p) => p.includes("sitemap.xml") && p.includes("later")));
  // 確認用は、未来の記事があってよい
  assert.deepEqual(check("develop", "develop", { posts: [{ slug: "later", publishedAt: FUTURE }], extra: { "posts/later.html": "<html></html>", "data/posts/later.json": "{}" } }), []);
});

test("本番: タグの件数・ワールドの枚数と参照が一覧とずれていると止まる", () => {
  const base = consistent();
  const run = (extra) => prod({ posts: base.posts, extra: { ...base.extra, ...extra } });
  assert.ok(run({ "data/tags.json": JSON.stringify([{ slug: "night", postCount: 2 }]) }).some((p) => p.includes("data/tags.json")));
  assert.ok(run({ "data/worlds.json": JSON.stringify([{ id: "wrld_" + U, name: "W", photoCount: 2 }]) }).some((p) => p.includes("data/worlds.json") && p.includes("枚数")));
  assert.ok(run({ "data/worlds.json": JSON.stringify([{ id: "wrld_" + U.replace("1", "2"), name: "W", photoCount: 1 }]) }).some((p) => p.includes("worlds/{uuid}.json がありません")));
  assert.ok(run({ [`data/worlds/${U}.json`]: JSON.stringify({ photos: [{ key: "k", slug: "later" }] }) }).some((p) => p.includes("later")));
  // 服の一覧・ページ
  assert.deepEqual(run({ "data/outfits.json": JSON.stringify([{ id: "coat-red", name: "X", photoCount: 1 }]), "data/outfits/coat-red.json": JSON.stringify({ id: "coat-red", photos: [{ key: "k", slug: "a" }] }) }), []);
  assert.ok(run({ "data/outfits.json": JSON.stringify([{ id: "coat-red", name: "X", photoCount: 1 }]) }).some((p) => p.includes("outfits/{id}.json がありません")));
  assert.ok(run({ "data/outfits.json": JSON.stringify([{ id: "coat-red", name: "X", photoCount: 2 }]), "data/outfits/coat-red.json": JSON.stringify({ photos: [{ key: "k", slug: "a" }] }) }).some((p) => p.includes("data/outfits.json") && p.includes("枚数")));
  assert.ok(run({ "data/outfits/coat-red.json": JSON.stringify({ photos: [{ key: "k", slug: "later" }] }) }).some((p) => p.includes("later")));
});

test("静的 HTML・フィード・sitemap・data の JSON に usr_id があるとどちらの環境でも止まる", () => {
  const usr = "usr_00000000-0000-0000-0000-000000000000";
  for (const env of ["production", "develop"]) {
    const run = (extra) => check(env, env, { extra });
    assert.ok(run({ "posts/a.html": `<a href="https://vrchat.com/home/user/${usr}">x</a>` }).some((p) => p.includes("usr_")));
    assert.ok(run({ "feed.xml": `<rss>${usr}</rss>` }).some((p) => p.includes("usr_")));
    assert.ok(run({ "data/misc.json": `{"x":"${usr}"}` }).some((p) => p.includes("usr_")), "data/ の JSON の usr_ を検出する");
    assert.ok(run({ "data/posts/a.json": `{"author":{"id":"${usr}"}}` }).some((p) => p.includes("usr_")));
    assert.deepEqual(run({ "data/misc.txt": `${usr}` }), []);                  // data/ でも JSON 以外は対象外
  }
  assert.ok(check("production", "production", { extra: { "sitemap.xml": "<urlset>usr_00000000-0000-0000-0000-000000000000</urlset>" } }).some((p) => p.includes("usr_")));
});

test("画像の基準 URL: 確認用も本番も R2 の画像ドメイン。取り違えると止まる", () => {
  assert.deepEqual(check("develop", "develop"), []);
  assert.deepEqual(check("production", "production"), []);
  assert.ok(check("production", "production", { imageBaseUrl: "images/dev-photos/" }).some((p) => p.includes("本番の data/site.json の imageBaseUrl")));
  assert.ok(check("develop", "develop", { imageBaseUrl: "images/dev-photos/" }).some((p) => p.includes("確認用の data/site.json の imageBaseUrl")));
  assert.ok(check("develop", "develop", { imageBaseUrl: "https://example.test/" }).some((p) => p.includes("確認用の data/site.json の imageBaseUrl")));
});

test("静的 HTML の写真が、その環境の画像ドメイン以外を指すと止まる。ヘッダーの画像はよい", () => {
  const page = (src) => ({ "tags.html": `<main><img src="${src}" alt=""></main>` });
  for (const env of ["production", "develop"]) {
    assert.deepEqual(check(env, env, { extra: page(IMAGE_ORIGIN + "/2026-10/a_640.webp") }), []);
    assert.deepEqual(check(env, env, { extra: page("images/header-43454dfe.webp") }), []);
    assert.deepEqual(check(env, env, { extra: page("https://booth.pximg.net/x/i/1/a_base_resized.jpg") }), []);   // 服のページの BOOTH の商品の画像
    assert.ok(check(env, env, { extra: page("https://booth.pximg.net.evil.test/a.jpg") }).some((p) => p.includes("画像が imageBaseUrl")));
    assert.ok(check(env, env, { extra: page("https://evil.test/a.webp") }).some((p) => p.includes("画像が imageBaseUrl")));
    assert.ok(check(env, env, { extra: page("images/dev-photos/2026-10/a_640.webp") }).some((p) => p.includes("画像が imageBaseUrl")));
  }
});

test("CI 用のサンプル記事（slug が sample-）が入った公開物は止まる。ALBUM_ALLOW_SAMPLE=1（CI のビルド）のときだけ通る", () => {
  const posts = [{ slug: "sample-2025-01-01", publishedAt: "2025-01-01T12:00:00+09:00" }];
  const saved = process.env.ALBUM_ALLOW_SAMPLE;
  try {
    delete process.env.ALBUM_ALLOW_SAMPLE;
    for (const env of ["production", "develop"]) {
      const problems = check(env, env, { posts });
      assert.equal(problems.length, 1, env);
      assert.match(problems[0], /サンプル記事.*sample-2025-01-01/, env);
    }
    process.env.ALBUM_ALLOW_SAMPLE = "1";
    assert.deepEqual(check("production", "production", { posts }), []);
    assert.deepEqual(check("develop", "develop", { posts }), []);
  } finally {
    if (saved === undefined) delete process.env.ALBUM_ALLOW_SAMPLE; else process.env.ALBUM_ALLOW_SAMPLE = saved;
  }
});
