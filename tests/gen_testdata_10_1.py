from pathlib import Path
from datetime import datetime, timedelta
import random, shutil, json, zipfile
from PIL import Image, ImageDraw, ImageFont
from docx import Document
from docx.shared import Inches
from pptx import Presentation
from pptx.util import Inches as PInches
from openpyxl import Workbook

ROOT = Path("tests/测试数据-10.1.17")
if ROOT.exists():
    shutil.rmtree(ROOT)
ROOT.mkdir(parents=True)
random.seed(20261026)

def write(path: Path, text: str):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")

def image(path: Path, w=640, h=400, color=(80, 110, 220), label="Locifab"):
    path.parent.mkdir(parents=True, exist_ok=True)
    img = Image.new("RGB", (w, h), color)
    d = ImageDraw.Draw(img)
    d.rectangle([20, 20, w-20, h-20], outline=(255,255,255), width=3)
    d.text((40, 40), label, fill=(255,255,255))
    for i in range(30):
        x = random.randrange(20, max(21, w-40)); y = random.randrange(60, max(61, h-40))
        d.ellipse([x, y, x+random.randrange(8, 30), y+random.randrange(8, 30)], fill=(random.randrange(255), random.randrange(255), random.randrange(255)))
    img.save(path, quality=88)

def docx_with_image(path: Path, title: str):
    path.parent.mkdir(parents=True, exist_ok=True)
    doc = Document()
    doc.core_properties.author = "测试作者"
    doc.core_properties.company = "测试公司"
    doc.add_heading(title, 0)
    doc.add_paragraph("这是用于 Locifab 流水线、元数据清理和内嵌图片提取的测试文档。")
    doc.add_paragraph("旧项目名需要被替换；请保留中文标点和段落。")
    tmp = path.parent / "_tmp_img.png"
    image(tmp, 480, 280, label=title[:12])
    doc.add_picture(str(tmp), width=Inches(3.2))
    doc.add_paragraph("结束。")
    doc.save(path)
    tmp.unlink(missing_ok=True)

def pptx_with_images(path: Path, title: str):
    path.parent.mkdir(parents=True, exist_ok=True)
    prs = Presentation()
    slide = prs.slides.add_slide(prs.slide_layouts[5])
    slide.shapes.title.text = title
    tx = slide.shapes.add_textbox(PInches(0.6), PInches(1.3), PInches(5), PInches(1))
    tx.text_frame.text = "用于内嵌图片提取与格式转换测试。"
    for i in range(2):
        tmp = path.parent / f"_tmp_slide_{i}.png"
        image(tmp, 360, 220, label=f"{title} {i+1}", color=(40+i*60, 120, 200))
        slide.shapes.add_picture(str(tmp), PInches(0.7+i*3.2), PInches(2.2), width=PInches(2.8))
        tmp.unlink(missing_ok=True)
    prs.save(path)

def xlsx_demo(path: Path):
    path.parent.mkdir(parents=True, exist_ok=True)
    wb = Workbook()
    ws = wb.active; ws.title = "订单"
    ws.append(["订单号", "客户", "金额", "日期", "备注"])
    for i in range(1, 31):
        ws.append([f"A{i:04d}", f"客户{i%7}", round(random.uniform(10, 999),2), f"2025-{1+i%12:02d}-{1+i%27:02d}", "旧项目名"])
    wb.save(path)

# 00 流水线输入：数量多、结构深
p = ROOT / "00-流水线输入"
for i in range(1, 31):
    write(p / "文档" / f"说明_{i:03d}.txt", f"旧项目名 文档 {i}\n这是第 {i} 个文本。\n重复内容行。\n")
for i in range(1, 21):
    write(p / "文档" / f"记录_{i:03d}.md", f"# 记录 {i}\n\n旧项目名\n\n- 条目 {i}\n")
for i in range(1, 16):
    xlsx_demo(p / "表格" / f"订单_{i:03d}.xlsx")
for i in range(1, 9):
    docx_with_image(p / "文档" / f"报告_{i:03d}.docx", f"项目报告 {i}")
for i in range(1, 5):
    pptx_with_images(p / "文档" / f"汇报_{i:03d}.pptx", f"项目汇报 {i}")
for i in range(1, 31):
    image(p / "图片" / f"照片_{i:03d}.jpg", 800, 520, label=f"照片 {i}")
for i in range(1, 11):
    image(p / "图片" / f"图标_{i:03d}.png", 320, 320, label=f"图标 {i}", color=(30, 160, 120))
# deep tree
for customer in ["客户A", "客户B", "客户C"]:
    for year in ["2024", "2025", "2026"]:
        for month in ["01", "06", "12"]:
            d = p / "深层" / customer / year / month
            write(d / "合同.txt", f"{customer} {year}-{month} 合同\n旧项目名\n")
            if month == "06":
                image(d / "现场.jpg", 600, 400, label=f"{customer}-{year}")
# duplicates exact
(p / "重复").mkdir(parents=True, exist_ok=True)
for i in range(1, 11):
    src = p / "文档" / f"说明_{i:03d}.txt"
    shutil.copy2(src, p / "重复" / f"复制_{i:03d}.txt")
