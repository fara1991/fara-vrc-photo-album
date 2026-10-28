// 公開の前の確認（純粋な関数。テストできるように、Git の呼び出しは tools/site.mjs に任せる）。
// 本番（production）は master から、確認用（develop）は develop から公開する。ほかのブランチからは公開しない（取り違えを防ぐ）。

/** 公開先ごとの、公開してよいブランチ。 */
export const PUBLISH_BRANCH = { production: "master", develop: "develop" };

/**
 * env（公開先）と、いまのブランチ名を比べる。合わなければ例外（理由つき）。
 * branch が空（detached HEAD・Git が使えない）も合わないものとして扱う。
 */
export function checkPublishBranch(env, branch) {
  const expected = PUBLISH_BRANCH[env];
  if (!expected) throw new Error(`公開先が不正です: ${env}`);
  if (branch !== expected) {
    throw new Error(`${env} の公開は、ブランチ ${expected} から行います（いまは「${branch || "不明"}」）。ブランチを切り替えてからやり直してください`);
  }
}
