# -*- coding: utf-8 -*-
r"""Preview uniform ink-scale options for the C3+a tray icon.

`make_icons.INK_SCALE` enlarges the whole drawing about a fixed anchor without
distorting it, but the useful range is not obvious: the drawing's horizontal
extent already runs 0.13..0.87 of the tile (saucer left edge to handle right
edge), so a pure scale stops fitting somewhere around 1.2.  This renders the
candidates at true pixel size on both a light and a dark taskbar, and prints
the measured ink bounding box of each so "does it clip?" is answered by the
render rather than by arithmetic on paper.

    python tools\icon_scale.py        ->  assets\icon_scale.png
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import make_icons as mi

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.abspath(os.path.join(HERE, "..", "assets"))

SIZES = (16, 20, 24, 32, 48, 64)

# (label, overrides applied to make_icons before rendering)
VARIANTS = [
    ("S0  x1.00  (current)", {}),
    ("S1  x1.10  pure scale", {"INK_SCALE": 1.10}),
    ("S2  x1.18  pure scale", {"INK_SCALE": 1.18}),
    ("S3  x1.18  +saucer/handle -8%",
     {"INK_SCALE": 1.18, "SAUCER_HW": 0.290, "HANDLE_HW": 0.230}),
    ("S4  x1.26  +low steam +tray -8%",
     {"INK_SCALE": 1.26, "SAUCER_HW": 0.290, "HANDLE_HW": 0.230,
      "STEAM_Y0": 0.205, "STEAM_Y1": 0.310}),
]

DARK = (32, 32, 32, 255)
LIGHT = (250, 250, 250, 255)


def _font(px):
    for name in ("arial.ttf", "segoeui.ttf", "tahoma.ttf"):
        p = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts", name)
        if os.path.exists(p):
            try:
                from PIL import ImageFont
                return ImageFont.truetype(p, px)
            except Exception:
                pass
    return None


def apply(overrides):
    saved = dict((k, getattr(mi, k)) for k in overrides)
    for k, v in overrides.items():
        setattr(mi, k, v)
    return saved


def restore(saved):
    for k, v in saved.items():
        setattr(mi, k, v)


def ink_bbox(full):
    """Measured ink extent of the icon in 0..1 of the tile, at 128px."""
    a = mi.render(128, full).getchannel("A")
    x0, y0, x1, y1 = a.getbbox()
    return x0 / 128.0, y0 / 128.0, (x1 - 1) / 128.0, (y1 - 1) / 128.0


def strip(sizes, full, bg, pad=8):
    row_w = sum(s + pad for s in sizes)
    rowh = max(sizes) + 8
    from PIL import Image
    im = Image.new("RGBA", (row_w, rowh), bg)
    x = pad // 2
    for s in sizes:
        art = mi.render(s, full)
        im.paste(art, (x, (rowh - s) // 2), art)
        x += s + pad
    return im


def main():
    from PIL import Image, ImageDraw

    f_ttl = _font(19)
    f_lbl = _font(15)
    f_num = _font(12)

    cellw = sum(s + 8 for s in SIZES)
    rowh = max(SIZES) + 8
    label_w = 250
    gap = 14
    head = 58
    per = rowh * 2 + 26

    W = label_w + cellw * 2 + gap * 2 + 24
    H = head + per * len(VARIANTS) + 34

    sheet = Image.new("RGBA", (W, H), (245, 245, 245, 255))
    d = ImageDraw.Draw(sheet)

    d.text((12, 10), "C3+a  tray icon  -  ink scale options",
           fill=(15, 15, 15, 255), font=f_ttl)
    d.text((12, 34), "every cell is the real rendered pixel count;  "
                     "idle top, keep-awake below;  dark taskbar left, light right",
           fill=(85, 85, 85, 255), font=f_num)

    # size ruler
    x = label_w + 12
    for _ in range(2):
        cx = x
        for s in SIZES:
            d.text((cx, head - 17), str(s), fill=(110, 110, 110, 255), font=f_num)
            cx += s + 8
        x += cellw + gap

    y = head
    for label, over in VARIANTS:
        saved = apply(over)
        try:
            ei = ink_bbox(False)
            ea = ink_bbox(True)
            print("%-34s idle ink  x %.3f..%.3f  y %.3f..%.3f  (w %.0f%% h %.0f%%)"
                  % (label, ei[0], ei[2], ei[1], ei[3],
                     (ei[2] - ei[0]) * 100, (ei[3] - ei[1]) * 100))
            print("%-34s awake ink x %.3f..%.3f  y %.3f..%.3f  (w %.0f%% h %.0f%%)"
                  % ("", ea[0], ea[2], ea[1], ea[3],
                     (ea[2] - ea[0]) * 100, (ea[3] - ea[1]) * 100))

            d.text((12, y + 4), label, fill=(15, 15, 15, 255), font=f_lbl)
            d.text((12, y + 26),
                   "idle ink %dx%d px @32" % (round((ei[2] - ei[0]) * 32),
                                               round((ei[3] - ei[1]) * 32)),
                   fill=(90, 90, 90, 255), font=f_num)
            d.text((12, y + 42),
                   "awake ink %dx%d px @32" % (round((ea[2] - ea[0]) * 32),
                                               round((ea[3] - ea[1]) * 32)),
                   fill=(90, 90, 90, 255), font=f_num)

            for r, full in enumerate((False, True)):
                for c, bg in enumerate((DARK, LIGHT)):
                    s = strip(SIZES, full, bg)
                    sheet.paste(s, (label_w + 12 + c * (cellw + gap),
                                    y + r * rowh))
        finally:
            restore(saved)

        d.line([(8, y + per - 12), (W - 8, y + per - 12)], fill=(215, 215, 215, 255))
        y += per

    out = os.path.join(ASSETS, "icon_scale.png")
    sheet.save(out)
    print("wrote", out, sheet.size)


if __name__ == "__main__":
    main()
