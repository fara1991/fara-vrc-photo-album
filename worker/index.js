// Fara Album の Worker。Blazor WebAssembly の静的サイト（dist。wrangler.jsonc の assets）を配信する。HTML・API はここを通る（wrangler.jsonc の run_worker_first の配列。_framework などの静的な部品は通さない）。
//   GET /api/report/config・POST /api/report → 作者への報告（external/fara-blazor-component/worker/report.js。設定は docs/cloudflare-setup.md）
//   GET /api/maintenance → いまの状態（アプリが起動時に読み、MAINTENANCE_MODE=notice のときは「更新予定」の帯を、kit の MaintenanceBanner で出す）
//   MAINTENANCE_MODE=true   … すべて 503。ページはメンテナンス画面、/api/ は JSON（external/fara-blazor-component/worker/maintenance.js）
// メンテナンスの切り替えは、Worker の変数 MAINTENANCE_MODE / MAINTENANCE_MESSAGE（docs/cloudflare-setup.md）。
import { getMaintenance, maintenanceResponse } from "../external/fara-blazor-component/worker/maintenance.js";
import { handleReport } from "../external/fara-blazor-component/worker/report.js";
import { errorSummary } from "../external/fara-blazor-component/worker/error-summary.js";

// 報告の 1 日の送信回数の制限（送信元ごと・3 回まで）の Durable Object。wrangler.jsonc の REPORT_LIMITER が指す（docs/usage.md の 2.2）
export { ReportRateLimiter } from "../external/fara-blazor-component/worker/report-rate-limit.js";

// メンテナンス画面は kit が作る（色は紫系の直書き）。このサイトの色（ローズ寄り。app.css の「テーマ」ブロックの --fbc-maintenance-*）にそろえる。kit は変更しない
const MAINTENANCE_COLORS = [["#0c0616", "#130a0e"], ["#f3edff", "#fbeff3"], ["#b39ddb", "#f4a9c4"], ["#b4a7d1", "#c9abb8"], ["#7c3aed", "#b83a64"]];

async function themedMaintenance(pending) {
  const response = await pending;
  if (!(response.headers.get("Content-Type") ?? "").includes("text/html")) return response;
  let html = await response.text();
  for (const [from, to] of MAINTENANCE_COLORS) html = html.split(from).join(to);
  return new Response(html, { status: response.status, headers: response.headers });
}

export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    const maintenance = getMaintenance(env);
    // メンテナンスの状態（アプリが起動時に読む。kit の MaintenanceInfo）
    if (url.pathname === "/api/maintenance") {
      return new Response(JSON.stringify({ state: maintenance.state, message: maintenance.message }), {
        headers: { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" },
      });
    }
    if (maintenance.state === "on") return themedMaintenance(maintenanceResponse(request, maintenance, "Fara Album"));

    // 作者への報告の受け口（external/fara-blazor-component/worker/report.js。ポケモン・アバター・Bot と同じファイル）
    if (url.pathname === "/api/report" || url.pathname === "/api/report/config") {
      try {
        return await handleReport(request, env, "Fara Album");
      } catch (error) {
        // 例外の中身はログに残す（URL・トークンは伏せる）。応答には詳細を返さない（シークレットを含む可能性がある）
        console.error(`報告の受け口で例外: ${errorSummary(error)}`);
        return new Response(JSON.stringify({ error: "internal_error", message: "サーバーでエラーが発生しました" }), {
          status: 500,
          headers: { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" },
        });
      }
    }
    if (url.pathname.startsWith("/api/")) return new Response("not found", { status: 404 });

    // ページ・画像などは静的アセット。お知らせ（notice）の帯は、アプリが /api/maintenance を読んで出す（HTML には入れない。Blazor が #app の中身を描き直すため）
    return env.ASSETS.fetch(request);
  },
};
