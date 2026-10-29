// テーマ（ローズ寄りの色）の整合のテスト。  node --test tests/js/album-theme.test.mjs
// app.css の先頭の「テーマ」ブロックの色と、Layout/AlbumTheme.cs（MudBlazor のパレット）が同じ値か。kit の既定（紫）が残っていないか。
// 文字の色の比（WCAG AA）は、テーマの色の組み合わせで計算する。
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const css = readFileSync(new URL("../../src/FaraVrcAlbum.Web/wwwroot/css/app.css", import.meta.url), "utf8");
const cs = readFileSync(new URL("../../src/FaraVrcAlbum.Web/Layout/AlbumTheme.cs", import.meta.url), "utf8");

/** 「テーマ」ブロック（先頭から「テーマのブロックはここまで」まで）。 */
const themeBlock = css.slice(0, css.indexOf("（テーマのブロックはここまで）"));
const lightBlock = themeBlock.slice(themeBlock.indexOf(":root {"), themeBlock.indexOf(':root[data-bs-theme="dark"] {'));
const darkBlock = themeBlock.slice(themeBlock.indexOf(':root[data-bs-theme="dark"] {'));

const variable = (block, name) => block.match(new RegExp(`${name}:\\s*(#[0-9a-fA-F]{6})`))?.[1].toLowerCase();
const palette = (kind, name) => cs.match(new RegExp(`Palette${kind}\\.${name} = "(#[0-9a-fA-F]{6})"`))?.[1].toLowerCase();

const hex = (v) => [1, 3, 5].map((i) => parseInt(v.slice(i, i + 2), 16));
const lum = ([r, g, b]) => {
  const f = (c) => { const s = c / 255; return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4; };
  return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
};
const ratio = (a, b) => { const x = lum(hex(a)); const y = lum(hex(b)); return (Math.max(x, y) + 0.05) / (Math.min(x, y) + 0.05); };

test("ダークのテーマの変数は、kit の :root[data-bs-theme=dark] に負けない詳細度（html:root[...]）で書いている", () => {
  assert.ok(themeBlock.includes('html:root[data-bs-theme="dark"] {'), "html:root[data-bs-theme=\"dark\"] がありません");
  assert.doesNotMatch(themeBlock, /\nhtml\[data-bs-theme="dark"\] \{\n {2}--fbc-/);
});

test("MudBlazor のパレット（AlbumTheme.cs）は、CSS のテーマの色と同じ値", () => {
  const pairs = [
    ["Light", "Primary", lightBlock, "--fbc-brand"],
    ["Light", "Background", lightBlock, "--fbc-body-bg"],
    ["Light", "TextPrimary", lightBlock, "--album-text"],
    ["Light", "TextSecondary", lightBlock, "--fbc-muted"],
    ["Light", "DrawerBackground", lightBlock, "--album-sidebar-solid"],
    ["Dark", "Primary", darkBlock, "--fbc-brand"],
    ["Dark", "Background", darkBlock, "--fbc-body-bg"],
    ["Dark", "Surface", darkBlock, "--album-card-bg"],
    ["Dark", "TextPrimary", darkBlock, "--album-text"],
    ["Dark", "TextSecondary", darkBlock, "--fbc-muted"],
  ];
  for (const [kind, name, block, v] of pairs) {
    const fromCss = variable(block, v);
    const fromCs = palette(kind, name);
    assert.ok(fromCss && fromCs, `${kind}.${name} / ${v} を読めません`);
    assert.equal(fromCs, fromCss, `${kind}.${name} (${fromCs}) と ${v} (${fromCss}) が違います`);
  }
});

test("kit の既定の紫の色が、テーマの値に残っていない", () => {
  for (const purple of ["#7c3aed", "#a855f7", "#c084fc", "#e879f9", "#e9d5ff", "#f7f4fd", "#0c0616"]) {
    assert.ok(!themeBlock.replace(/\/\*[\s\S]*?\*\//g, "").toLowerCase().includes(purple), `テーマのブロックに ${purple} があります`);
    assert.ok(!cs.toLowerCase().includes(purple), `AlbumTheme.cs に ${purple} があります`);
  }
});

test("文字の色の比は AA（通常 4.5:1）以上（ライト・ダークとも）", () => {
  const checks = [
    ["ライト 本文", variable(lightBlock, "--album-text"), variable(lightBlock, "--fbc-body-bg")],
    ["ライト 補足", variable(lightBlock, "--fbc-muted"), variable(lightBlock, "--fbc-body-bg")],
    ["ライト リンク", variable(lightBlock, "--album-link-text"), variable(lightBlock, "--fbc-body-bg")],
    ["ライト 補足（カードの上）", variable(lightBlock, "--album-muted"), variable(lightBlock, "--album-card-bg")],
    ["ライト 主ボタン（白文字）", "#ffffff", variable(lightBlock, "--fbc-brand")],
    ["ライト フォーカス枠", variable(lightBlock, "--fbc-focus"), variable(lightBlock, "--fbc-body-bg")],
    ["ダーク 本文", variable(darkBlock, "--album-text"), variable(darkBlock, "--fbc-body-bg")],
    ["ダーク 補足", variable(darkBlock, "--fbc-muted"), variable(darkBlock, "--fbc-body-bg")],
    ["ダーク リンク（カードの上）", variable(darkBlock, "--album-link-text"), variable(darkBlock, "--album-card-bg")],
    ["ダーク 補足（カードの上）", variable(darkBlock, "--album-muted"), variable(darkBlock, "--album-card-bg")],
    ["ダーク 主ボタン（白文字）", "#ffffff", variable(darkBlock, "--fbc-brand")],
    ["ダーク フォーカス枠", variable(darkBlock, "--fbc-focus"), variable(darkBlock, "--fbc-body-bg")],
  ];
  for (const [name, fg, bg] of checks) {
    assert.ok(fg && bg, `${name}: 色を読めません`);
    assert.ok(ratio(fg, bg) >= 4.5, `${name}: ${fg} / ${bg} = ${ratio(fg, bg).toFixed(2)} は 4.5 未満`);
  }
});
