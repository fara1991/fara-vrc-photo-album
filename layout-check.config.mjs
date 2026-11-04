// レイアウトの自動計測の設定（kit の docs/layout-check.md）。
// 実行は `npm run layout-check`（tools/layout-check.mjs）。サンプルの記事（samples/content）で作った静的サイト（site-production）を配って測る。
// baseURL は、実行のとき空いているポート（8850〜）で配る先に差し替える。CLI で直接使うときは、
//   node external/fara-blazor-component/tools/layout-check/serve-static.mjs site-production 8850
// で配ってから、kit の cli.mjs を --config layout-check.config.mjs で動かす。
//
// 幅は既定（360・390・768・1024・1440）。規則は既定（no-horizontal-scroll・single-h1・focus-visible・no-offscreen-focus・tap-target・min-font-size・menu-esc・menu-close-visible）。
// 除外（exclusions）は、規則・セレクター・理由を必ず書く。理由の無い除外は設定エラー。
export default {
  baseURL: "http://127.0.0.1:8850",
  pages: [
    { path: "/", name: "トップ" },
    { path: "/posts/sample-2026-01-01", name: "記事（サンプル）" },
    { path: "/archive/2026/01", name: "一覧（アーカイブ 2026 年 1 月）" },
    { path: "/worlds", name: "ワールド一覧" },
    { path: "/tags", name: "タグ一覧" },
    { path: "/outfits", name: "服一覧" },
    { path: "/report", name: "問い合わせ" },
    { path: "/privacy", name: "プライバシーポリシー" },
    // 404 は、配信のサーバーでは存在しない URL が 404.html の代わりに素の文字列になるため、404.html を直接開く（200 で返る）
    { path: "/404.html", name: "404（404.html）" },
  ],
  // Blazor が描く MudLayout。静的 HTML には無いので、起動後に待つ
  readySelector: ".mud-layout",
  // closeSelector: 開いたドロワーの閉じるボタン（kit 0.7.0 の SiteLayout が出す）。ドロワーがメニューボタンを覆うので、閉じる手段はこちらで測る（menu-close-visible）
  menuEsc: { buttonSelector: ".fbc-menu-toggle", closeSelector: ".fbc-drawer-close" },
  exclusions: [
    {
      rule: "tap-target",
      selector: ".fbc-report-submit",
      page: "/report",
      reason: "送信ボタンは、確認（Turnstile）が済むまで無効（MudBlazor の disabled は pointer-events: none）。無効のボタンは押せないので、計測では当たらない。有効になったときの大きさは kit の CSS（600px 未満で 44px）で決まる",
    },
    {
      rule: "tap-target",
      selector: "a.album-card-link",
      page: "/worlds",
      reason: "カードの題のリンクの当たり判定の下側が、同じカードのタグ（別のリンク。負の margin で上に伸びる当たり判定）と重なる。配置を変えると見た目が変わるので、この PR では除外し、別に検討する",
    },
  ],
};
