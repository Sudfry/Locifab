from pathlib import Path
from PIL import Image, ImageDraw
import math

ROOT = Path(__file__).parent / "layout" / "Assets"
ROOT.mkdir(parents=True, exist_ok=True)

def logo(size, transparent=True):
    s = 512
    img = Image.new("RGBA", (s, s), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    # rounded gradient tile
    for y in range(s):
        t = y / (s - 1)
        r = int(79 + (124 - 79) * t)
        g = int(70 + (58 - 70) * t)
        b = int(229 + (237 - 229) * t)
        d.line([(0, y), (s, y)], fill=(r, g, b, 255))
    mask = Image.new("L", (s, s), 0)
    ImageDraw.Draw(mask).rounded_rectangle([0, 0, s-1, s-1], radius=112, fill=255)
    img.putalpha(mask)
    d = ImageDraw.Draw(img)
    # folder glyph
    d.rounded_rectangle([92, 180, 420, 380], radius=34, fill=(255, 255, 255, 242))
    d.polygon([(92, 190), (92, 150), (210, 150), (246, 186)], fill=(255, 255, 255, 242))
    d.rounded_rectangle([150, 245, 360, 315], radius=18, fill=(99, 102, 241, 255))
    d.rounded_rectangle([150, 330, 300, 350], radius=10, fill=(199, 210, 254, 255))
    if size != s:
        img = img.resize((size, size), Image.LANCZOS)
    return img

specs = [
    ("StoreLogo.png", 50),
    ("Square44x44Logo.png", 44),
    ("Square44x44Logo.scale-200.png", 88),
    ("Square150x150Logo.png", 150),
    ("Square150x150Logo.scale-200.png", 300),
    ("Square310x310Logo.png", 310),
    ("Wide310x150Logo.png", 310),
]
for name, size in specs:
    if name.startswith("Wide"):
        base = logo(150)
        canvas = Image.new("RGBA", (310, 150), (0, 0, 0, 0))
        canvas.alpha_composite(base, (80, 0))
        canvas.save(ROOT / name)
    else:
        logo(size).save(ROOT / name)

splash = Image.new("RGBA", (620, 300), (0, 0, 0, 0))
splash.alpha_composite(logo(150), (235, 75))
splash.save(ROOT / "SplashScreen.png")
print("assets", sorted(p.name for p in ROOT.iterdir()))
