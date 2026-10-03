# -*- coding: utf-8 -*-
r"""
Generates the two tray icons for the Caffeine Windows tray app:

  * caffeine_empty.ico  -> empty cup  (idle, default power settings in effect)
  * caffeine_full.ico   -> full cup with rising steam (system kept awake)

Shape is "soft curve + visible saucer, bigger handle" - option C3+a from
tools\icon_options.py.  Every size is drawn as vector art and supersampled 8x,
then box-filtered down, so 16px stays crisp.  Each stroke is drawn twice: a
white halo first, then the real colour, so the icons stay readable on light
*and* dark taskbars.

Why it looks the way it does - a 16px tray icon is unforgiving, and this shape
went through several rounds of feedback:

  * "the steam is too long and the cup too short" - the first art drew a squat
    trapezoid 0.265 of the canvas tall under a steam column 0.226 tall, and
    only a 1.0px outline at 16px that a 0.6px white halo ate most of.  Hence
    the thicker lines and the narrower halo below.
  * "the cup is too tall, it no longer looks like a coffee cup" - overshooting
    gave a straight-walled mug with a height/width ratio of 1.56.  A real
    coffee cup is a flared bowl, which is what the curve below draws: widest
    at the rim, curving in to a small foot, h/w 0.59.
  * "still too big a change" - so the body only grew ~20% over the original
    (0.265 -> 0.32 tall) and the ink now balances about the middle of the tile
    instead of sitting low with a steam cloud above it.
  * "the icon is really small on the taskbar" - measured, not guessed: the tile
    is 32px at 200% DPI but the idle ink was only 24x16px in it, because the
    steam claims the space above the cup in *both* states and sits empty when
    the cup is empty.  Hence INK_SCALE below: a pure uniform 1.18x, which is
    the largest that still fits the tile horizontally without trimming the
    saucer or the handle (option S2 in tools\icon_scale.py).
"""

import math
import os
import struct

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.abspath(os.path.join(HERE, "..", "assets"))

# The tray icon is picked by SM_CXSMICON: 16px at 100%, 32px at 200%, 48px at
# 300%.  128/256 are only ever used for the .exe icon in Explorer, and they cost
# ~280 KB each in raw DIB, so they are deliberately left out of the build.
SIZES = [16, 20, 24, 32, 40, 48, 64]
SS = 8  # supersampling factor

# ---- cup geometry, all in 0..1 of the tile ----------------------------
CX = 0.445                 # shifted left of centre to leave room for the handle
RIM_Y = 0.410              # top of the cup body
CUP_BOTTOM = 0.720         # where the walls meet the foot
RIM_HW = 0.250             # half width at the rim
FOOT_HW = 0.140            # half width at the foot
CURVE = 1.25               # wall exponent: >1 flares out towards the rim
RIM_HH = 0.050             # half height of the rim ellipse

SAUCER_Y = 0.748           # top edge of the saucer
SAUCER_HW = 0.315
SAUCER_HH = 0.085

HANDLE_X = 0.170           # left edge, relative to CX
HANDLE_HW = 0.250          # bigger and rounder than C3's 0.225
HANDLE_Y0 = 0.440
HANDLE_Y1 = 0.700

# ---- uniform ink scale ------------------------------------------------
# The tray tile is 32px at 200% DPI, but this artwork only fills ~50% of the
# tile's height while idle: the steam claims the space above the cup even in
# the empty icon, because both states come from one set of geometry.  Scaling
# the whole drawing about a fixed anchor enlarges the cup *without distorting
# it* - cup/handle/saucer ratios, the rim/bowl curve and the stroke weights
# all stay exactly as authored.
#
# Hard ceiling: the horizontal extent already runs 0.13..0.87 of the tile
# (saucer left edge to handle right edge), so past ~1.2 the drawing starts
# clipping the tile horizontally no matter how much vertical room is free.
INK_SCALE = 1.18           # chosen as option S2 in tools\icon_scale.py
INK_AX = 0.500             # anchor x = centre of the horizontal ink extent
INK_AY = 0.480             # anchor y = centre of the awake ink extent

