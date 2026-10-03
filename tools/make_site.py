# -*- coding: utf-8 -*-
r"""Generates site\assets\ from the artwork and screenshots already in assets\.

The landing page is hand-written HTML (site\index.html + site\styles.css) - this
script only produces the images it links to, so that a re-run after an icon
change picks the new artwork up automatically.

What it produces:

  site/assets/hero.png          256px cup, rendered fresh from make_icons
  site/assets/favicon.png        64px, same source
  site/assets/tray-idle.png      real taskbar strip, cropped to the tray end
  site/assets/tray-awake.png     same, keep-awake state
  site/assets/menu.png           the real right-click menu

The taskbar screenshots are 1471x108 crops whose interesting part is the right
hand end, so they get cropped down; 1:1 pixels, never upscaled.

    python tools\make_site.py
"""

import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import make_icons as mi

from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, ".."))
ASSETS = os.path.join(ROOT, "assets")
OUT = os.path.join(ROOT, "site", "assets")

# (source screenshot, output name, crop box or None)
# The taskbar shots are 1471x108 crops whose interesting part is the right hand
# end, so they get cropped down.  The menu shot is a screen grab around the
# popup: the last 8 rows are the taskbar showing through the drop shadow, so
# they get trimmed too.  Everything is 1:1 pixels, never upscaled.
SHOTS = [
    ("taskbar_idle.png", "tray-idle.png", (930, 20, 1471, 96)),
    ("taskbar_awake.png", "tray-awake.png", (930, 20, 1471, 96)),
    ("menu_installed.png", "menu.png", (0, 0, 489, 331)),
]


def main():
    if not os.path.isdir(OUT):
        os.makedirs(OUT)

    # ---- the cup, re-rendered large rather than upscaled from the ICO -------
    # make_icons' biggest ICO entry is 64px; a hero image needs more than that,
    # and rendering at 256 keeps every stroke vector-crisp.
    for size, name in ((256, "hero.png"), (64, "favicon.png")):
        im = mi.render(size, True)
        path = os.path.join(OUT, name)
        im.save(path)
        print("wrote %s (%dx%d)" % (name, im.width, im.height))

    # ---- real screenshots --------------------------------------------------
    for src, dst, box in SHOTS:
        spath = os.path.join(ASSETS, src)
        if not os.path.exists(spath):
            print("  skip %s - %s not found" % (dst, src))
            print("       take it first: tests\\shot-taskbar.ps1 / tests\\shot-menu.ps1")
            continue
        im = Image.open(spath).convert("RGB")
        if box:
            box = (min(box[0], im.width), min(box[1], im.height),
                   min(box[2], im.width), min(box[3], im.height))
            im = im.crop(box)
        path = os.path.join(OUT, dst)
        im.save(path, optimize=True)
        print("wrote %s  %dx%d  (%d KB)" % (dst, im.width, im.height,
                                            os.path.getsize(path) // 1024))


if __name__ == "__main__":
    main()
