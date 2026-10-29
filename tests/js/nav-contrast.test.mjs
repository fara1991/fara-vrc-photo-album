// サイドバーの選択中の項目（ナビの「ホーム」など）の文字色が、WCAG AA（通常の文字 4.5:1）に届くかのテスト。  node --test tests/js/nav-contrast.test.mjs
// MudBlazor の .mud-navmenu-default .mud-nav-link.active（詳細度 0,4,0）が、テーマの Primary を文字色にするため、app.css で同じ詳細度以上に上書きしている。
// 比は、最も明るい背景（サイドバーの下端に薄い白 8% を重ねた色）に対して計算する。実描画の計測は docs と PR に書いた（Playwright・画素から測定）。
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const css = readFileSync(new URL("../../src/FaraVrcAlbum.Web/wwwroot/css/app.css", import.meta.url), "utf8").replace(/\/\*[\s\S]*?\*\//g, "");

/** 16 進の色（#rrggbb）を [r, g, b] にする。 */
function hex(value) {
  const v = value.replace("#", "");
  return [0, 2, 4].map((i) => parseInt(v.slice(i, i + 2), 16));
}

/** WCAG 2.x の相対輝度。 */
function luminance([r, g, b]) {
  const f = (c) => {
    const s = c / 255;
    return s <= 0.03928 ? s / 12.92 : ((s + 0.055) / 1.055) ** 2.4;
  };
  return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
}

/** 文字色と背景色の比（明るい方を分子にする）。 */
function ratio(a, b) {
  const la = luminance(a);
  const lb = luminance(b);
  return (Math.max(la, lb) + 0.05) / (Math.min(la, lb) + 0.05);
}

/** 色 fg を、不透明度 alpha で背景 bg に重ねる。 */
function over(fg, bg, alpha) {
  return fg.map((c, i) => c * alpha + bg[i] * (1 - alpha));
}

test("選択中の文字色のトークンは、サイドバーの最も明るい背景の上で AA（4.5:1）に届く", () => {
  const token = css.match(/--album-nav-active:\s*(#[0-9a-f]{6})/i);
  assert.ok(token, "--album-nav-active がありません");
  // サイドバーの下端（#421a2d を 0.92 で #1e0d17 に重ねる。app.css のテーマの --fbc-sidebar-bg・--album-sidebar-solid）に、選択中の薄い白 8% を重ねた色（最も明るい場合）
  const sidebarBottom = over(hex("#421a2d"), hex("#1e0d17"), 0.92);
  const activeBg = over([255, 255, 255], sidebarBottom, 0.08);
  assert.ok(ratio(hex(token[1]), activeBg) >= 4.5, `比 ${ratio(hex(token[1]), activeBg).toFixed(2)} は 4.5 未満`);
});

test("選択中のナビの文字色は、MudBlazor の Primary（ダーク #d6336c・ライト #b83a64）ではなく、トークンを使う（同じ詳細度以上の規則）", () => {
  const rule = css.match(/([^{}]*\.fbc-sidebar\s+\.mud-navmenu\.mud-navmenu-default\s+\.mud-nav-link\.active)\s*\{([^}]*)\}/);
  assert.ok(rule, "選択中の項目の規則がありません（.fbc-sidebar .mud-navmenu.mud-navmenu-default .mud-nav-link.active）");
  assert.match(rule[2], /color:\s*var\(--album-nav-active\)/);
});

test("ダーク・ライトで、サイドバーの選択中の文字色は同じトークン（テーマごとに別の色にしない）", () => {
  assert.doesNotMatch(css, /html\[data-bs-theme="(dark|light)"\][^{]*\{[^}]*--album-nav-active/);
});
