// js/lightbox.js の純粋な関数（循環・ハッシュ・スワイプ・フォーカスの回り方）のテスト。  node --test tests/js/lightbox.test.mjs
// DOM を使う部分（ダイアログの開閉・キー・フォーカス戻し）は、tools/verify（Playwright。m2-check は削除し git 履歴 18603ce に残す）で確かめていた。
import { test } from "node:test";
import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";

const source = readFileSync(new URL("../../src/FaraVrcAlbum.Web/wwwroot/js/lightbox.js", import.meta.url), "utf8");
const window = {};
vm.runInNewContext(source, { window, URL });          // document が無い環境でも、純粋な関数だけを出して終わる（DOM には触らない）
const lb = window.faraAlbumLightbox;

test("wrapIndex: 前後への移動は循環する（0 枚・1 枚でも壊れない）", () => {
  assert.equal(lb.wrapIndex(0, 5), 0);
  assert.equal(lb.wrapIndex(5, 5), 0);
  assert.equal(lb.wrapIndex(-1, 5), 4);
  assert.equal(lb.wrapIndex(-6, 5), 4);
  assert.equal(lb.wrapIndex(7, 5), 2);
  assert.equal(lb.wrapIndex(3, 1), 0);
  assert.equal(lb.wrapIndex(3, 0), 0);
});

test("parsePhotoHash: #photo-N（1 始まり）を 0 始まりの番号にする。範囲外・形式違いは -1", () => {
  assert.equal(lb.parsePhotoHash("#photo-1", 12), 0);
  assert.equal(lb.parsePhotoHash("#photo-12", 12), 11);
  assert.equal(lb.parsePhotoHash("#photo-13", 12), -1);
  assert.equal(lb.parsePhotoHash("#photo-0", 12), -1);
  assert.equal(lb.parsePhotoHash("#photo-", 12), -1);
  assert.equal(lb.parsePhotoHash("#photo-1x", 12), -1);
  assert.equal(lb.parsePhotoHash("#photo--1", 12), -1);
  assert.equal(lb.parsePhotoHash("#other", 12), -1);
  assert.equal(lb.parsePhotoHash("", 12), -1);
  assert.equal(lb.parsePhotoHash(undefined, 12), -1);
  assert.equal(lb.parsePhotoHash("#photo-1", 0), -1);
  assert.equal(lb.parsePhotoHash("#photo-99999", 100000), -1);             // 桁数の上限
});

test("parsePhotoHash: 境界（前後に余計な文字があると開かない。先頭の ^・末尾の $ が必要）", () => {
  assert.equal(lb.parsePhotoHash("x#photo-1", 12), -1);          // 先頭の ^ を外すと 0 になってしまう
  assert.equal(lb.parsePhotoHash(" #photo-1", 12), -1);
  assert.equal(lb.parsePhotoHash("/posts/a#photo-1", 12), -1);
  assert.equal(lb.parsePhotoHash("#photo-1#photo-2", 12), -1);
  assert.equal(lb.parsePhotoHash("#photo-1\n", 12), -1);          // 末尾の $ を外すと 0 になってしまう
  assert.equal(lb.parsePhotoHash("#photo-1/", 12), -1);
  assert.equal(lb.parsePhotoHash("#PHOTO-1", 12), -1);
  assert.equal(lb.parsePhotoHash("#photo-12345", 100000), -1);   // 5 桁以上は受けない
  assert.equal(lb.parsePhotoHash("#photo-1234", 5000), 1233);
});

test("hashFor は parsePhotoHash の逆", () => {
  for (const i of [0, 1, 11]) assert.equal(lb.parsePhotoHash(lb.hashFor(i), 12), i);
  assert.equal(lb.hashFor(2), "#photo-3");
});

test("swipeDirection: 左へ払う = 次（1）、右へ払う = 前（-1）。短い・縦の動きは 0", () => {
  assert.equal(lb.swipeDirection(-80, 5), 1);
  assert.equal(lb.swipeDirection(80, -5), -1);
  assert.equal(lb.swipeDirection(30, 0), 0);                 // 短い
  assert.equal(lb.swipeDirection(-60, 100), 0);              // 縦のスクロール
  assert.equal(lb.swipeDirection(-60, 40), 1);               // 斜めでも横が勝てば移動
  assert.equal(lb.swipeDirection(-60, 41), 0);
  assert.equal(lb.swipeDirection(-20, 0, 10), 1);            // しきい値を変えられる
});

test("tabTarget: Tab はダイアログの中を回る（先頭と末尾でつながる）", () => {
  assert.equal(lb.tabTarget(0, 4, false), 1);
  assert.equal(lb.tabTarget(3, 4, false), 0);               // 末尾 → 先頭
  assert.equal(lb.tabTarget(0, 4, true), 3);                // 先頭で Shift+Tab → 末尾
  assert.equal(lb.tabTarget(2, 4, true), 1);
  assert.equal(lb.tabTarget(-1, 4, false), 0);              // いまの要素が不明（ダイアログの外）→ 先頭 / 末尾
  assert.equal(lb.tabTarget(-1, 4, true), 3);
  assert.equal(lb.tabTarget(0, 0, false), -1);              // 操作できる要素が無い
  assert.equal(lb.tabTarget(0, 1, false), 0);               // 1 つだけ
});

const BASE = "https://vrc-album.fara-labs.com/posts/a";
const R2 = ["https://vrc-photos.fara-labs.com"];

test("isAllowedImageUrl: 同一オリジンと、許可したオリジン（imageBaseUrl のホスト）の http(s) だけ", () => {
  assert.equal(lb.isAllowedImageUrl("images/dev-photos/a_2048.webp", BASE, []), true);                       // 相対 = 同一オリジン
  assert.equal(lb.isAllowedImageUrl("https://vrc-album.fara-labs.com/x.webp", BASE, []), true);
  assert.equal(lb.isAllowedImageUrl("https://vrc-photos.fara-labs.com/2026-10/a_2048.webp", BASE, R2), true);
  assert.equal(lb.isAllowedImageUrl("https://vrc-photos.fara-labs.com/2026-10/a_2048.webp", BASE, []), false);   // 許可前
});

test("isAllowedImageUrl: 別のホスト・別のポート・別のスキーム・危険なスキーム・形式不正は使わない", () => {
  for (const bad of [
    "https://evil.example/a.webp", "https://vrc-photos.fara-labs.com.evil.example/a.webp", "https://vrc-photos.fara-labs.com:8443/a.webp",
    "http://vrc-photos.fara-labs.com/a.webp", "javascript:alert(1)", "data:image/svg+xml;base64,AAAA", "blob:https://vrc-album.fara-labs.com/x", "//evil.example/a.webp", "", null, undefined, 5,
  ]) assert.equal(lb.isAllowedImageUrl(bad, BASE, R2), false, String(bad));
  assert.equal(lb.isAllowedImageUrl("a.webp", "not a url", R2), false);
});
