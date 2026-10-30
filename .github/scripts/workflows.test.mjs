// .github/workflows の決まりのテスト。  node --test .github/scripts/workflows.test.mjs
// このリポジトリは公開（パブリック）にする予定で、サイトの公開は GitHub Actions では行わない（手元のデスクトップアプリ／tools/site.mjs から行う）。
// 本物の記事（content/）は Git に入れていないので、Actions で公開すると空のサイトで本番を上書きしてしまう。公開するワークフローを足さないための関門。
import { test } from "node:test";
import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";

const dir = new URL("../workflows/", import.meta.url);
const files = readdirSync(dir).filter((f) => f.endsWith(".yml") || f.endsWith(".yaml"));
const read = (f) => readFileSync(new URL(f, dir), "utf8").replace(/\r\n/g, "\n");

test("ワークフローは build.yml だけ（公開・データ更新のワークフローは置かない）", () => {
  assert.deepEqual(files, ["build.yml"]);
});

test("build.yml は公開しない: wrangler deploy（--dry-run 以外）も Cloudflare のシークレットも使わない", () => {
  const text = read("build.yml");
  for (const line of text.split("\n")) {
    if (/wrangler\s+deploy/.test(line)) assert.match(line, /--dry-run/, `公開してしまう行: ${line.trim()}`);
  }
  assert.doesNotMatch(text, /secrets\./, "シークレットは使わない");
  assert.doesNotMatch(text, /CLOUDFLARE_API_TOKEN/);
  assert.doesNotMatch(text, /environment:/, "Environment（公開用の保護つき）は使わない");
  assert.match(text, /permissions:\n\s+contents: read/);
});

test("build.yml のサイトのビルドは、CI 用のサンプル（samples/content）から作り、サンプルを許す環境変数を付ける", () => {
  const text = read("build.yml");
  const builds = text.split("\n").filter((l) => l.includes("src/FaraVrcAlbum.Build"));
  assert.equal(builds.length, 2);
  for (const l of builds) assert.match(l, /--content samples\/content/);
  assert.match(text, /ALBUM_ALLOW_SAMPLE: "1"/);
});