# similar photos groups
for g in range(1, 6):
    base = p / "相似照片" / f"组{g}"
    image(base / "原图.jpg", 800, 520, label=f"组{g}")
    for v in range(1, 4):
        image(base / f"变体{v}.jpg", 800, 520, label=f"组{g} 变体{v}", color=(80+g*20, 100, 200))
# corrupt images
bad = p / "损坏"
bad.mkdir(parents=True, exist_ok=True)
(bad / "截断.jpg").write_bytes(b"\xff\xd8\xff\xe0" + b"0"*500)
(bad / "空.png").write_bytes(b"")
# large-ish files for split/merge
for i in range(1, 4):
    write(p / "大文件" / f"日志_{i}.log", ("2026-10-26 INFO 流水线测试行\n" * 40000))

# 01 rename
p1 = ROOT / "01-重命名"
for i in range(1, 41):
    write(p1 / f"IMG_{i:04d}_旧项目名.txt", f"rename test {i}\n旧项目名\n")

# 02 image compress
p2 = ROOT / "02-图片压缩"
for i in range(1, 21):
    image(p2 / f"comp_{i:03d}.png", 1600, 1000, label=f"compress {i}")

# 03 image check
p3 = ROOT / "03-图片检查"
for i in range(1, 11):
    image(p3 / f"ok_{i:02d}.jpg", 600, 400, label=f"ok {i}")
(p3 / "坏图_01.jpg").write_bytes(b"\xff\xd8\xff\xe0" + b"x"*200)
(p3 / "坏图_02.png").write_bytes(b"\x89PNG\r\n\x1a\n" + b"x"*50)
(p3 / "坏图_03.jpg").write_bytes(b"")
# 04 embedded images
p4 = ROOT / "04-内嵌图片"
docx_with_image(p4 / "带图文档.docx", "带图文档")
pptx_with_images(p4 / "带图演示.pptx", "带图演示")

# 05 archive
p5 = ROOT / "05-压缩解压" / "待压缩"
for i in range(1, 16):
    write(p5 / "资料" / f"文件_{i:02d}.txt", f"archive {i}\n旧项目名\n")
image(p5 / "封面.jpg")
with zipfile.ZipFile(ROOT / "05-压缩解压" / "普通压缩包.zip", "w", zipfile.ZIP_DEFLATED) as z:
    for f in p5.rglob("*"):
        if f.is_file(): z.write(f, f.relative_to(p5))

# 06 metadata
p6 = ROOT / "06-元数据"
docx_with_image(p6 / "隐私文档.docx", "隐私文档")
xlsx_demo(p6 / "隐私表格.xlsx")
for i in range(1, 6):
    image(p6 / f"带EXIF_{i}.jpg", 500, 300, label=f"meta {i}")

# 07 checksum/report
p7 = ROOT / "07-校验清单"
for i in range(1, 41):
    write(p7 / f"目录{i%4}" / f"清单_{i:03d}.txt", f"checksum {i}\n旧项目名\n")

# 08 timestamps
p8 = ROOT / "08-时间属性"
for i in range(1, 21):
    write(p8 / f"属性_{i:02d}.txt", f"time {i}\n")

# 09 split
p9 = ROOT / "09-拆分合并"
write(p9 / "大文件.bin.txt", "0123456789ABCDEF" * 120000)

# 10 dedup
p10 = ROOT / "10-内容去重"
for i in range(1, 11):
    write(p10 / f"原文_{i:02d}.txt", f"完全重复内容 {i}\n" * 5)
    shutil.copy2(p10 / f"原文_{i:02d}.txt", p10 / f"副本_{i:02d}.txt")

# 11 similar photos
p11 = ROOT / "11-相似照片"
for g in range(1, 6):
    for v in range(1, 5):
        image(p11 / f"组{g}_版本{v}.jpg", 900, 600, label=f"G{g} V{v}", color=(60+g*20, 100, 210))

# 12 batch
p12 = ROOT / "12-图片批处理"
for i in range(1, 21):
    image(p12 / f"batch_{i:02d}.png", 1400, 900, label=f"batch {i}")

# 13 text replace
p13 = ROOT / "13-文本替换"
for i in range(1, 21):
    write(p13 / f"replace_{i:02d}.txt", f"旧项目名 需要替换。\n第 {i} 行旧项目名。\n")

# 14 template target
(ROOT / "14-目录模板" / "目标文件夹").mkdir(parents=True)
(ROOT / "14-目录模板" / "目标文件夹" / "已有说明.txt").write_text("已有文件不会被覆盖。", encoding="utf-8")

# 15 empty dirs
p15 = ROOT / "15-空目录清理"
for i in range(1, 6):
    (p15 / f"空目录_{i}").mkdir(parents=True)
    (p15 / f"零字节_{i}.txt").write_bytes(b"")

print("ROOT", ROOT)
print("files", sum(1 for x in ROOT.rglob("*") if x.is_file()))
print("size", sum(x.stat().st_size for x in ROOT.rglob("*") if x.is_file()))
