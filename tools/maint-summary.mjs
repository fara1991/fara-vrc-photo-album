// （以前の deploy.yml・deploy-develop.yml の Summary (maintenance) ステップ用。今は Actions では公開しない）反映後の MAINTENANCE_MODE / MAINTENANCE_MESSAGE の現在値を Cloudflare の API から読み、ジョブサマリー用の 1 行を標準出力に出す。
// シェルの単一引用符の中に node -e を埋め込むと、中の ' でシェルの構文エラーになる（avatar でも起きた）ので、別ファイルにして環境変数で値を受ける。
//   必要な環境変数: CLOUDFLARE_ACCOUNT_ID, CLOUDFLARE_API_TOKEN, WORKER_NAME
// 値が plain_text のときだけ中身を出す（シークレットは種類だけ）。トークンは出さない。失敗しても終了コード 0（サマリーの補足なので、公開の結果を変えない）。

const BACKTICK = String.fromCharCode(96);
const APOSTROPHE = String.fromCharCode(39);

/** サマリーの Markdown のコードスパンを壊さないよう、値の中のバッククォートを置き換える。 */
export function describe(bindings, name) {
  const x = (bindings ?? []).find((y) => y.name === name);
  if (!x) return "(未設定)";
  if (x.type !== "plain_text") return `(${x.type})`;
  return JSON.stringify(x.text).replaceAll(BACKTICK, APOSTROPHE);
}

export function summaryLine(bindings) {
  return `- 反映後の現在値: MAINTENANCE_MODE=${BACKTICK}${describe(bindings, "MAINTENANCE_MODE")}${BACKTICK} / MAINTENANCE_MESSAGE=${BACKTICK}${describe(bindings, "MAINTENANCE_MESSAGE")}${BACKTICK}`;
}

export async function run(env, fetchImpl = fetch, log = console.log) {
  try {
    const url = `https://api.cloudflare.com/client/v4/accounts/${env.CLOUDFLARE_ACCOUNT_ID}/workers/scripts/${env.WORKER_NAME}/settings`;
    const res = await fetchImpl(url, { headers: { Authorization: `Bearer ${env.CLOUDFLARE_API_TOKEN}` }, signal: AbortSignal.timeout(15000) });
    const body = await res.json();
    log(summaryLine(body?.result?.bindings ?? []));
  } catch {
    log("- 反映後の現在値: 取得できませんでした");
  }
}

if (process.argv[1]?.endsWith("maint-summary.mjs")) await run(process.env);
