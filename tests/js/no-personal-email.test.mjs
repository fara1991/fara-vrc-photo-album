// Git に入るファイルに、個人のメールアドレスの形の文字列を入れないための検査。  node --test tests/js/no-personal-email.test.mjs
// 対象は git ls-files（Git に入っているファイル）。content/・local/・.env.local は Git に入れない決まりなので、見ない。
// 連絡先は公開の URL（https://github.com/fara1991/fara-vrc-photo-album）にする。違反が出たら、メールを消して URL に直す。
import { test } from "node:test";
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import { existsSync, readFileSync, statSync } from "node:fs";
import { join } from "node:path";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../../", import.meta.url));
const EMAIL = /[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/g;

// 例外は最小限にする。
// - 予約ドメイン（RFC 2606）の example.com・example.org・example.net・example.test: テストの架空の宛先。実在の個人を指さない。
// - GitHub の noreply・Claude の noreply: コミットの作者・共著者の行に出る既定のアドレス（個人のメールではない）。
const ALLOWED = [
  /@(?:[\w-]+\.)*example\.(?:com|org|net|test)$/,
  /@users\.noreply\.github\.com$/,
  /^noreply@anthropic\.com$/,
];

const mask = (address) => address.replace(/^(.{0,2})[^@]*(@.*)$/, "$1***$2");

/** テキストの中の、例外でないメールアドレスの形の文字列（行番号と、伏せた形）。 */
function findAddresses(text) {
  const hits = [];
  text.split("\n").forEach((line, i) => {
    for (const m of line.matchAll(EMAIL)) {
      if (!ALLOWED.some((allowed) => allowed.test(m[0]))) hits.push({ line: i + 1, masked: mask(m[0]) });
    }
  });
  return hits;
}

test("検査: 例外の予約ドメイン・noreply は許し、それ以外のアドレスは見つける", () => {
  // 実在しないよう、アドレスの一部を分けて書く（この検査自身にも掛かるため）
  const personal = ["ta", "ro@", "gmail.com"].join("");
  const sample = `a@example.test 69506848+fara1991@users.noreply.github.com noreply@anthropic.com ${personal}`;
  assert.deepEqual(findAddresses(sample).map((h) => h.masked), ["ta***@gmail.com"]);
});

test("Git に入っているファイルに、個人のメールアドレスの形の文字列がない", () => {
  const tracked = execFileSync("git", ["ls-files", "-z"], { cwd: root, encoding: "utf8" }).split("\0").filter(Boolean);
  const found = [];
  for (const rel of tracked) {
    const abs = join(root, rel);
    // サブモジュールの空のフォルダなどは飛ばす
    if (!existsSync(abs) || !statSync(abs).isFile()) continue;
    const bytes = readFileSync(abs);
    if (bytes.includes(0)) continue; // バイナリは見ない
    for (const hit of findAddresses(bytes.toString("utf8"))) found.push(`${rel}:${hit.line} ${hit.masked}`);
  }
  assert.deepEqual(found, [], "メールアドレスの形の文字列が入っている（伏せて表示）。連絡先は公開の URL にする");
});
