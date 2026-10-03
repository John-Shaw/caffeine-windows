# -*- coding: utf-8 -*-
r"""Renders alternative tray-icon designs side by side at real pixel sizes.

Nothing here is wired into the build.  Look at assets/icon_options.png, pick a
variant, then fold its geometry back into make_icons.py.

Two rounds of feedback shaped the numbers below:

  * round 1 - "the steam is too long and the cup is too short, and the idle
    icon is hard to see".  The old art drew a squat trapezoid only 0.265 of the
    canvas tall with a 0.226-tall steam column, and a 1.0px outline at 16px
    that the 0.6px white halo ate most of.
  * round 2 - "the cup is too tall, it no longer looks like a coffee cup".
    Overshooting in the other direction gave a body 0.70 tall and 0.45 wide,
    i.e. a height/width ratio of 1.56.  A real mug is about 1.0-1.2 and a
    teacup about 0.9.

So every variant below states its body HEIGHT/WIDTH ratio explicitly.  The ink
fills the tile (that part of round 1 was right), the steam is short, the
outline is ~1.4px at 16px, and the cup is a cup again.
"""

import math
import os

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.abspath(os.path.join(HERE, "..", "assets"))

SS = 8  # supersampling

OUTLINE = (58, 35, 20, 255)
CERAMIC = (252, 248, 241, 255)
COFFEE = (96, 53, 24, 255)
COFFEE_HI = (186, 126, 68, 255)
SAUCER = (243, 235, 224, 255)
STEAM = (112, 132, 148, 255)
HALO = (255, 255, 255, 255)

# h/w is the height of the cup body (rim to base) divided by its width at the
# rim.  ~1.15 mug, ~0.9 teacup, ~0.6 espresso cup.
VARIANTS = [
    dict(
        key="C3", label="C3   baseline  -   soft curve + visible saucer   h/w 0.59",
        cx=0.445, rim_y=0.410, cup_bottom=0.720, rim_hh=0.050,
        rim_hw=0.250, foot_hw=0.140, curve=1.25,
        saucer=0.028, saucer_hw=0.315, saucer_hh=0.085,
        handle_x=0.180, handle_y0=0.450, handle_y1=0.690, handle_hw=0.225,
        w_body=0.074, w_rim=0.068, w_handle=0.074, halo=0.032,
        steam=dict(n=2, y0=0.160, y1=0.315, amp=0.025, w=0.062,
                   spread=0.120, lean=0.020, stagger=0.030),
    ),
    dict(
        key="a", label="a   only the handle: bigger and rounder",
        cx=0.445, rim_y=0.410, cup_bottom=0.720, rim_hh=0.050,
        rim_hw=0.250, foot_hw=0.140, curve=1.25,
        saucer=0.028, saucer_hw=0.315, saucer_hh=0.085,
        handle_x=0.170, handle_y0=0.440, handle_y1=0.700, handle_hw=0.250,
        w_body=0.074, w_rim=0.068, w_handle=0.082, halo=0.032,
        steam=dict(n=2, y0=0.160, y1=0.315, amp=0.025, w=0.062,
                   spread=0.120, lean=0.020, stagger=0.030),
    ),
    dict(
        key="b", label="b   only the cup: wider mouth, a little more flare",
        cx=0.445, rim_y=0.410, cup_bottom=0.720, rim_hh=0.052,
        rim_hw=0.262, foot_hw=0.130, curve=1.45,
        saucer=0.028, saucer_hw=0.315, saucer_hh=0.085,
        handle_x=0.180, handle_y0=0.450, handle_y1=0.690, handle_hw=0.225,
        w_body=0.074, w_rim=0.068, w_handle=0.074, halo=0.032,
        steam=dict(n=2, y0=0.160, y1=0.315, amp=0.025, w=0.062,
                   spread=0.120, lean=0.020, stagger=0.030),
    ),
    dict(
        key="c", label="c   only the saucer: thinner and tucked into the cup",
        cx=0.445, rim_y=0.410, cup_bottom=0.720, rim_hh=0.050,
        rim_hw=0.250, foot_hw=0.140, curve=1.25,
        saucer=0.018, saucer_hw=0.290, saucer_hh=0.058,
        handle_x=0.180, handle_y0=0.450, handle_y1=0.690, handle_hw=0.225,
        w_body=0.074, w_rim=0.068, w_handle=0.074, halo=0.032,
        steam=dict(n=2, y0=0.160, y1=0.315, amp=0.025, w=0.062,
                   spread=0.120, lean=0.020, stagger=0.030),
    ),
    dict(
        key="d", label="d   only the steam: shorter, thinner, closer to the rim",
        cx=0.445, rim_y=0.410, cup_bottom=0.720, rim_hh=0.050,
        rim_hw=0.250, foot_hw=0.140, curve=1.25,
        saucer=0.028, saucer_hw=0.315, saucer_hh=0.085,
        handle_x=0.180, handle_y0=0.450, handle_y1=0.690, handle_hw=0.225,
        w_body=0.074, w_rim=0.068, w_handle=0.074, halo=0.032,
        steam=dict(n=2, y0=0.200, y1=0.320, amp=0.022, w=0.058,
                   spread=0.105, lean=0.020, stagger=0.026),
    ),
]


