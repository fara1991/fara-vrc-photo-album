// テーマ（紫）の整合のテスト。  node --test tests/js/album-theme.test.mjs
// app.css の先頭の「テーマ」ブロックは、kit の既定の紫を上書きしすぎない（上書きは --fbc-focus だけ）。MudBlazor のパレットは kit の既定のまま（AlbumTheme.cs は角丸だけ）。
// 文字の色の比（WCAG AA）は、テーマの色と kit の既定の色の組み合わせで計算する。
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const css = readFileSync(new URL("../../src/FaraVrcAlbum.Web/wwwroot/css/app.css", import.meta.url), "utf8");
const cs = readFileSync(new URL("../../src/FaraVrcAlbum.Web/Layout/AlbumTheme.cs", import.meta.url), "utf8");
const kitCss = readFileSync(new URL("../../external/fara-blazor-component/src/FaraBlazorComponent.Razor/wwwroot/css/fara-blazor-component.css", import.meta.url), "utf8");

/** 「テーマ」ブロック（先頭から「テーマのブロックはここまで」まで）。 */
const themeBlock = css.slice(0, css.indexOf("（テーマのブロックはここまで）"));
const lightBlock = themeBlock.slice(themeBlock.search(/^:root \{/m), themeBlock.indexOf('html:root[data-bs-theme="dark"] {'));
const darkBlock = themeBlock.slice(themeBlock.indexOf('html:root[data-bs-theme="dark"] {'));

const variable = (block, name) => block.match(new RegExp(`${name}:\\s*(#[0-9a-fA-F]{6})`))?.[1].toLowerCase();
// kit の既定（:root と :root[data-bs-theme="dark"]）
const kitLight = kitCss.slice(kitCss.search(/^:root \{/m), kitCss.indexOf(':root[data-bs-theme="dark"] {'));
const kitDark = kitCss.slice(kitCss.indexOf(':root[data-bs-theme="dark"] {'), kitCss.search(/^html,/m));

const hex = (v) => [1, 3, 5].map((i) => parseInt(v.slice(i, i + 2), 16));
const lum = ([r, g, b]) => {
  const f = (c) => { const s = c / 255; return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4; };
  return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
};
const ratio = (a, b) => { const x = lum(hex(a)); const y = lum(hex(b)); return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05); };

test("ダークのテーマの変数は、kit の :root[data-bs-theme=dark] に負けない詳細度（html:root[...]）で書いている", () => {
  assert.ok(themeBlock.includes('html:root[data-bs-theme="dark"] {'), "html:root[data-bs-theme=\"dark\"] がありません");
});

test("kit の変数（--fbc-*）の上書きは、フォーカス枠だけ（それ以外は kit の既定の紫に任せる）", () => {
  const overridden = [...new Set([...themeBlock.replace(/\/\*[\s\S]*?\*\//g, "").matchAll(/(--fbc-[a-z-]+)\s*:/g)].map((m) => m[1]))];
  assert.deepEqual(overridden, ["--fbc-focus"]);
});

test("MudBlazor のパレットは kit の既定のまま（AlbumTheme.cs は色を上書きしない）", () => {
  assert.doesNotMatch(cs, /Palette(Light|Dark)\./);
});

test("kit の既定の色が紫（この前提が変わったら、このサイトの色も見直す）", () => {
  assert.equal(variable(kitLight, "--fbc-brand"), "#7c3aed");
  assert.equal(variable(kitDark, "--fbc-brand"), "#a855f7");
  assert.equal(variable(kitLight, "--fbc-body-bg"), "#f7f4fd");
  assert.equal(variable(kitDark, "--fbc-body-bg"), "#0c0616");
});

test("文字の色の比は AA（通常 4.5:1）以上（ライト・ダークとも）", () => {
  const lightBg = variable(kitLight, "--fbc-body-bg");
  const darkBg = variable(kitDark, "--fbc-body-bg");
  const checks = [
    ["ライト 本文", variable(lightBlock, "--album-text"), lightBg],
    ["ライト 補足", variable(kitLight, "--fbc-muted"), lightBg],
    ["ライト 補足（カードの上）", variable(lightBlock, "--album-muted"), variable(lightBlock, "--album-card-bg")],
    ["ライト リンク", variable(lightBlock, "--album-link-text"), lightBg],
    ["ライト 主ボタン（白文字）", "#ffffff", variable(kitLight, "--fbc-brand")],
    ["ライト フォーカス枠", variable(lightBlock, "--fbc-focus"), lightBg],
    ["ダーク 本文", variable(darkBlock, "--album-text"), darkBg],
    ["ダーク 補足", variable(kitDark, "--fbc-muted"), darkBg],
    ["ダーク 補足（カードの上）", variable(darkBlock, "--album-muted"), variable(darkBlock, "--album-card-bg")],
    ["ダーク リンク（カードの上）", variable(darkBlock, "--album-link-text"), variable(darkBlock, "--album-card-bg")],
    ["主ボタン（問い合わせの送信。グラデーションの明るい側と白文字）", "#ffffff", css.match(/\.fbc-btn-primary\.mud-button-root \{\s*background: linear-gradient\(135deg, (#[0-9a-f]{6})/i)?.[1]],
    ["ダーク フォーカス枠", variable(darkBlock, "--fbc-focus"), darkBg],
  ];
  for (const [name, fg, bg] of checks) {
    assert.ok(fg && bg, `${name}: 色を読めません`);
    assert.ok(ratio(fg, bg) >= 4.5, `${name}: ${fg} / ${bg} = ${ratio(fg, bg).toFixed(2)} は 4.5 未満`);
  }
});
