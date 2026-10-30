// dotnet publish の ILLink（サイズを小さくする処理）は、環境によってたまに落ちる（「Internal CLR error」・終了コード -1073741819 など）。
// コードの問題ではなく、再実行すると通るので、ILLink が原因の失敗に限って、tools/site.mjs が自動でやり直す。
// ここは、MSBuild のログの文から「ILLink が落ちたか」を決める部分だけ（tests/js/publish-retry.test.mjs が確かめる）。

/** ILLink が落ちたときに出る文（NETSDK1144: サイズの最適化に失敗・IL1012・Internal CLR error・ILLink.targets のエラー）。 */
const ILLINK_CRASH = /NETSDK1144|\bIL1012\b|Internal CLR error|ILLink\.targets\(\d+,\d+\): error/;

/** やり直す最大の回数（1 回目を含む）。 */
export const PUBLISH_ATTEMPTS = 3;

/** ログの文が、ILLink が落ちた失敗か（ふつうのコンパイルエラーなどは false。やり直さずに、すぐ止める）。 */
export function isIllinkCrash(logText) {
  return ILLINK_CRASH.test(logText ?? "");
}
