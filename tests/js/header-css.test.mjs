// ヘッダーの高さ（PC 112px・スマホ 72px。768px で切り替え）が app.css に正しく書かれているかのテスト。  node --test tests/js/header-css.test.mjs
// 実際の描画（高さ・本文の上余白・ドロワーの上端）は、以前は tools/verify/m2-check.mjs（Playwright）で確かめた（削除。git 履歴 18603ce）。
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const css = readFileSync(new URL("../../src/FaraVrcAlbum.Web/wwwroot/css/app.css", import.meta.url), "utf8").replace(/\/\*[\s\S]*?\*\//g, "");

/** セレクターが行頭にある規則のうち、本体に must（文字列）を含む最初のものの本体。文字列の検索で探す（正規表現のエスケープを避ける）。 */
function rule(selector, must = "") {
  const key = `${selector} {`;
  let at = css.startsWith(key) ? 0 : css.indexOf(`
${key}`) + 1;
  while (at >= 0 && (at > 0 || css.startsWith(key))) {
    const open = css.indexOf("{", at);
    const body = css.slice(open + 1, css.indexOf("}", open));
    if (body.includes(must)) return body;
    const next = css.indexOf(`
${key}`, open);
    at = next < 0 ? -1 : next + 1;
  }
  assert.fail(`${selector} の規則（${must}）がありません`);
}

test("--album-header-h: kit の既定と同じ 3.5rem の 1 つだけ（スマホ用の上書きは無い）", () => {
  assert.match(rule(":root", "--album-header-h"), /--album-header-h:\s*3\.5rem/);
  assert.doesNotMatch(css, /--album-header-h:\s*(?!3\.5rem)[\d.]+(px|rem)/);          // ほかの値を足さない
  assert.doesNotMatch(css, /@media[^{]*\{\s*:root\s*\{[^}]*--album-header-h/);
});

test("kit の 3.5rem 固定のヘッダー・ツールバーと、MudBlazor の --mud-appbar-height・本文の上余白を、同じ変数に上書きしている", () => {
  assert.match(rule(".fsk-header.mud-appbar"), /height:\s*var\(--album-header-h\)/);
  const toolbar = rule(".fsk-header .mud-toolbar");
  assert.match(toolbar, /height:\s*var\(--album-header-h\)/);
  assert.match(toolbar, /min-height:\s*var\(--album-header-h\)/);
  assert.match(rule("html:root,\n.fsk-page.mud-layout"), /--mud-appbar-height:\s*var\(--album-header-h\)/);
  assert.match(rule(".fsk-page .mud-main-content"), /padding-top:\s*var\(--album-header-h\)/);
  assert.match(rule(".fsk-search-sheet"), /top:\s*var\(--album-header-h\)/);
  assert.match(rule("html"), /scroll-padding-top:\s*calc\(var\(--album-header-h\)/);
});

test("本文の最初の画像（.album-banner）は本文の幅いっぱい・縦横比 4:1 で場所を空ける", () => {
  const img = rule(".album-banner img");
  assert.match(img, /width:\s*100%/);
  assert.match(img, /aspect-ratio:\s*4 \/ 1/);
  assert.doesNotMatch(css, /\.album-brand/);          // ヘッダーの画像は無い（名前は kit の文字）
});
