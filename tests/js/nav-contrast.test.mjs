// サイドバー（A 案）の文字の色が、WCAG AA（通常の文字 4.5:1）に届くかのテスト。  node --test tests/js/nav-contrast.test.mjs
// 色は app.css のテーマのブロックの変数（--album-sidebar-*・--album-current-*・--album-badge-*）から読む。サイドバーは両テーマで暗い。
// 背景は、パネルの上に項目の薄い白（7%・ホバー 14%）を重ねた色のうち、文字から最も遠い（明るい）ものに対して計算する。実描画の計測は PR に書いた（Playwright・画素から測定）。
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const css = readFileSync(new URL("../../src/FaraVrcAlbum.Web/wwwroot/css/app.css", import.meta.url), "utf8").replace(/\/\*[\s\S]*?\*\//g, "");

/** トークン（--album-…: #rrggbb）の値。 */
function token(name) {
  const m = css.match(new RegExp(`${name}:\\s*(#[0-9a-fA-F]{6})`));
  assert.ok(m, `${name} がありません`);
  return hex(m[1]);
}

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

const WHITE = [255, 255, 255];
const panel = token("--album-sidebar-panel");
const panel2 = token("--album-sidebar-panel2");
const itemHover = over(WHITE, panel, 0.14);      // 項目の背景のうち最も明るい（ホバー）
const link = token("--album-sidebar-link");
const muted = token("--album-sidebar-muted");

function atLeast(name, fg, bg, min) {
  const r = ratio(fg, bg);
  assert.ok(r >= min, `${name}: ${r.toFixed(2)}:1 は ${min}:1 未満`);
}

test("項目の文字・補足は、項目の最も明るい背景（ホバー）の上で AA（4.5:1）", () => {
  atLeast("項目の文字", link, itemHover, 4.5);
  atLeast("補足（見出し・「>」）", muted, panel, 4.5);
  atLeast("補足（ホバーの項目の上の「>」）", muted, itemHover, 4.5);
  atLeast("ホバーの文字（白）", WHITE, itemHover, 4.5);
});

test("現在のページ（紫の塗りつぶし）の白文字は、グラデーションの両端で AA", () => {
  atLeast("白 / 塗りつぶしの始点", WHITE, token("--album-current-from"), 4.5);
  atLeast("白 / 塗りつぶしの終点", WHITE, token("--album-current-to"), 4.5);
});

test("件数のバッジの文字は AA", () => {
  atLeast("バッジ", token("--album-badge-text"), token("--album-badge-bg"), 4.5);
});

test("プロフィールカード: 名前（白）・補足・件数のチップの文字は AA", () => {
  atLeast("名前", WHITE, panel2, 4.5);
  atLeast("補足", muted, panel2, 4.5);
  atLeast("件数（白）", WHITE, over(WHITE, panel2, 0.09), 4.5);
  atLeast("件数の単位", link, over(WHITE, panel2, 0.09), 4.5);
});

test("アイコン・フォーカス枠は非テキストの 3:1 以上", () => {
  const iconBg = over(token("--album-sidebar-icon"), itemHover, 0.16);
  atLeast("左のアイコン", token("--album-sidebar-icon"), iconBg, 3);
  atLeast("フォーカス枠（パネルの上）", token("--album-focus-on-dark"), panel, 3);
});

test("選択中の印は色だけでなく aria-current（マークアップ）で示す。CSS はそれを見る", () => {
  assert.match(css, /\.album-nav-item\[aria-current="page"\]\s*\{[^}]*linear-gradient/);
});