# ---- line weights, as a fraction of the rendered icon size ------------
W_BODY = 0.074             # 1.18px at 16px
W_RIM = 0.068
W_HANDLE = 0.082
HALO = 0.032               # 0.51px at 16px, down from 0.038

# ---- steam: 2 wisps, deliberately small --------------------------------
STEAM_N = 2
STEAM_Y0 = 0.160
STEAM_Y1 = 0.315
STEAM_AMP = 0.025
STEAM_SPREAD = 0.120
STEAM_W = 0.062
STEAM_LEAN = 0.020         # each wisp drifts sideways as it rises
STEAM_STAGGER = 0.030      # so the two are not identical lengths

# palette
OUTLINE = (58, 35, 20, 255)       # dark coffee brown line work
CERAMIC = (252, 248, 241, 255)    # warm white cup body
COFFEE = (96, 53, 24, 255)        # dark brew surface
COFFEE_HI = (186, 126, 68, 255)   # crema highlight
SAUCER = (243, 235, 224, 255)
STEAM = (112, 132, 148, 255)      # soft blue-grey steam
HALO_COLOR = (255, 255, 255, 255)

BODY_STEPS = 16


def GX(v):
    """Authored x in 0..1 -> scaled x in 0..1, uniform about INK_AX."""
    return INK_AX + (v - INK_AX) * INK_SCALE


def GY(v):
    """Authored y in 0..1 -> scaled y in 0..1, uniform about INK_AY.

    Applied to absolute y values, not to distances between them, so a scaled
    cup stays the same height *and* keeps the same shape.
    """
    return INK_AY + (v - INK_AY) * INK_SCALE


class Pen:
    """Stroked vector primitives with an automatic white halo underneath.

    Every method paints twice: once with a fat white stroke (halo) and once
    with the real colour.  `extra` widens only the halo pass, which is how the
    steam gets an extra breathing ring.
    """

    def __init__(self, draw, halo=0.0):
        self.d = draw
        self.halo = halo

    def _pair(self, paint, w, extra, fill, color=OUTLINE):
        """paint(color, width, fill) -> None"""
        if w > 0 and (self.halo + extra) > 0:
            paint(HALO_COLOR, w + self.halo + extra, None)
        paint(color if w > 0 else None, w, fill)

    # ---- primitives -------------------------------------------------
    def ellipse(self, box, fill, w=0.0, extra=0.0, color=OUTLINE):
        def paint(col, width, f):
            o = col if (col is not None and width > 0) else None
            self.d.ellipse(box, fill=f, outline=o,
                           width=int(round(width)) if width else 1)
        self._pair(paint, w, extra, fill, color)

    def polyline(self, pts, close, w=0.0, extra=0.0, fill=None, color=OUTLINE):
        seq = list(pts) + ([pts[0]] if close else [])
        r = w / 2.0

        def paint(col, width, f):
            if f is not None:
                self.d.polygon(seq, fill=f)
            if width > 0:
                self.d.line(seq, fill=col, width=int(round(width)), joint="curve")
                for x, y in (seq[0], seq[-1]):
                    self.d.ellipse([x - r, y - r, x + r, y + r], fill=col)
        self._pair(paint, w, extra, fill, color)

    def stroke(self, pts, color, w=0.0, extra=0.0):
        def paint(c, width, _f):
            self.d.line(pts, fill=c, width=int(round(width)), joint="curve")
            r = width / 2.0
            for x, y in (pts[0], pts[-1]):
                self.d.ellipse([x - r, y - r, x + r, y + r], fill=c)
        self._pair(paint, w, extra, None, color)

    def arc(self, box, start, end, w=0.0, extra=0.0):
        def paint(color, width, _f):
            self.d.arc(box, start, end, fill=color, width=int(round(width)))
        self._pair(paint, w, extra, None)


