// 写真のライトボックス（記事ページのギャラリー用。外部ライブラリなし。CSP の script-src 'self' のまま動く）。
//
// 静的 HTML の写真は <a class="album-photo-link" href="2048px の画像"><img></a>。JS が無い・起動前でも、リンクでフルサイズの画像へ行ける。
// このファイルは、クリック・Enter・Space でネイティブの <dialog>（showModal）を開く。dialog は JS が <body> の直下に作る（#app の外なので、Blazor の再描画で消えない）。
// イベントは document で受ける（静的 HTML から Blazor の描画に入れ替わっても、そのまま効く）。キャプション（ワールド・撮影日時）は、開く・前後へ移るときに
// ギャラリーの figcaption から写す（Blazor の描画で #app が入れ替わるので、開いている間も #app の変化を見て写し直す）。
//
// 操作: クリック / Enter / Space で開く・Esc / 閉じるボタン / 背景のクリックで閉じる・← → / 左右のボタン / スワイプで前後（循環）・Home / End・Tab はダイアログの中を回る（フォーカストラップ）。
//   閉じると、開いた写真（前後へ動いたあとはその写真）のリンクへフォーカスを戻す。URL の #photo-N（1 始まり）を、開くたびに更新し（履歴は増やさない = replaceState）、直接開ける。
//   背景のスクロールは止める（html に album-lb-open）。前後の画像（2048px）を先読みする。読み込み中は回転する表示。prefers-reduced-motion では回転・フェードを止める（app.css）。
// 純粋な関数（循環・ハッシュ・スワイプ・フォーカスの回り方）は window.faraAlbumLightbox に出している（tests/js/lightbox.test.mjs）。
(function () {
    "use strict";

    // ---- 純粋な関数 ----

    /** index を 0..count-1 に循環させる（count が 0 以下は 0）。 */
    function wrapIndex(index, count) {
        return count <= 0 ? 0 : ((index % count) + count) % count;
    }

    /** "#photo-3" → 2（0 始まり）。形式が違う・範囲外は -1。 */
    function parsePhotoHash(hash, count) {
        var match = /^#photo-(\d{1,4})$/.exec(hash || "");
        if (!match) return -1;
        var n = parseInt(match[1], 10);
        return n >= 1 && n <= count ? n - 1 : -1;
    }

    /** 0 始まりの index → "#photo-3"。 */
    function hashFor(index) {
        return "#photo-" + (index + 1);
    }

    /** スワイプの向き: 右へ払う = -1（前）、左へ払う = 1（次）、それ以外（短い・縦の動き）は 0。 */
    function swipeDirection(dx, dy, threshold) {
        var limit = threshold || 50;
        if (Math.abs(dx) < limit || Math.abs(dx) < Math.abs(dy) * 1.5) return 0;
        return dx < 0 ? 1 : -1;
    }

    /** Tab で次にフォーカスする位置（ダイアログの中を回す）。active は今の位置（-1 = ダイアログの外・不明）。count は操作できる要素の数。 */
    function tabTarget(active, count, shift) {
        if (count <= 0) return -1;
        if (active < 0) return shift ? count - 1 : 0;
        return shift ? (active === 0 ? count - 1 : active - 1) : (active === count - 1 ? 0 : active + 1);
    }

    /**
     * ライトボックスの画像に使ってよい URL か。http(s) で、同一オリジン（base のオリジン）か、許可したオリジン（data/site.json の imageBaseUrl のホスト）だけ。
     * それ以外（javascript:・data:・別のホスト・形式不正）は使わない（img.src と「画像だけを開く」の href に入れない）。
     */
    function isAllowedImageUrl(href, base, origins) {
        if (typeof href !== "string" || href === "") return false;
        var url;
        try { url = new URL(href, base); } catch { return false; }
        if (url.protocol !== "https:" && url.protocol !== "http:") return false;
        var baseUrl;
        try { baseUrl = new URL(base); } catch { return false; }
        return url.origin === baseUrl.origin || (origins || []).indexOf(url.origin) >= 0;
    }

    var helpers = { isAllowedImageUrl: isAllowedImageUrl, wrapIndex: wrapIndex, parsePhotoHash: parsePhotoHash, hashFor: hashFor, swipeDirection: swipeDirection, tabTarget: tabTarget };
    if (typeof window !== "undefined") window.faraAlbumLightbox = helpers;
    if (typeof document === "undefined") return;

    // ---- DOM ----

    // 画像に使ってよいオリジン: 同一オリジンと、data/site.json の imageBaseUrl のホスト（読めるまでは同一オリジンだけ。別オリジンの写真は、そのあいだ普通のリンクとして開く）
    var allowedOrigins = [];

    function loadAllowedOrigins() {
        if (typeof fetch !== "function") return;
        fetch("data/site.json", { credentials: "same-origin" })
            .then(function (res) { return res.ok ? res.json() : null; })
            .then(function (site) {
                if (!site || typeof site.imageBaseUrl !== "string") return;
                try { allowedOrigins = [new URL(site.imageBaseUrl, document.baseURI).origin]; } catch { /* 形式不正は許可しない */ }
                if (location.hash && !(dialog && dialog.open) && openFromHash()) guardFocusUntilBooted();      // 読み込みより先にハッシュで開こうとして見送った分
            })
            .catch(function () { /* 読めなければ同一オリジンだけ */ });
    }

    function allowed(link) {
        return isAllowedImageUrl(link.getAttribute("href"), document.baseURI, allowedOrigins);
    }

    var GALLERY_LINKS = ".album-gallery a.album-photo-link";
    var FOCUSABLE = "button:not([hidden]):not([disabled]), a[href]";

    var dialog = null;
    var parts = {};
    var index = 0;
    var token = 0;
    var observer = null;
    var lastCaption = "";
    var touch = null;
    var preloaded = [];
    var frame = 0;
    var emptyTimer = 0;
    var leaving = false;           // ページ内のリンクを押して閉じるとき（閉じたあとにフォーカスを戻さない）

    function links() {
        return Array.prototype.slice.call(document.querySelectorAll(GALLERY_LINKS));
    }

    function supported() {
        return typeof HTMLDialogElement === "function" && typeof HTMLDialogElement.prototype.showModal === "function";
    }

    function element(tag, className, text) {
        var el = document.createElement(tag);
        if (className) el.className = className;
        if (text) el.textContent = text;
        return el;
    }

    function button(className, label, glyph) {
        var el = element("button", "album-lb-button " + className, glyph);
        el.type = "button";
        el.setAttribute("aria-label", label);
        el.title = label;
        return el;
    }

    function build() {
        dialog = element("dialog", "album-lb");
        dialog.setAttribute("role", "dialog");
        dialog.setAttribute("aria-modal", "true");
        dialog.setAttribute("aria-label", "写真ビューアー");

        var bar = element("div", "album-lb-bar");
        parts.count = element("span", "album-lb-count");
        parts.count.setAttribute("aria-live", "polite");
        parts.close = button("album-lb-close", "閉じる", "×");
        bar.appendChild(parts.count);
        bar.appendChild(parts.close);

        var stage = element("div", "album-lb-stage");
        parts.stage = stage;
        parts.prev = button("album-lb-prev", "前の写真", "‹");
        parts.next = button("album-lb-next", "次の写真", "›");
        parts.figure = element("div", "album-lb-figure");
        parts.image = element("img", "album-lb-image");
        parts.image.decoding = "async";
        parts.spinner = element("span", "album-lb-spinner");
        parts.spinner.setAttribute("role", "status");
        parts.spinner.setAttribute("aria-label", "読み込み中");
        parts.figure.appendChild(parts.image);
        parts.figure.appendChild(parts.spinner);
        stage.appendChild(parts.prev);
        stage.appendChild(parts.figure);
        stage.appendChild(parts.next);

        parts.caption = element("div", "album-lb-caption");

        dialog.appendChild(bar);
        dialog.appendChild(stage);
        dialog.appendChild(parts.caption);
        document.body.appendChild(dialog);

        parts.close.addEventListener("click", function () { dialog.close(); });
        parts.prev.addEventListener("click", function () { go(-1); });
        parts.next.addEventListener("click", function () { go(1); });
        dialog.addEventListener("close", onClosed);
        // キーはダイアログではなく document で受ける（キャプションのリンクにフォーカスがあるまま写真を移すと、リンクが作り直されてフォーカスが body に落ちる。そのときも矢印キーが効くように）
        document.addEventListener("keydown", onKeyDown);
        dialog.addEventListener("click", onDialogClick);
        stage.addEventListener("touchstart", onTouchStart, { passive: true });
        stage.addEventListener("touchend", onTouchEnd, { passive: true });
        stage.addEventListener("touchcancel", function () { touch = null; }, { passive: true });
    }

    function open(i) {
        var list = links();
        if (list.length === 0 || !supported()) return false;
        if (!list[wrapIndex(i, list.length)] || !allowed(list[wrapIndex(i, list.length)])) return false;
        if (!dialog) build();
        if (!dialog.open) {
            document.documentElement.classList.add("album-lb-open");
            dialog.showModal();
            watch();
        }
        show(i);
        parts.close.focus();
        return true;
    }

    function show(i) {
        var list = links();
        if (list.length === 0) return;
        index = wrapIndex(i, list.length);
        var link = list[index];
        if (!allowed(link)) return;                                   // 許可していない URL の写真は表示しない（前後への移動でも飛ばさず、その場にとどまる）
        var thumb = link.querySelector("img");
        var src = link.getAttribute("href");
        var mine = ++token;

        parts.count.textContent = (index + 1) + " / " + list.length;
        var several = list.length > 1;
        parts.prev.hidden = !several;
        parts.next.hidden = !several;

        parts.image.alt = thumb ? thumb.alt : "";
        parts.image.style.backgroundColor = thumb ? thumb.style.backgroundColor : "";
        parts.figure.setAttribute("data-loading", "");
        parts.figure.setAttribute("aria-busy", "true");
        parts.image.onload = parts.image.onerror = function () {
            if (mine !== token) return;
            parts.figure.removeAttribute("data-loading");
            parts.figure.removeAttribute("aria-busy");
        };
        parts.image.src = src;
        if (parts.image.complete && parts.image.naturalWidth > 0) {
            parts.figure.removeAttribute("data-loading");
            parts.figure.removeAttribute("aria-busy");
        }

        renderCaption(link, src, true);
        setHash(hashFor(index));
        preload(list, index);
    }

    function go(step) {
        show(index + step);
    }

    // キャプション: ギャラリーの figcaption を写す（リンクはそのまま使える）
    function renderCaption(link, src, force) {
        var figure = link.closest("figure");
        var source = figure ? figure.querySelector("figcaption") : null;
        var html = source ? source.innerHTML : "";
        if (!force && html === lastCaption) return;
        lastCaption = html;
        var hadFocus = parts.caption.contains(document.activeElement);
        parts.caption.textContent = "";
        if (source) {
            var info = element("div", "album-lb-info");
            Array.prototype.forEach.call(source.children, function (child) {
                if (child.getAttribute("aria-hidden") === "true") return;      // 名前の取得待ちの空き
                info.appendChild(child.cloneNode(true));
            });
            parts.caption.appendChild(info);
        }
        var original = element("a", "album-lb-open-original", "画像だけを開く");
        original.href = src;
        original.target = "_blank";
        original.rel = "noopener noreferrer";
        parts.caption.appendChild(original);
        if (hadFocus) parts.close.focus();            // フォーカスのあったリンクを作り直したので、閉じるボタンへ（フォーカスが body に落ちない）
    }

    function preload(list, i) {
        preloaded = [];
        [1, -1].forEach(function (step) {
            if (list.length < 2) return;
            var img = new Image();
            img.decoding = "async";
            var next = list[wrapIndex(i + step, list.length)];
            if (!allowed(next)) return;
            img.src = next.getAttribute("href");
            preloaded.push(img);
        });
    }

    function setHash(hash) {
        try {
            if (location.hash === hash) return;
            history.replaceState(history.state, "", location.pathname + location.search + hash);
        } catch { /* 履歴を書き換えられない環境では、URL は変えない */ }
    }

    function onClosed() {
        token++;
        document.documentElement.classList.remove("album-lb-open");
        if (observer) { observer.disconnect(); observer = null; }
        parts.image.removeAttribute("src");
        parts.image.onload = parts.image.onerror = null;
        preloaded = [];
        touch = null;
        if (parsePhotoHash(location.hash, 1000) >= 0) {
            try { history.replaceState(history.state, "", location.pathname + location.search); } catch { /* 同上 */ }
        }
        // 閉じたら、いまの写真のリンクへフォーカスを戻す（Blazor の描画でリンクが入れ替わっていても、番号で探し直す）
        var list = links();
        var target = list.length > 0 ? list[wrapIndex(index, list.length)] : null;
        if (target && !leaving) target.focus();
        leaving = false;
    }

    function onKeyDown(e) {
        if (e.defaultPrevented || !dialog || !dialog.open || e.metaKey || e.ctrlKey || e.altKey) return;
        if (e.key === "ArrowLeft") { e.preventDefault(); go(-1); }
        else if (e.key === "ArrowRight") { e.preventDefault(); go(1); }
        else if (e.key === "Home") { e.preventDefault(); show(0); }
        else if (e.key === "End") { e.preventDefault(); show(links().length - 1); }
        else if (e.key === "Tab") trapTab(e);
    }

    function trapTab(e) {
        var items = Array.prototype.filter.call(dialog.querySelectorAll(FOCUSABLE), function (el) { return !el.hidden && el.offsetParent !== null; });
        var next = tabTarget(items.indexOf(document.activeElement), items.length, e.shiftKey);
        if (next >= 0) { e.preventDefault(); items[next].focus(); }
    }

    function onDialogClick(e) {
        var target = e.target;
        if (target === dialog || target === parts.stage || target === parts.figure) { dialog.close(); return; }
        // キャプションのリンク（同じサイトの中）を押したら、ライトボックスを閉じてから移る（新しいタブで開くリンクは開いたまま）
        var anchor = target.closest ? target.closest("a[href]") : null;
        if (anchor && parts.caption.contains(anchor) && anchor.target !== "_blank") {
            leaving = true;
            dialog.close();
        }
    }

    function onTouchStart(e) {
        touch = e.touches.length === 1 ? { x: e.touches[0].clientX, y: e.touches[0].clientY } : null;
    }

    function onTouchEnd(e) {
        if (!touch || e.changedTouches.length !== 1) { touch = null; return; }
        var direction = swipeDirection(e.changedTouches[0].clientX - touch.x, e.changedTouches[0].clientY - touch.y);
        touch = null;
        if (direction !== 0) go(direction);
    }

    // 開いている間: ページが入れ替わった（別の記事へ移った）なら閉じる。表示が入れ替わったら、キャプションを写し直す
    function watch() {
        var root = document.getElementById("app");
        if (!root || typeof MutationObserver !== "function") return;
        observer = new MutationObserver(function () {
            if (frame) return;
            frame = requestAnimationFrame(function () {
                frame = 0;
                if (!dialog || !dialog.open) return;
                var list = links();
                if (list.length === 0) {
                    // 起動の途中は、静的な本文から Blazor の描画に入れ替わる間、ギャラリーが一瞬無くなる。しばらく戻らなければ（別のページへ移った）閉じる
                    if (!emptyTimer) emptyTimer = setTimeout(function () {
                        emptyTimer = 0;
                        if (dialog && dialog.open && links().length === 0) { leaving = true; dialog.close(); }
                    }, 3000);
                    return;
                }
                if (emptyTimer) { clearTimeout(emptyTimer); emptyTimer = 0; }
                var i = wrapIndex(index, list.length);
                parts.count.textContent = (i + 1) + " / " + list.length;
                renderCaption(list[i], list[i].getAttribute("href"), false);
            });
        });
        observer.observe(root, { childList: true, subtree: true, characterData: true });
    }

    function openFromHash() {
        var i = parsePhotoHash(location.hash, links().length);
        if (i < 0) {
            if (dialog && dialog.open) dialog.close();
            return false;
        }
        if (dialog && dialog.open) { show(i); return true; }
        return open(i);
    }

    // クリック（Enter も click になる）。修飾キー・中ボタン・新しいタブのクリックは、ふつうのリンクとして通す。
    // 捕捉の段階で止める: 確認用の画像は同一オリジンなので、止めないと Blazor のリンク処理が「サイト内の移動」として 404 の画面へ移してしまう
    document.addEventListener("click", function (e) {
        if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
        var link = e.target.closest ? e.target.closest("a.album-photo-link") : null;
        if (!link) return;
        var i = links().indexOf(link);
        if (i < 0 || !supported() || !allowed(link)) return;
        e.preventDefault();
        e.stopPropagation();
        open(i);
    }, true);

    // Space（リンクは Enter だけで動く。ボタンのように Space でも開く）
    document.addEventListener("keydown", function (e) {
        if (e.key !== " " || e.defaultPrevented || e.metaKey || e.ctrlKey || e.altKey) return;
        var link = e.target.closest ? e.target.closest("a.album-photo-link") : null;
        if (!link || !supported() || !allowed(link)) return;
        e.preventDefault();
        var i = links().indexOf(link);
        if (i >= 0) open(i);
    }, true);

    window.addEventListener("hashchange", openFromHash);

    // URL の #photo-N で開いたあと、Blazor の起動（本文へのフォーカス移動など）がフォーカスを奪うことがある。起動が終わるまで、ダイアログの外に出たら閉じるボタンへ戻す
    function guardFocusUntilBooted() {
        var tries = 0;
        var timer = setInterval(function () {
            tries++;
            if (!dialog || !dialog.open) { clearInterval(timer); return; }
            if (!dialog.contains(document.activeElement)) parts.close.focus();
            else if (!document.getElementById("boot-indicator") && tries > 3) clearInterval(timer);
            if (tries > 100) clearInterval(timer);
        }, 150);
    }

    function start() {
        loadAllowedOrigins();
        if (location.hash && openFromHash()) guardFocusUntilBooted();
    }

    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", start);
    else start();
})();
