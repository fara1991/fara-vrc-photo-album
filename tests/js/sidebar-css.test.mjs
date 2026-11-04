// サイドバー（A 案）の寸法の整合のテスト。  node --test tests/js/sidebar-css.test.mjs
// ドロワーの幅（AlbumTheme.DrawerWidth）と、起動の前の静的 HTML の左余白（app.css の --album-drawer-w）が同じ値か。
// PC の細い列（Mini）と、スマホのドロワーの規則があるか。
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const css = readFileSync(new URL("../../src/FaraVrcAlbum.Web/wwwroot/css/app.css", import.meta.url), "utf8").replace(/\/\*[\s\S]*?\*\//g, "");
const cs = readFileSync(new URL("../../src/FaraVrcAlbum.Web/Layout/AlbumTheme.cs", import.meta.url), "utf8");

test("ドロワーの幅: AlbumTheme.DrawerWidth と --album-drawer-w が同じ値（304px のパネル + 左右 12px）", () => {
  const fromCs = cs.match(/DrawerWidth = "(\d+)px"/)?.[1];
  const fromCss = css.match(/--album-drawer-w:\s*(\d+)px/)?.[1];
  assert.ok(fromCs && fromCss);
  assert.equal(fromCss, fromCs);
  assert.equal(Number(fromCs), 304 + 12 * 2);
});

test("起動の前の静的 HTML の左余白とフッターの位置は、ドロワーの幅（--mud-drawer-width-left。無いときは --album-drawer-w）に合わせる", () => {
  assert.match(css, /padding-left:\s*calc\(var\(--mud-drawer-width-left, var\(--album-drawer-w\)\) \+ 1rem\)/);
  assert.match(css, /margin-left:\s*var\(--mud-drawer-width-left, var\(--album-drawer-w\)\)/);
  assert.doesNotMatch(css, /230px/);
});

test("PC で細くしている間（Mini）は、プロフィールカード・アーカイブ・バッジ・「>」を隠す。ラベルは読み上げのために残す（display: none にしない）", () => {
  const mini = css.match(/(\.fbc-sidebar\.mud-drawer--closed:not\(:hover\) \.album-profile,[\s\S]*?)\{\s*display:\s*none;/);
  assert.ok(mini, "Mini で隠す規則がありません");
  for (const name of ["album-profile", "album-archive", "album-nav-badge", "album-nav-chev"]) assert.match(mini[1], new RegExp(`\\.${name}`));
  assert.doesNotMatch(mini[1], /album-nav-label/);
  assert.match(css, /\.album-nav-label\s*\{[^}]*clip-path:\s*inset\(50%\)/);
});

test("スマホのドロワーは、画面の幅に収まるパネル（min(88vw, 320px)）。閉じるボタンはバナーの右上に重ねる", () => {
  assert.match(css, /width:\s*min\(88vw, 320px\)/);
  assert.match(css, /\.fbc-drawer-head\s*\{[^}]*position:\s*absolute/);
});

test("メニューの項目は 44px 以上のタップ領域（PC 52px・月 46px・Mini 44px）", () => {
  assert.match(css, /\.album-nav-item\s*\{[^}]*min-height:\s*52px/);
  assert.match(css, /\.album-nav-sub\s*\{[^}]*min-height:\s*46px/);
  assert.match(css, /album-nav-item\s*\{[^}]*min-height:\s*44px/);
});