class Pen:
    """Stroked vector primitives with an automatic white halo underneath."""

    def __init__(self, draw, halo=0.0):
        self.d = draw
        self.halo = halo

    def _pair(self, paint, w, extra, fill, color=OUTLINE):
        if w > 0 and (self.halo + extra) > 0:
            paint(HALO, w + self.halo + extra, None)
        paint(color if w > 0 else None, w, fill)

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

    def hline(self, x0, x1, y, w=0.0, extra=0.0, color=OUTLINE):
        def paint(col, width, _f):
            self.d.line([(x0, y), (x1, y)], fill=col,
                        width=int(round(width)))
            r = width / 2.0
            for x in (x0, x1):
                self.d.ellipse([x - r, y - r, x + r, y + r], fill=col)
        self._pair(paint, w, extra, None, color)


def sw(size, factor):
    return max(1.0, size * factor) * SS


def steam_paths(px, spec, cx):
    """n curls of steam, authored in 0..1 space, returned in pixels.

    `lean` drifts each curl sideways as it rises so the wisps look drawn rather
    than like parallel bars, and `stagger` makes them different lengths, which
    is what stops two identical squiggles reading as a mechanical pattern.
    """
    out = []
    n = spec["n"]
    for i in range(n):
        frac = 0.5 if n == 1 else i / float(n - 1)
        xc = cx + (frac - 0.5) * spec["spread"]
        ph = i * 0.44
        lean = spec.get("lean", 0.0) * (1 if i % 2 == 0 else -1)
        up = spec.get("stagger", 0.0) * i      # shorter wisp starts lower
        pts = []
        steps = 24
        for s in range(steps + 1):
            t = s / steps
            y = spec["y0"] + up * (1 - t) + (spec["y1"] - spec["y0"]) * t
            x = xc + spec["amp"] * math.sin((t * 1.6 + ph) * 2 * math.pi * 0.60)
            x += lean * t * t
            pts.append((x * px, y * px))
        out.append(pts)
    return out


