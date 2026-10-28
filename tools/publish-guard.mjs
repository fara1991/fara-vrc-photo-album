// 公開の前の確認（純粋な関数。テストできるように、Git の呼び出しは tools/site.mjs に任せる）。
// 本番（production）は master から、確認用（develop）は develop から公開する。ほかのブランチからは公開しない（取り違えを防ぐ）。
// さらに、作業ツリーに未コミットの変更があれば公開しない（ローカルの変更が本番に出るのを防ぐ）。本番は、HEAD が origin/master と同じコミットのときだけ公開する。

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

/**
 * 作業ツリーと、コミットの状態を確かめる。合わなければ例外（理由つき）。
 * - porcelain: `git status --porcelain` の出力（無視したファイル（content/・local/・publish/ など）は含まれない）。空でなければ未コミットの変更がある
 * - head: `git rev-parse HEAD`。remoteHead: `git rev-parse --verify origin/master`（無ければ空）
 * 確認用（develop）は、作業ツリーがクリーンであることだけを確かめる（未 push のコミットは許す）。
 * 本番（production）は、作業ツリーがクリーンで、HEAD が origin/master と同じコミットのときだけ許す。
 */
export function checkWorkTree(env, { porcelain = "", head = "", remoteHead = "" }) {
  if (!PUBLISH_BRANCH[env]) throw new Error(`公開先が不正です: ${env}`);
  if (String(porcelain).trim() !== "") {
    throw new Error("作業ツリーに、コミットしていない変更（または追跡されていないファイル）があります。公開する内容を確かめてから、コミットするか退避してからやり直してください（git status で確認）");
  }
  if (env !== "production") return;
  if (!head) throw new Error("HEAD のコミットが読めません（Git が使えない）。本番は公開できません");
  if (!remoteHead) throw new Error("origin/master が見つかりません（git fetch origin master で取得してください）。本番は公開できません");
  if (head !== remoteHead) {
    throw new Error(`HEAD が origin/master と一致しません（HEAD: ${head.slice(0, 8)}・origin/master: ${remoteHead.slice(0, 8)}）。master に無いコミットは本番に公開しません。master にマージしてから（git pull で揃えてから）公開してください`);
  }
}
