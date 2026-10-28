// 公開の前のブランチの確認（tools/publish-guard.mjs）。  node --test tests/js/publish-guard.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { checkPublishBranch } from "../../tools/publish-guard.mjs";

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
