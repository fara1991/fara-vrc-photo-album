#!/usr/bin/env python3
"""ヘッダー画像（src/FaraVrcAlbum.Web/wwwroot/images/header-{ハッシュ}.webp）を、元の 1920x480 から作る。

表示は最大でも PC 448px 幅（高さ 112px）・スマホ 288px 幅なので、2x の 960x240 に縮める（約 90KB → 30KB 前後）。
元の画像の左下にある、背景の扉のかすかな明るい四角（x 105〜210・y 390〜480）は、左隣の色でなじませる（継ぎ目に見えるため）。
  python tools/make-header-image.py <元の 1920x480 の WebP> [出力フォルダ]
出力のファイル名の 8 桁は内容の SHA-256 の先頭。できたら SiteInfo.HeaderImage（と幅・高さ）を直す。元の写真は Git に入れない。
"""
import hashlib
import io
import os
import sys

from PIL import Image, ImageFilter

src = sys.argv[1]
out_dir = sys.argv[2] if len(sys.argv) > 2 else "src/FaraVrcAlbum.Web/wwwroot/images"
im = Image.open(src).convert("RGB")
assert im.size == (1920, 480), im.size
px = im.load()
x0, x1, y0, y1 = 100, 215, 385, 480
# 行ごとに、四角の左（x 60〜95）の平均色で埋め、上端・左右の端は幅 10px でぼかして重ねる
patch = Image.new("RGB", (x1 - x0, y1 - y0))
pp = patch.load()
for y in range(y0, y1):
    n = 35
    avg = tuple(sum(px[x, y][c] for x in range(60, 95)) // n for c in range(3))
    for x in range(x0, x1):
        pp[x - x0, y - y0] = avg
mask = Image.new("L", im.size, 0)
mask.paste(255, (x0 + 10, y0 + 10, x1 - 10, y1))
mask = mask.filter(ImageFilter.GaussianBlur(8))
full = Image.new("RGB", im.size)
full.paste(patch, (x0, y0))
im = Image.composite(full, im, mask)
small = im.resize((960, 240), Image.LANCZOS)
buf = io.BytesIO()
small.save(buf, "WEBP", quality=80, method=6)
data = buf.getvalue()
name = f"header-{hashlib.sha256(data).hexdigest()[:8]}.webp"
os.makedirs(out_dir, exist_ok=True)
with open(os.path.join(out_dir, name), "wb") as f:
    f.write(data)
print(name, len(data), "bytes")