def sw(size, factor):
    """Stroke width in supersampled canvas px for a `size`px target icon.

    Line weight is specified relative to the *rendered* icon size rather than
    as a fixed fraction of the canvas, so the outline lands at ~1.2 device px
    at 16px and stays optically even all the way up.
    """
    return max(1.0, size * factor) * SS


def steam_paths(px):
    """STEAM_N curls of steam, authored in 0..1 space, returned in pixels."""
    out = []
    for i in range(STEAM_N):
        frac = 0.5 if STEAM_N == 1 else i / float(STEAM_N - 1)
        xc = CX + (frac - 0.5) * STEAM_SPREAD
        ph = i * 0.44
        lean = STEAM_LEAN * (1 if i % 2 == 0 else -1)
        up = STEAM_STAGGER * i
        pts = []
        steps = 24
        for s in range(steps + 1):
            t = s / steps
            y = STEAM_Y0 + up * (1 - t) + (STEAM_Y1 - STEAM_Y0) * t
            x = xc + STEAM_AMP * math.sin((t * 1.6 + ph) * 2 * math.pi * 0.60)
            x += lean * t * t
            pts.append((GX(x) * px, GY(y) * px))
        out.append(pts)
    return out


def body_path(px):
    """The flared bowl: widest at the rim, curving in to a small foot.

    A straight taper reads as a mug, which is exactly what this is not meant
    to be - the curve is what makes it say "coffee cup" at a glance.
    """
    left, right = [], []
    for i in range(BODY_STEPS + 1):
        t = i / float(BODY_STEPS)
        y = RIM_Y + (CUP_BOTTOM - RIM_Y) * t
        hw = FOOT_HW + (RIM_HW - FOOT_HW) * ((1.0 - t) ** CURVE)
        left.append((GX(CX - hw) * px, GY(y) * px))
        right.append((GX(CX + hw) * px, GY(y) * px))
    bot = FOOT_HW * 0.78
    return (left
            + [(GX(CX - bot) * px, GY(CUP_BOTTOM + 0.022) * px),
               (GX(CX + bot) * px, GY(CUP_BOTTOM + 0.022) * px)]
            + right[::-1])


