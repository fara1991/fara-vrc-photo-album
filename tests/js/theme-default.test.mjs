// 既定のテーマ（ダーク）と、保存のキーの一致のテスト。  node --test tests/js/theme-default.test.mjs
// 同じキーを 3 か所に書く（SiteInfo.cs・index.html の theme-init.js の data-*・js/theme-default.js）。ずれると、テーマが描画の前と後で別の場所を見る。
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";

const read = (p) => readFileSync(new URL(`../../${p}`, import.meta.url), "utf8");
const siteInfo = read("src/FaraVrcAlbum.Web/Services/SiteInfo.cs");
const indexHtml = read("src/FaraVrcAlbum.Web/wwwroot/index.html");
const themeDefault = read("src/FaraVrcAlbum.Web/wwwroot/js/theme-default.js");

const STORAGE_KEY = /StorageKey = "([^"]+)"/.exec(siteInfo)[1];
const SIDEBAR_KEY = /SidebarKey = "([^"]+)"/.exec(siteInfo)[1];

/** theme-default.js を、偽の document / localStorage で動かし、html の data-bs-theme を返す。 */
function run({ stored = null, storageThrows = false, initial = "light" } = {}) {
  const attrs = { "data-bs-theme": initial };
  const document = { documentElement: { setAttribute: (k, v) => { attrs[k] = v; }, getAttribute: (k) => attrs[k] } };
  const localStorage = { getItem: (k) => { if (storageThrows) throw new Error("denied"); return k === STORAGE_KEY ? stored : null; } };
  vm.runInNewContext(themeDefault, { document, localStorage });
  return attrs["data-bs-theme"];
}

test("保存のキーは、SiteInfo・index.html の theme-init.js・theme-default.js で同じ", () => {
  assert.equal(/data-storage-key="([^"]+)"/.exec(indexHtml)[1], STORAGE_KEY);
  assert.equal(/data-sidebar-key="([^"]+)"/.exec(indexHtml)[1], SIDEBAR_KEY);
  assert.equal(/var KEY = "([^"]+)"/.exec(themeDefault)[1], STORAGE_KEY);
  assert.equal(STORAGE_KEY, "fara-vrc-album.settings");
  assert.equal(SIDEBAR_KEY, "fara-vrc-album.sidebar-hidden");
});

test("保存が無いとき・保存が使えないときは、ダークにする（theme-init.js が light にした後）", () => {
  assert.equal(run({ stored: null }), "dark");
  assert.equal(run({ storageThrows: true }), "dark");
});

test("保存された値があれば、何もしない（ユーザーが選んだ light / dark をそのまま）", () => {
  assert.equal(run({ stored: JSON.stringify({ Theme: "light" }), initial: "light" }), "light");
  assert.equal(run({ stored: JSON.stringify({ Theme: "dark" }), initial: "dark" }), "dark");
});

test("index.html: html は dark で始まり、theme-init.js → theme-default.js の順に読む（インラインの script なし）", () => {
  assert.match(indexHtml, /<html lang="ja" data-bs-theme="dark">/);
  assert.ok(indexHtml.indexOf("theme-init.js") < indexHtml.indexOf("js/theme-default.js"));
  assert.doesNotMatch(indexHtml, /<script(?![^>]*\bsrc=)[^>]*>/);   // CSP: インラインの script は使わない
  assert.match(indexHtml, /<!-- SEO-HEAD:START -->[\s\S]*<!-- SEO-HEAD:END -->/);
  assert.match(indexHtml, /<div id="app">\s*<!-- SEO-BODY:START -->\s*<!-- SEO-BODY:END -->\s*<\/div>/);   // テンプレートの本文は空
});
