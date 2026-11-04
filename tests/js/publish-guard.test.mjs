// 公開の前の確認（tools/publish-guard.mjs）。  node --test tests/js/publish-guard.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { checkPublishBranch, checkWorkTree } from "../../tools/publish-guard.mjs";

const SHA = "a".repeat(40);
const OTHER = "b".repeat(40);

test("本番は master から、確認用は develop からだけ公開できる", () => {
  assert.doesNotThrow(() => checkPublishBranch("production", "master"));
  assert.doesNotThrow(() => checkPublishBranch("develop", "develop"));
});

test("ほかのブランチからは、本番も確認用も公開しない（理由つき）", () => {
  assert.throws(() => checkPublishBranch("production", "develop"), /ブランチ master から行います（いまは「develop」）/);
  assert.throws(() => checkPublishBranch("develop", "master"), /ブランチ develop から行います（いまは「master」）/);
  assert.throws(() => checkPublishBranch("production", "fix/review-x"), /fix\/review-x/);
});

test("detached HEAD や Git が使えないとき（ブランチ名が空）は公開しない", () => {
  assert.throws(() => checkPublishBranch("production", ""), /不明/);
});

test("公開先が本番・確認用以外なら止める", () => {
  assert.throws(() => checkPublishBranch("staging", "master"), /公開先が不正です/);
});

test("本番: 作業ツリーがクリーンで、HEAD が origin/master と同じなら通す", () => {
  assert.doesNotThrow(() => checkWorkTree("production", { porcelain: "", head: SHA, remoteHead: SHA }));
});

test("本番: 未コミットの変更（追跡・未追跡どちらも）があれば止める", () => {
  assert.throws(() => checkWorkTree("production", { porcelain: " M src/x.cs\n", head: SHA, remoteHead: SHA }), /コミットしていない変更/);
  assert.throws(() => checkWorkTree("production", { porcelain: "?? notes.txt\n", head: SHA, remoteHead: SHA }), /コミットしていない変更/);
});

test("git status が読めなかった（null）ときは、「コミットしていない変更」ではなく、読めなかったという理由で止める", () => {
  assert.throws(() => checkWorkTree("production", { porcelain: null, head: SHA, remoteHead: SHA }), /git の状態を読めませんでした/);
  assert.throws(() => checkWorkTree("develop", { porcelain: null, head: SHA, remoteHead: SHA }), /git の状態を読めませんでした/);
  assert.throws(() => checkWorkTree("develop", { porcelain: null }), (e) => !/コミットしていない変更/.test(e.message));
});

test("本番: HEAD が origin/master と違う（master に無いコミット）なら止める", () => {
  assert.throws(() => checkWorkTree("production", { porcelain: "", head: OTHER, remoteHead: SHA }), /origin\/master と一致しません/);
});

test("本番: origin/master が無い・HEAD が読めないときは止める", () => {
  assert.throws(() => checkWorkTree("production", { porcelain: "", head: SHA, remoteHead: "" }), /origin\/master が見つかりません/);
  assert.throws(() => checkWorkTree("production", { porcelain: "", head: "", remoteHead: SHA }), /HEAD のコミットが読めません/);
});

test("確認用: 作業ツリーがクリーンなら、未 push のコミットがあっても通す", () => {
  assert.doesNotThrow(() => checkWorkTree("develop", { porcelain: "", head: OTHER, remoteHead: "" }));
});

test("確認用: 未コミットの変更があれば止める", () => {
  assert.throws(() => checkWorkTree("develop", { porcelain: " M x\n", head: SHA, remoteHead: SHA }), /コミットしていない変更/);
});

test("checkWorkTree も、公開先が不正なら止める", () => {
  assert.throws(() => checkWorkTree("staging", { porcelain: "" }), /公開先が不正です/);
});
