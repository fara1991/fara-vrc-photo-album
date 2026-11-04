// dotnet publish を自動でやり直すかどうかの判定（ILLink が落ちたときだけ）。  node --test tests/js/publish-retry.test.mjs
import { test } from "node:test";
import assert from "node:assert/strict";
import { PUBLISH_ATTEMPTS, isIllinkCrash } from "../../tools/publish-retry.mjs";

const CRASH_LOG = `
  Fatal error.
  Internal CLR error. (0x80131506)
C:\\Users\\Fara\\.nuget\\packages\\microsoft.net.illink.tasks\\10.0.14\\build\\Microsoft.NET.ILLink.targets(150,5): error MSB6006: "dotnet.exe" はコード -1073741819 を伴って終了しました。 [E:\\Projects\\fara\\fara-vrc-photo-album\\src\\FaraVrcAlbum.Web\\FaraVrcAlbum.Web.csproj]
C:\\Users\\Fara\\.nuget\\packages\\microsoft.net.illink.tasks\\10.0.14\\build\\Microsoft.NET.ILLink.targets(103,5): error NETSDK1144: サイズに合わせてアセンブリを最適化できませんでした。
`;

test("ILLink が落ちたログ（Internal CLR error・NETSDK1144・ILLink.targets のエラー）は、やり直す", () => {
  assert.equal(isIllinkCrash(CRASH_LOG), true);
  assert.equal(isIllinkCrash("error NETSDK1144: サイズに合わせてアセンブリを最適化できませんでした。"), true);
  assert.equal(isIllinkCrash("ILLink : error IL1012: Failed to resolve assembly"), true);
  assert.equal(isIllinkCrash("Internal CLR error. (0x80131506)"), true);
});

test("ふつうのコンパイルエラー・空のログは、やり直さない（すぐ止める）", () => {
  assert.equal(isIllinkCrash("E:\\src\\Foo.cs(12,5): error CS1002: ; が必要です"), false);
  assert.equal(isIllinkCrash("error MSB1009: プロジェクト ファイルがありません。"), false);
  assert.equal(isIllinkCrash(""), false);
  assert.equal(isIllinkCrash(undefined), false);
});

test("やり直しは、1 回目を含めて 3 回まで", () => {
  assert.equal(PUBLISH_ATTEMPTS, 3);
});
