// 公開する静的ファイル一式（Build の出力）が、公開先の環境（production / develop）のものか、wrangler deploy の前に確かめる。
// 「develop 用のビルドを本番に出す」「本番用のビルドを確認用に出す」取り違えを、公開の直前で止めるための関門（fail-closed）。
// avatar の .github/scripts/verify-site.mjs が元（このサイト向けの変更: ホスト名・必須ファイル・noindex の見方 = 下の注）。
//
//   node .github/scripts/verify-site.mjs <静的ファイルのフォルダ> <production|develop>
//
// 本番:   `/*` のルールに X-Robots-Tag なし（検索に載せる）・sitemap.xml あり（Sitemap: の行が robots.txt にある）・canonical は https://vrc-album.fara-labs.com/
// 確認用: `/*` のルールに X-Robots-Tag: noindex あり・sitemap.xml なし・canonical は https://vrc-album-dev.fara-labs.com/
// どちらも: 起動に要る WebAssembly の一式（_framework）と、本文入りの静的 HTML・404.html・_headers（CSP）・_redirects がある。
//
// 人名を一切出さない関門: どの出力（HTML・feed・sitemap・data/ の JSON）にも、VRChat のユーザー ID（usr_…）・人物 ID（p_…）が無く、
//   人物データのファイル（people・n/xx）も無い。ワールド名は出してよい。
// 予約公開: 本番の posts-index.json に、公開日が今より後の記事が無い（確認用は出す）。
//
// 注（avatar との違い）: 本番の _headers にも /data/* の X-Robots-Tag: noindex がある（データの JSON を検索に載せないため）。
// そのため noindex の有無は `/*` のルールの中だけを見る。
import { existsSync, readFileSync, readdirSync } from "node:fs";
import { join, relative } from "node:path";

export const ORIGINS = {
  production: "https://vrc-album.fara-labs.com",
  develop: "https://vrc-album-dev.fara-labs.com",
};

/** 写真の配信元（R2 の公開ドメイン）。確認用も本番も同じ写真を見る（合成画像は作らない）。 */
export const IMAGE_PRODUCTION = "https://vrc-photos.fara-labs.com/";

/** VRChat のユーザー ID（usr_ + UUID）。どの出力にも出さない。 */
const USER_ID = /usr_[0-9a-f]{8}-/;
/** 人物 ID（p_ + 小文字 16 進 12〜32 桁）。どの出力にも出さない（人名に戻る手がかりになるため）。 */
const PERSON_ID = /\bp_[0-9a-f]{12,32}\b/;

/** フォルダの中の HTML ファイル（相対パス）。 */
function htmlFiles(dir, base = dir) {
  const found = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    if (entry.name === "_framework" || entry.name === "_content") continue;
    const full = join(dir, entry.name);
    if (entry.isDirectory()) found.push(...htmlFiles(full, base));
    else if (entry.name.endsWith(".html")) found.push(relative(base, full).replaceAll("\\", "/"));
  }
  return found;
}

/** フォルダの中のすべてのパス（相対）。 */
function allPaths(dir, base = dir) {
  const found = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    found.push(relative(base, full).replaceAll("\\", "/"));
    if (entry.isDirectory()) found.push(...allPaths(full, base));
  }
  return found;
}

const read = (dir, name) => readFileSync(join(dir, name), "utf8");

/** _headers の、指定したパスのルール（そのパスの行から次のパスの行の手前まで）。無ければ null。 */
export function headersBlock(headers, path) {
  const lines = headers.split(/\r?\n/);
  let inside = false;
  const block = [];
  let found = false;
  for (const line of lines) {
    if (/^\S/.test(line) && !line.startsWith("#")) {
      inside = line.trim() === path;
      if (inside) found = true;
      continue;
    }
    if (inside) block.push(line);
  }
  return found ? block.join("\n") : null;
}