def render(size, v, full):
    px = size * SS
    img = Image.new("RGBA", (px, px), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pen = Pen(d, halo=sw(size, v["halo"]))

    cx = v["cx"]
    rim_y, cup_bottom = v["rim_y"], v["cup_bottom"]
    rim_hw = v["rim_hw"]

    if full:
        for p in steam_paths(px, v["steam"], cx):
            pen.stroke(p, STEAM, w=sw(size, v["steam"]["w"]), extra=sw(size, 0.020))

    # handle, behind the body
    if v["handle_hw"] > 0:
        hb = [(cx + v["handle_x"]) * px, v["handle_y0"] * px,
              (cx + v["handle_x"] + v["handle_hw"]) * px, v["handle_y1"] * px]
        pen.arc(hb, -62, 108, w=sw(size, v["w_handle"]))

    # saucer
    if v.get("saucer", 0) > 0:
        y0 = (cup_bottom + v["saucer"]) * px
        pen.ellipse([(cx - v["saucer_hw"]) * px, y0,
                     (cx + v["saucer_hw"]) * px, y0 + v["saucer_hh"] * px],
                    fill=SAUCER, w=sw(size, v["w_rim"] * 0.92))

    # a single flat line under the cup reads as "table" and is the cheapest
    # possible base - the most reduced stick-figure option
    ln = v.get("line")
    if ln:
        pen.hline(cx + ln[0] * px, cx + ln[1] * px, ln[2] * px,
                  w=sw(size, v.get("w_line", 0.075)), extra=sw(size, 0.014))

    # cup body.  A straight taper reads as a mug; a coffee cup is a flared
    # bowl, so the wall is a curve: widest at the rim, curving in fast near
    # the foot.  `curve` is the exponent that controls how tulip-like it gets.
    N = 16
    foot = v["foot_hw"]
    expo = v["curve"]
    left, right = [], []
    for i in range(N + 1):
        t = i / float(N)
        y = rim_y + (cup_bottom - rim_y) * t
        hw = foot + (rim_hw - foot) * ((1.0 - t) ** expo)
        left.append(((cx - hw) * px, y * px))
        right.append(((cx + hw) * px, y * px))
    bot = foot * 0.78
    body = (left
            + [((cx - bot) * px, (cup_bottom + 0.022) * px),
               ((cx + bot) * px, (cup_bottom + 0.022) * px)]
            + right[::-1])
    pen.polyline(body, close=True, w=sw(size, v["w_body"]), fill=CERAMIC)

    # the opening
    rim = [(cx - rim_hw) * px, (rim_y - v["rim_hh"]) * px,
           (cx + rim_hw) * px, (rim_y + v["rim_hh"]) * px]
    inset = min(sw(size, 0.080), 0.34 * (rim[3] - rim[1]))
    inner = [rim[0] + inset, rim[1] + inset, rim[2] - inset, rim[3] - inset]

    d.ellipse(inner, fill=COFFEE if full else (255, 255, 255, 255))
    if full:
        d.ellipse([inner[0] + 0.030 * (inner[2] - inner[0]),
                   inner[1] + 0.110 * (inner[3] - inner[1]),
                   inner[2] - 0.030 * (inner[2] - inner[0]),
                   inner[1] + 0.620 * (inner[3] - inner[1])],
                  fill=COFFEE_HI)

    pen.ellipse(rim, fill=None, w=sw(size, v["w_rim"]))

    return img.resize((size, size), Image.LANCZOS)


SIZES = (16, 20, 24, 32, 48, 64, 96)
PAD = 10
LABEL_W = 215


def sheet():
    light = (250, 250, 250, 255)
    dark = (34, 34, 34, 255)
    row_w = sum(s + PAD for s in SIZES) + PAD
    rowh = max(SIZES) + PAD
    width = LABEL_W + row_w * 2 + PAD
    header = 42

    height = header + len(VARIANTS) * (18 + 2 * (rowh + 3)) + 12
    out = Image.new("RGBA", (width, height), (246, 246, 246, 255))
    dr = ImageDraw.Draw(out)
    dr.text((PAD, 10), "Caffeine tray icon options  -  real pixel sizes",
            fill=(20, 20, 20, 255))
    dr.text((PAD, 25), "h/w = cup body height / width   |   sizes " +
            "/".join(str(s) for s in SIZES) +
            "   |   left strip light tray, right strip dark tray",
            fill=(120, 120, 120, 255))

    y = header
    for v in VARIANTS:
        dr.text((PAD, y + 2), v["label"], fill=(0, 96, 44, 255))
        dr.line([(LABEL_W - 14, y + 10), (width - PAD, y + 10)],
                fill=(0, 150, 70, 255))
        y += 18
        for full in (False, True):
            dr.text((PAD, y + rowh // 2 - 5), "ON  keep awake" if full else "OFF  idle",
                    fill=(70, 70, 70, 255))
            x = LABEL_W
            for bg in (light, dark):
                strip = Image.new("RGBA", (row_w, rowh), bg)
                sx = PAD
                for s in SIZES:
                    art = render(s, v, full)
                    strip.paste(art, (sx, (rowh - s) // 2), art)
                    sx += s + PAD
                out.paste(strip, (x, y))
                x += row_w
            dr.line([(0, y + rowh + 1), (width, y + rowh + 1)], fill=(214, 214, 214, 255))
            y += rowh + 3

    out = out.crop((0, 0, width, y))
    path = os.path.join(OUT, "icon_options.png")
    out.save(path)
    print("wrote", path, out.size)


def compact():
    """One column per variant, panels stacked.

    The tall sheet gets scaled down to unreadable when it has to fit a message
    window, so this one is narrow enough that every icon stays big, and only
    the sizes that actually decide whether the shape reads.
    """
    sizes = (24, 32, 48, 64)
    gap = 10
    colw = sum(s + gap for s in sizes)
    label_w = 62
    pad = 12
    rowh = max(sizes) + gap
    panel_h = 18 + rowh * 2 + 6

    width = label_w + len(VARIANTS) * colw + pad
    height = 30 + 2 * (panel_h + 22) + 8

    out = Image.new("RGBA", (width, height), (247, 247, 247, 255))
    dr = ImageDraw.Draw(out)
    dr.text((pad, 9), "OFF = idle (empty cup)      ON = keep awake (full cup + steam)",
            fill=(20, 20, 20, 255))

    y = 30
    for bg, name, fg in (((250, 250, 250, 255), "light tray", (90, 90, 90, 255)),
                         ((34, 34, 34, 255), "dark tray", (170, 170, 170, 255))):
        dr.text((pad, y), name, fill=fg)
        for c, v in enumerate(VARIANTS):
            dr.text((label_w + c * colw + 2, y), v["key"], fill=(0, 150, 70, 255))
        for r, full in enumerate((False, True)):
            ry = y + 18 + r * rowh
            dr.text((pad, ry + rowh // 2 - 5), "ON" if full else "OFF", fill=fg)
            for c, v in enumerate(VARIANTS):
                tile = Image.new("RGBA", (colw, rowh), bg)
                sx = gap
                for s in sizes:
                    art = render(s, v, full)
                    tile.paste(art, (sx, (rowh - s) // 2), art)
                    sx += s + gap
                out.paste(tile, (label_w + c * colw, ry))
        y += panel_h
        if bg[0] > 100:
            dr.line([(pad, y + 8), (width - pad, y + 8)], fill=(205, 205, 205, 255))
            y += 18

    path = os.path.join(OUT, "icon_options_compact.png")
    out.save(path)
    print("wrote", path, out.size)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    sheet()
    compact()
