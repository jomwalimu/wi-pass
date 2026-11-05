"""
Microsoft Store (MSIX) visual assets for WiPass, from the same mark as make_assets.py.

Writes store/Assets/*.png with scale / targetsize qualifiers; the build indexes them with makepri.
Usage:  python3 tools/brand/make_store_assets.py
"""
import os

from PIL import Image

from make_assets import ROOT, app_icon

OUT = os.path.join(ROOT, "store", "Assets")


def tile(w, h, icon_ratio):
    """Transparent tile with the app icon centred (Start menu, Store)."""
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    s = int(round(min(w, h) * icon_ratio))
    ic = app_icon(s)
    img.alpha_composite(ic, ((w - s) // 2, (h - s) // 2))
    return img


def save(img, name):
    img.save(os.path.join(OUT, name))


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".png"):
            os.remove(os.path.join(OUT, f))
    for scale in (100, 125, 150, 200, 400):
        k = scale / 100.0
        save(app_icon(int(round(44 * k))), "Square44x44Logo.scale-%d.png" % scale)
        save(tile(int(round(150 * k)), int(round(150 * k)), 0.56), "Square150x150Logo.scale-%d.png" % scale)
        save(tile(int(round(310 * k)), int(round(150 * k)), 0.56), "Wide310x150Logo.scale-%d.png" % scale)
        save(app_icon(int(round(50 * k))), "StoreLogo.scale-%d.png" % scale)
    # Taskbar, Start list, Alt+Tab use the target-size icons (plated and unplated look the same: it is a tile).
    for size in (16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256):
        ic = app_icon(size)
        save(ic, "Square44x44Logo.targetsize-%d.png" % size)
        save(ic, "Square44x44Logo.targetsize-%d_altform-unplated.png" % size)
        save(ic, "Square44x44Logo.targetsize-%d_altform-lightunplated.png" % size)
    print("store assets:", len(os.listdir(OUT)), "files in", OUT)


if __name__ == "__main__":
    main()
