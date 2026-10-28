// テーマが保存されていない（初めて・保存が使えない）ときの既定を、写真向きのダークにする。
// kit の theme-init.js は、保存を読めないときは light にするので、その直後（描画の前）に読んで、ダークに直す。
// 保存された値があれば（ユーザーが選んだ light / dark）、何もしない。起動の後の C# 側（StartupError.ApplyDefaultThemeAsync）も同じ規則。
// KEY は SiteInfo.StorageKey と index.html の theme-init.js の data-storage-key と同じ値（tests/js/theme-default.test.mjs が一致を確かめる）。
(function () {
    "use strict";
    var KEY = "fara-vrc-album.settings";
    var saved = null;
    try { saved = localStorage.getItem(KEY); } catch (e) { saved = null; }
    if (!saved) document.documentElement.setAttribute("data-bs-theme", "dark");
})();
