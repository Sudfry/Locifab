from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageFilter
import math

OUT = Path("docs/screenshots/store")
OUT.mkdir(parents=True, exist_ok=True)
YAHEI = "C:/Windows/Fonts/msyh.ttc"
YAHEI_BOLD = "C:/Windows/Fonts/msyhbd.ttc"

def font(size, bold=False):
    return ImageFont.truetype(YAHEI_BOLD if bold else YAHEI, size)

def gradient(size, top=(79, 70, 229), bottom=(91, 33, 182)):
    w, h = size
    img = Image.new("RGB", size)
    d = ImageDraw.Draw(img)
    for y in range(h):
        t = y / max(1, h - 1)
        d.line([(0, y), (w, y)], fill=(
            int(top[0] * (1 - t) + bottom[0] * t),
            int(top[1] * (1 - t) + bottom[1] * t),
            int(top[2] * (1 - t) + bottom[2] * t)))
    return img

def rounded(draw, box, radius, fill, outline=None, width=1):
    draw.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)

def folder_mark(img, cx, cy, size):
    d = ImageDraw.Draw(img)
    w = size
    x0, y0 = cx - w / 2, cy - w * 0.34
    x1, y1 = cx + w / 2, cy + w * 0.36
    # folder tab
    d.rounded_rectangle([x0, y0, x1, y1], radius=int(w * 0.09), fill=(79, 70, 229, 255))
    d.polygon([(x0, y0 + w * 0.08), (x0, y0 - w * 0.10),
               (x0 + w * 0.42, y0 - w * 0.10), (x0 + w * 0.55, y0 + w * 0.08)],
              fill=(79, 70, 229, 255))
    # inner lines
    d.rounded_rectangle([x0 + w * 0.16, cy - w * 0.02, x1 - w * 0.16, cy + w * 0.09],
                        radius=int(w * 0.035), fill=(255, 255, 255, 255))
    d.rounded_rectangle([x0 + w * 0.16, cy + w * 0.15, x1 - w * 0.30, cy + w * 0.22],
                        radius=int(w * 0.025), fill=(199, 210, 254, 255))

def glow(img, cx, cy, r, color=(129, 140, 248), alpha=55):
    layer = Image.new("RGBA", img.size, (0, 0, 0, 0))
    d = ImageDraw.Draw(layer)
    d.ellipse([cx - r, cy - r, cx + r, cy + r], fill=color + (alpha,))
    layer = layer.filter(ImageFilter.GaussianBlur(r // 3))
    img.alpha_composite(layer)

# 1:1 box art 1080
S = 1080
img = gradient((S, S), (67, 56, 202), (109, 40, 217)).convert("RGBA")
glow(img, 180, 160, 420, (129, 140, 248), 65)
glow(img, 920, 960, 460, (192, 38, 211), 55)
d = ImageDraw.Draw(img)
# subtle rings
# app tile
rounded(d, [S/2-190, 160, S/2+190, 540], 92, (255, 255, 255, 22), (255, 255, 255, 70), 3)
folder_mark(img, S/2, 350, 280)
d = ImageDraw.Draw(img)
title = "Locifab"
f = font(112, True)
bbox = d.textbbox((0, 0), title, font=f)
d.text(((S - (bbox[2]-bbox[0]))/2, 600), title, font=f, fill=(255, 255, 255, 255))
sub = "本地文件工坊"
f2 = font(42)
bbox = d.textbbox((0, 0), sub, font=f2)
d.text(((S - (bbox[2]-bbox[0]))/2, 742), sub, font=f2, fill=(224, 231, 255, 245))
line = "离线 · 可回退 · 流水线"
f3 = font(28)
bbox = d.textbbox((0, 0), line, font=f3)
d.text(((S - (bbox[2]-bbox[0]))/2, 824), line, font=f3, fill=(199, 210, 254, 230))
img.convert("RGB").save(OUT / "Locifab-StoreLogo-1080.png", quality=95)

# 2:3 poster 1440x2160 (export 720x1080 too)
W, H = 1440, 2160
img = gradient((W, H), (49, 46, 129), (76, 29, 149)).convert("RGBA")
glow(img, 160, 120, 640, (99, 102, 241), 80)
glow(img, 1320, 1500, 700, (168, 85, 247), 55)
d = ImageDraw.Draw(img)
# top label
rounded(d, [100, 100, 520, 180], 30, (35, 30, 90, 255), (255, 255, 255, 90), 2)
d.text((140, 120), "WINDOWS · 完全离线", font=font(30, True), fill=(255, 255, 255, 255))
# icon tile
rounded(d, [W/2-250, 300, W/2+250, 800], 110, (255, 255, 255, 20), (255, 255, 255, 70), 3)
folder_mark(img, W/2, 535, 360)
d = ImageDraw.Draw(img)
# title
f = font(170, True)
bbox = d.textbbox((0, 0), "Locifab", font=f)
d.text(((W-(bbox[2]-bbox[0]))/2, 900), "Locifab", font=f, fill="white")
f2 = font(58)
sub = "本地文件工坊"
bbox = d.textbbox((0, 0), sub, font=f2)
d.text(((W-(bbox[2]-bbox[0]))/2, 1100), sub, font=f2, fill=(224, 231, 255, 245))
# feature chips
features = ["批量重命名", "格式转换", "图片压缩", "内容去重", "校验备份", "任务流水线"]
f3 = font(36, True)
x, y = 140, 1270
for i, text in enumerate(features):
    tw = d.textbbox((0, 0), text, font=f3)[2]
    cw = tw + 74
    if x + cw > W - 140:
        x = 140; y += 104
    rounded(d, [x, y, x+cw, y+74], 24, (42, 36, 105, 255), (255, 255, 255, 85), 2)
    d.text((x+37, y+18), text, font=f3, fill=(255, 255, 255, 255))
    x += cw + 20
# bottom statement
f4 = font(48, True)
line = "先预览 · 再执行 · 可回退"
bbox = d.textbbox((0, 0), line, font=f4)
d.text(((W-(bbox[2]-bbox[0]))/2, 1710), line, font=f4, fill="white")
f5 = font(30)
line2 = "文件不上传 · 不联网 · 不收集个人数据"
bbox = d.textbbox((0, 0), line2, font=f5)
d.text(((W-(bbox[2]-bbox[0]))/2, 1790), line2, font=f5, fill=(199, 210, 254, 240))
# footer
d.line([(140, 1900), (W-140, 1900)], fill=(255, 255, 255, 45), width=2)
d.text((140, 1930), "Sudfry", font=font(30, True), fill=(224, 231, 255, 230))
d.text((W-140-360, 1930), "github.com/Sudfry/Locifab", font=font(26), fill=(199, 210, 254, 220))
poster = img.convert("RGB")
poster.save(OUT / "Locifab-StorePoster-1440x2160.png", quality=95)
poster.resize((720, 1080), Image.LANCZOS).save(OUT / "Locifab-StorePoster-720x1080.png", quality=95)
print("saved", [p.name for p in OUT.glob("*.png")])
