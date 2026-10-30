// ESLint（flat config）。共通の中身は kit の tools/lint/eslint.config.shared.mjs（external/fara-blazor-component）。
// 既定の `shared` は使わない: Node のグローバルを **/*.{js,mjs,cjs} に当ててしまい、画面の JS（wwwroot/js）の no-undef が効かなくなるため。
// 対象ごとに環境（node / browser）を分けて当てる。
import { createSharedConfig, sharedIgnores } from "./external/fara-blazor-component/tools/lint/eslint.config.shared.mjs";

export default [
  {
    // 共通の無視（node_modules・bin・obj・publish・artifacts・.wrangler・external/**）に加えて、このサイトの生成物。
    // bin.unix・obj.unix は Windows 以外（ubuntu の CI）の出力先（Directory.Build.props）。site-*・dist・verify-out は公開物と検査の出力。
    ignores: [
      ...sharedIgnores.ignores,
      "**/bin.unix/**",
      "**/obj.unix/**",
      "site-*/**",
      "dist/**",
      "verify-out/**",
    ],
  },
  // Node の JS（ビルド・Worker・公開の関門・テスト・検査のツール）
  ...createSharedConfig({
    files: ["tools/**/*.{js,mjs,cjs}", "worker/**/*.{js,mjs,cjs}", ".github/scripts/**/*.{js,mjs,cjs}", "tests/js/**/*.{js,mjs,cjs}", "eslint.config.mjs"],
    environments: ["node"],
  }),
  // 動作確認（tools/verify）の page.evaluate に渡す関数は、ブラウザで動く（window・document を使う）。Node のファイルの中にあるので、その 2 つだけに両方の環境を当てる
  ...createSharedConfig({
    files: ["tools/verify/lib.mjs", "tools/verify/report-form.mjs"],
    environments: ["node", "browser"],
  }),
  // 画面の JS（index.html の <script> で読む古い形の script。import も module もしない）
  ...createSharedConfig({
    files: ["src/FaraVrcAlbum.Web/wwwroot/js/**/*.js"],
    environments: ["browser"],
    sourceType: "script",
  }),
];
