# -*- coding: utf-8 -*-
r"""Diagnostic: how much of the tray icon slot does the Caffeine ink fill?

Two things the eye cannot judge reliably at 32px:

  * the tray icon *slot* is SM_CXSMICON (16/32/48 at 100/200/300%), but the
    artwork inside the 32px tile can easily be a third of that, which is what
    "the icon looks tiny" actually means;
  * menu row pitch, i.e. how many device pixels sit between one baseline and
    the next, which is what "the text is cramped" means.

Both are measured here instead of guessed.  `brown` detection is unique to the
cup: its outline (58,35,20), brew (96,53,24) and crema (186,126,68) are the only
warm brown pixels anywhere in the system tray.

    python tools\probe_tray.py <taskbar-png> [<menu-png>]
"""

import os
import sys

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.abspath(os.path.join(HERE, "..", "assets"))


def is_brown(p):
    """Cup outline / brew / crema.  Nothing else in the tray looks like this."""
    r, g, b = p[0], p[1], p[2]
    return r > 30 and r >= g + 10 and g >= b


def find_brown(im):
    px = im.load()
    w, h = im.size
    x0, y0, x1, y1 = w, h, -1, -1
    for y in range(h):
        for x in range(w):
            if is_brown(px[x, y]):
                if x < x0: x0 = x
                if x > x1: x1 = x
                if y < y0: y0 = y
                if y > y1: y1 = y
    return (x0, y0, x1, y1)


def zoom_grid(im, box, out, zoom=6, step=4, major=8):
    """Nearest-neighbour blow-up with a 1px grid every `step` source px.

    Reading a ruler off the blow-up is much more reliable than counting pixels
    in a 32px tile by eye.
    """
    c = im.crop(box)
    big = c.resize((c.width * zoom, c.height * zoom), Image.NEAREST)
    d = ImageDraw.Draw(big)
    for sx in range(0, c.width + 1, step):
        X = sx * zoom
        col = (255, 0, 0) if (sx % major) == 0 else (255, 150, 150)
        d.line([(X, 0), (X, big.height)], fill=col, width=1)
    for sy in range(0, c.height + 1, step):
        Y = sy * zoom
        col = (0, 0, 255) if (sy % major) == 0 else (150, 150, 255)
        d.line([(0, Y), (big.width, Y)], fill=col, width=1)
    big.save(os.path.join(OUT, out))
    print("  wrote %s  crop=%s zoom=%dx" % (out, box, zoom))


def probe_tray(path):
    print("tray: %s" % path)
    im = Image.open(path).convert("RGB")
    print("  image size      : %dx%d" % im.size)
    bb = find_brown(im)
    if bb[2] < 0:
        print("  no brown pixels found")
        return
    x0, y0, x1, y1 = bb
    print("  cup ink bbox    : x %d..%d  y %d..%d" % bb)
    print("  cup ink size    : %dx%d px" % (x1 - x0 + 1, y1 - y0 + 1))
    m = 26
    box = (max(0, x0 - m), max(0, y0 - m),
           min(im.width, x1 + 1 + m), min(im.height, y1 + 1 + m))
    print("  crop around it  : %s  (%dx%d)" % (box, box[2] - box[0], box[3] - box[1]))
    zoom_grid(im, box, "_probe_tray.png")


def probe_menu(path):
    print("menu: %s" % path)
    im = Image.open(path).convert("RGB")
    w, h = im.size
    print("  image size      : %dx%d" % (w, h))
    px = im.load()

    # text = anything much darker than the menu background.  Works on both the
    # light popup and the blue highlight bar.
    rowdark = []
    for y in range(h):
        n = 0
        for x in range(w):
            r, g, b = px[x, y][:3]
            if r + g + b < 240:
                n += 1
        rowdark.append(n)

    bands = []
    inb = False
    for y, n in enumerate(rowdark):
        if n > 0 and not inb:
            inb, start = True, y
        elif n == 0 and inb:
            inb = False
            bands.append((start, y - 1))
    if inb:
        bands.append((start, h - 1))

    bands = [b for b in bands if b[1] - b[0] >= 2]
    print("  text bands      : %d" % len(bands))
    prev = None
    for i, (a, b) in enumerate(bands):
        pitch = (a - prev) if prev is not None else 0
        print("    band %2d  y %3d..%3d  h=%2d  gap_to_prev=%s"
              % (i + 1, a, b, b - a + 1, pitch if prev is not None else "-"))
        prev = a

    if bands:
        y0, y1 = bands[0][0], bands[-1][1]
        box = (0, max(0, y0 - 18), w, min(h, y1 + 19))
        zoom_grid(im, box, "_probe_menu.png")


def main():
    args = sys.argv[1:]
    if not args:
        print(__doc__)
        return
    probe_tray(args[0])
    if len(args) > 1:
        probe_menu(args[1])


if __name__ == "__main__":
    main()