/** 本番の出力に、公開日が未来の記事（予約公開）が一部でも残っていないか。問題の一覧を返す。 */
export function verifyNoFuturePosts(dir) {
  const problems = [];
  const now = Date.now();
  const isFuture = (value) => !(Date.parse(value) <= now);        // 読めない日付も通さない
  const json = (name) => { try { return JSON.parse(read(dir, name)); } catch { problems.push(`${name} を JSON として読めません`); return null; } };
  const index = json("data/posts-index.json") ?? [];
  const slugs = new Set(index.map((p) => p.slug));
  const future = index.filter((p) => isFuture(p.publishedAt)).map((p) => p.slug);
  if (future.length > 0) problems.push(`本番の posts-index.json に、公開日が未来の記事があります（予約公開は本番に出さない）: ${future.slice(0, 3).join(", ")}`);
  const files = (sub, ext) => (existsSync(join(dir, sub)) ? readdirSync(join(dir, sub)).filter((f) => f.endsWith(ext)).map((f) => f.slice(0, -ext.length)) : []);
  for (const slug of files("data/posts", ".json")) {
    if (!slugs.has(slug)) problems.push(`data/posts/${slug}.json は posts-index.json に無い記事です（予約公開のファイルが残っている）`);
    else { const post = json(`data/posts/${slug}.json`); if (post && isFuture(post.publishedAt)) problems.push(`data/posts/${slug}.json の公開日が未来です`); }
  }
  for (const slug of files("posts", ".html")) if (!slugs.has(slug)) problems.push(`posts/${slug}.html は posts-index.json に無い記事です（予約公開のページが残っている）`);
  if (existsSync(join(dir, "feed.xml"))) {
    for (const m of read(dir, "feed.xml").matchAll(/<pubDate>([^<]+)<\/pubDate>/g)) if (isFuture(m[1])) problems.push(`feed.xml に公開日が未来の項目があります（${m[1]}）`);
    for (const m of read(dir, "feed.xml").matchAll(/<link>[^<]*\/posts\/([a-z0-9-]+)<\/link>/g)) if (!slugs.has(m[1])) problems.push(`feed.xml に posts-index.json に無い記事があります: ${m[1]}`);
  }
  if (existsSync(join(dir, "sitemap.xml"))) {
    const sitemap = read(dir, "sitemap.xml");
    for (const m of sitemap.matchAll(/<lastmod>([^<]+)<\/lastmod>/g)) if (isFuture(m[1])) problems.push(`sitemap.xml に未来の lastmod があります（${m[1]}）`);
    for (const m of sitemap.matchAll(/\/posts\/([a-z0-9-]+)</g)) if (!slugs.has(m[1])) problems.push(`sitemap.xml に posts-index.json に無い記事があります: ${m[1]}`);
  }
  // タグの件数・ワールドの枚数と参照が、一覧（posts-index）と合っている
  for (const tag of json("data/tags.json") ?? []) {
    const count = index.filter((p) => (p.tags ?? []).includes(tag.slug)).length;
    if (count !== tag.postCount) problems.push(`data/tags.json の ${tag.slug} の件数（${tag.postCount}）が posts-index.json の記事数（${count}）と違います`);
  }
  for (const world of json("data/worlds.json") ?? []) {
    const page = existsSync(join(dir, `data/worlds/${String(world.id).slice(5)}.json`)) ? json(`data/worlds/${String(world.id).slice(5)}.json`) : null;
    if (!page) problems.push(`data/worlds.json の ${world.id} に data/worlds/{uuid}.json がありません`);
    else {
      if (page.photos.length !== world.photoCount) problems.push(`data/worlds.json の ${world.id} の枚数（${world.photoCount}）が data/worlds/{uuid}.json（${page.photos.length}）と違います`);
      for (const photo of page.photos) if (!slugs.has(photo.slug)) problems.push(`data/worlds/ に posts-index.json に無い記事の写真があります: ${photo.slug}`);
    }
  }
  for (const name of existsSync(join(dir, "data/worlds")) ? readdirSync(join(dir, "data/worlds")) : []) {
    for (const photo of json(`data/worlds/${name}`)?.photos ?? []) if (!slugs.has(photo.slug)) problems.push(`data/worlds/${name} に posts-index.json に無い記事の写真があります: ${photo.slug}`);
  }
  // 服の一覧・ページ（data/outfits.json があるとき）: 枚数と参照が、記事の一覧と合っている
  for (const outfit of json("data/outfits.json") ?? []) {
    const file = `data/outfits/${outfit.id}.json`;
    const page = existsSync(join(dir, file)) ? json(file) : null;
    if (!page) problems.push(`data/outfits.json の ${outfit.id} に data/outfits/{id}.json がありません`);
    else {
      if (page.photos.length !== outfit.photoCount) problems.push(`data/outfits.json の ${outfit.id} の枚数（${outfit.photoCount}）が data/outfits/{id}.json（${page.photos.length}）と違います`);
      for (const photo of page.photos) if (!slugs.has(photo.slug)) problems.push(`data/outfits/ に posts-index.json に無い記事の写真があります: ${photo.slug}`);
    }
  }
  for (const name of existsSync(join(dir, "data/outfits")) ? readdirSync(join(dir, "data/outfits")) : []) {
    for (const photo of json(`data/outfits/${name}`)?.photos ?? []) if (!slugs.has(photo.slug)) problems.push(`data/outfits/${name} に posts-index.json に無い記事の写真があります: ${photo.slug}`);
  }
  return [...new Set(problems)];
}

