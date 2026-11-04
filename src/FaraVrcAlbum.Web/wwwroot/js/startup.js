// 起動できなかったときの表示（Program.cs の StartupError.ShowAsync から呼ぶ）。index.html の #blazor-error-ui を出す。
// 出すと、kit の boot-indicator.js が読み込み表示を外し、静的 HTML の本文が操作できるようになる。
(function () {
    "use strict";
    window.faraAlbumStartup = {
        showError: function () {
            var ui = document.getElementById("blazor-error-ui");
            if (ui) ui.style.display = "block";
        },
    };
})();