def render(size, full):
    px = size * SS
    img = Image.new("RGBA", (px, px), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pen = Pen(d, halo=sw(size, HALO))

    # ---- steam first: the cup then overlaps the wisps' feet ----------
    if full:
        for p in steam_paths(px):
            pen.stroke(p, STEAM, w=sw(size, STEAM_W), extra=sw(size, 0.018))

    # ---- handle, behind the body -------------------------------------
    hb = [GX(CX + HANDLE_X) * px, GY(HANDLE_Y0) * px,
          GX(CX + HANDLE_X + HANDLE_HW) * px, GY(HANDLE_Y1) * px]
    pen.arc(hb, -62, 108, w=sw(size, W_HANDLE))

    # ---- saucer -------------------------------------------------------
    sy0 = GY(SAUCER_Y) * px
    sy1 = GY(SAUCER_Y + SAUCER_HH) * px
    pen.ellipse([GX(CX - SAUCER_HW) * px, sy0,
                 GX(CX + SAUCER_HW) * px, sy1],
                fill=SAUCER, w=sw(size, W_RIM * 0.92))

    # ---- cup body ------------------------------------------------------
    pen.polyline(body_path(px), close=True, w=sw(size, W_BODY), fill=CERAMIC)

    # ---- the opening ---------------------------------------------------
    rim = [GX(CX - RIM_HW) * px, GY(RIM_Y - RIM_HH) * px,
           GX(CX + RIM_HW) * px, GY(RIM_Y + RIM_HH) * px]
    inset = min(sw(size, 0.080), 0.34 * (rim[3] - rim[1]))
    inner = [rim[0] + inset, rim[1] + inset, rim[2] - inset, rim[3] - inset]

    d.ellipse(inner, fill=COFFEE if full else (255, 255, 255, 255))
    if full:
        d.ellipse([inner[0] + 0.030 * (inner[2] - inner[0]),
                   inner[1] + 0.110 * (inner[3] - inner[1]),
                   inner[2] - 0.030 * (inner[2] - inner[0]),
                   inner[1] + 0.620 * (inner[3] - inner[1])],
                  fill=COFFEE_HI)

    # rim ring last so it reads cleanly on top of the brew
    pen.ellipse(rim, fill=None, w=sw(size, W_RIM))

    return img.resize((size, size), Image.LANCZOS)


def dib_entry(im):
    """Classic 32bpp BITMAPINFOHEADER + BGRA pixels + AND mask.

    System.Drawing.Icon (and old shell code) is far happier with plain DIB
    entries than with PNG-compressed ones, so the icons are written this way.
    """
    w, h = im.size
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    rgba = im.convert("RGBA").tobytes()
    rows = []
    stride = w * 4
    for y in range(h - 1, -1, -1):          # DIB rows run bottom-up
        row = bytearray()
        base = y * stride
        for x in range(w):
            r, g, b, a = rgba[base + x * 4: base + x * 4 + 4]
            row += bytes((b, g, r, a))
        rows.append(bytes(row))
    xor = b"".join(rows)

    mask_row = ((w + 31) // 32) * 4
    mask = b"\x00" * (mask_row * h)          # all-zero AND mask: alpha wins
    return header + xor + mask


def save_ico(path, kind):
    images = [render(s, kind) for s in SIZES]
    blobs = [dib_entry(im) for im in images]
    n = len(images)
    offset = 6 + 16 * n
    entries = []
    for im, blob in zip(images, blobs):
        entries.append(struct.pack(
            "<BBBBHHII",
            im.width if im.width < 256 else 0,
            im.height if im.height < 256 else 0,
            0, 0, 1, 32, len(blob), offset))
        offset += len(blob)
    with open(path, "wb") as f:
        f.write(struct.pack("<HHH", 0, 1, n))
        for e in entries:
            f.write(e)
        for b in blobs:
            f.write(b)
    print("wrote %s (%d bytes, sizes=%s)" % (path, os.path.getsize(path), SIZES))


def preview(kind, name):
    sizes = (16, 20, 24, 32, 48, 64, 128)
    pad = 10
    row_w = sum(s + pad for s in sizes) + pad
    rowh = max(sizes) + pad
    light = (250, 250, 250, 255)
    dark = (34, 34, 34, 255)

    label_w = 120
    sheet = Image.new("RGBA", (label_w + row_w * 2, rowh * 2 + 26), (246, 246, 246, 255))
    dr = ImageDraw.Draw(sheet)
    dr.text((10, 6), "caffeine_%s.ico   -   OFF idle (top) / ON keep awake (bottom)"
            % ("empty" if not kind else "full"),
            fill=(20, 20, 20, 255))

    for r, full in enumerate((False, True)):
        y = 20 + r * rowh
        dr.text((10, y + rowh // 2 - 6), "ON  awake" if full else "OFF  idle",
                fill=(80, 80, 80, 255))
        x = label_w
        for bg in (light, dark):
            strip = Image.new("RGBA", (row_w, rowh), bg)
            sx = pad
            for s in sizes:
                art = render(s, full)
                strip.paste(art, (sx, (rowh - s) // 2), art)
                sx += s + pad
            sheet.paste(strip, (x, y))
            x += row_w

    sheet.save(os.path.join(OUT, name))
    print("wrote preview", name)


def main():
    os.makedirs(OUT, exist_ok=True)
    save_ico(os.path.join(OUT, "caffeine_empty.ico"), kind=False)
    save_ico(os.path.join(OUT, "caffeine_full.ico"), kind=True)
    preview(False, "preview_empty.png")
    preview(True, "preview_full.png")


if __name__ == "__main__":
    main()