/**
 * @param {string} dir 静的ファイルのフォルダ
 * @param {string} env "production" か "develop"
 * @returns {string[]} 問題の一覧（空なら公開してよい）
 */
export function verifySite(dir, env) {
  const origin = ORIGINS[env];
  if (!origin) return [`環境の指定が不正です: ${JSON.stringify(env)}（production か develop）`];
  const problems = [];

  for (const name of ["index.html", "404.html", "privacy.html", "report.html", "_headers", "_redirects", "robots.txt", "data/site.json", "data/posts-index.json", "data/archive.json", "data/tags.json", "data/worlds.json", "data/outfits.json", "feed.xml"]) {
    if (!existsSync(join(dir, name))) problems.push(`${name} がありません`);
  }
  if (!existsSync(join(dir, "_framework")) || !readdirSync(join(dir, "_framework")).some((f) => f.startsWith("blazor.webassembly"))) {
    problems.push("_framework/blazor.webassembly* がありません（dotnet publish の出力ではありません）");
  }
  if (problems.length > 0) return problems;

  // CI 用の架空のサンプル記事（samples/content。slug が sample- で始まる）が入った出力は、公開に使えない。CI のビルドだけ ALBUM_ALLOW_SAMPLE=1 で通す
  if (process.env.ALBUM_ALLOW_SAMPLE !== "1") {
    let slugs = [];
    try { slugs = JSON.parse(read(dir, "data/posts-index.json")).map((p) => String(p.slug)); } catch { return ["data/posts-index.json を JSON として読めません"]; }
    const samples = slugs.filter((slug) => slug.startsWith("sample-"));
    if (samples.length > 0) return [`CI 用のサンプル記事が入っています（本物の content/ から作り直してください）: ${samples.slice(0, 3).join(", ")}`];
  }

  const headers = read(dir, "_headers");
  const robots = read(dir, "robots.txt");
  const index = read(dir, "index.html");
  const canonical = /<link rel="canonical" href="([^"]*)"/.exec(index)?.[1];

  const all = headersBlock(headers, "/*");
  if (all === null) problems.push("_headers に /* のルールがありません");
  if (!/^\s*Content-Security-Policy: /m.test(all ?? "")) problems.push("_headers の /* に Content-Security-Policy がありません");
  const noindex = /^\s*X-Robots-Tag:\s*noindex/mi.test(all ?? "");

  if (env === "production") {
    if (noindex) problems.push("本番用のはずが、_headers の /* に X-Robots-Tag があります（確認用のビルドです）");
    if (!existsSync(join(dir, "sitemap.xml"))) problems.push("本番用のはずが、sitemap.xml がありません");
    if (!robots.includes(`Sitemap: ${origin}/sitemap.xml`)) problems.push(`robots.txt に Sitemap: ${origin}/sitemap.xml がありません`);
    if (/^\s*Disallow:\s*\/\s*$/m.test(robots)) problems.push("robots.txt が全体を禁止しています");
  } else {
    if (!noindex) problems.push("確認用のはずが、_headers の /* に X-Robots-Tag: noindex がありません（検索に載ってしまいます）");
    if (existsSync(join(dir, "sitemap.xml"))) problems.push("確認用のはずが、sitemap.xml があります（本番用のビルドです）");
  }
  // データ（記事 JSON・ワールドの JSON）は検索に載せない
  if (!/X-Robots-Tag:\s*noindex/i.test(headersBlock(headers, "/data/*") ?? "")) problems.push("_headers に /data/* の X-Robots-Tag: noindex がありません");
  if (canonical !== `${origin}/`) problems.push(`index.html の canonical が ${origin}/ ではありません（${canonical ?? "なし"}）`);

  // 写真の配信元: 確認用も本番も R2 の画像ドメイン
  let site = {};
  try { site = JSON.parse(read(dir, "data/site.json")); } catch { problems.push("data/site.json を JSON として読めません"); }
  const imageBase = site.imageBaseUrl ?? "";
  if (imageBase !== IMAGE_PRODUCTION) {
    problems.push(`${env === "production" ? "本番" : "確認用"}の data/site.json の imageBaseUrl が ${IMAGE_PRODUCTION} ではありません（${JSON.stringify(imageBase)}）`);
  }

  if ("peopleBaseUrl" in site) problems.push("data/site.json に peopleBaseUrl があります（人物データは出さない）");
  // 人名を一切出さない: 人物データのファイル・人物 ID（p_）・ユーザー ID（usr_）が、どの出力にも無い
  const personFiles = allPaths(dir).filter((p) => p.includes("people") || /(^|\/)n\/[0-9a-f]{2}\.json$/.test(p));
  if (personFiles.length > 0) problems.push(`人物データのファイルが出力にあります: ${personFiles.slice(0, 3).join(", ")}`);
  const textFiles = [...htmlFiles(dir), "feed.xml", "sitemap.xml", "robots.txt", ...allPaths(dir).filter((p) => p.startsWith("data/") && p.endsWith(".json"))]
    .filter((name) => existsSync(join(dir, name)));
  const withPerson = textFiles.filter((name) => PERSON_ID.test(read(dir, name)));
  if (withPerson.length > 0) problems.push(`人物 ID（p_…）が出力にあります: ${withPerson.slice(0, 3).join(", ")}`);
  const withUsr = textFiles.filter((name) => USER_ID.test(read(dir, name)));
  if (withUsr.length > 0) problems.push(`VRChat のユーザー ID（usr_…）が出力にあります: ${withUsr.slice(0, 3).join(", ")}`);

  // 静的 HTML の写真は、その環境の画像の基準 URL（ヘッダーの画像などサイト内の images/header-* は別）から読む。
  // 例外: 服のページ・一覧の BOOTH の商品の画像（https://booth.pximg.net/。CSP の imgSrc にも同じホストを許可している）
  const allowedImageHosts = ["https://booth.pximg.net/"];
  const strayImages = [];
  for (const name of htmlFiles(dir)) {
    for (const m of read(dir, name).matchAll(/<img\b[^>]*?\bsrc="([^"]+)"/g)) {
      if (!m[1].startsWith(imageBase) && !m[1].startsWith("images/header-") && !allowedImageHosts.some((host) => m[1].startsWith(host))) strayImages.push(`${name}: ${m[1]}`);
    }
  }
  if (strayImages.length > 0) problems.push(`静的 HTML の画像が imageBaseUrl（${imageBase}）の外を指しています: ${strayImages.slice(0, 3).join(", ")}`);
  // 予約公開: 本番に、公開日が今より後の記事を出さない（一覧・記事 JSON・記事 HTML のファイル名・フィード・sitemap・タグ/ワールドの集計と参照）
  if (env === "production") problems.push(...verifyNoFuturePosts(dir));

  // 別の環境のホスト名が、ほかの環境の出力に混ざっていない（本番の出力に vrc-album-dev、確認用の出力に vrc-album.fara-labs.com の URL がない）
  const other = env === "production" ? ORIGINS.develop : ORIGINS.production;
  const stray = [];
  for (const name of ["index.html", "404.html", "privacy.html", "report.html", "robots.txt", "data/site.json", "feed.xml"]) {
    if (existsSync(join(dir, name)) && read(dir, name).includes(other)) stray.push(name);
  }
  if (stray.length > 0) problems.push(`別の環境のホスト名（${other}）が含まれています: ${stray.join(", ")}`);
  return problems;
}

if (process.argv[1]?.endsWith("verify-site.mjs")) {
  const [dir, env] = process.argv.slice(2);
  if (!dir || !env) {
    console.error("使い方: node .github/scripts/verify-site.mjs <フォルダ> <production|develop>");
    process.exit(2);
  }
  const problems = verifySite(dir, env);
  if (problems.length > 0) {
    for (const p of problems) console.error(`::error::${p}`);
    console.error(`${dir} は ${env} 用の公開物として確認できませんでした。公開を止めます`);
    process.exit(1);
  }
  console.log(`${dir} は ${env} 用の公開物です（canonical ${ORIGINS[env]}/、${env === "production" ? "noindex なし・sitemap あり" : "noindex あり・sitemap なし"}）`);
}
