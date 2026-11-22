from pathlib import Path
from datetime import datetime
import random, shutil, zipfile, json
from PIL import Image, ImageDraw
from docx import Document
from openpyxl import Workbook
from pptx import Presentation
from pptx.util import Inches, Pt

ROOT = Path("视频演示数据-11.0.3")
if ROOT.exists():
    shutil.rmtree(ROOT)
ROOT.mkdir(parents=True)
random.seed(20261027)

def write(path: Path, text: str):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")

def image(path: Path, w=1400, h=900, label="", hue=0):
    path.parent.mkdir(parents=True, exist_ok=True)
    img = Image.new("RGB", (w, h))
    d = ImageDraw.Draw(img)
    # soft two-color gradient
    for y in range(h):
        t = y / max(1, h - 1)
        d.line([(0, y), (w, y)], fill=(
            int(40 + 80 * t + hue * 8) % 255,
            int(80 + 100 * t) % 255,
            int(170 + 50 * (1 - t)) % 255))
    for _ in range(80):
        x, y = random.randrange(w), random.randrange(h)
        r = random.randrange(20, min(160, w // 5, h // 5))
        d.ellipse([x - r, y - r, x + r, y + r],
                  fill=(random.randrange(255), random.randrange(255), random.randrange(255)))
    d.rectangle([20, 20, w - 20, h - 20], outline=(255, 255, 255), width=max(2, w // 400))
    if label:
        d.text((40, 40), label, fill=(255, 255, 255))
    img.save(path, quality=88, optimize=True)

def docx(path: Path, title: str):
    path.parent.mkdir(parents=True, exist_ok=True)
    doc = Document()
    doc.core_properties.author = "旧项目名"
    doc.add_heading(title, 0)
    doc.add_paragraph("这是视频演示用的项目资料，包含旧项目名与待整理内容。")
    doc.add_paragraph("交付前需要统一命名、清理元数据并生成清单。")
    for i in range(3):
        doc.add_paragraph(f"第 {i+1} 条说明：所有批量操作都应当先预览、再执行、可回退。")
    doc.save(path)

def xlsx(path: Path):
    path.parent.mkdir(parents=True, exist_ok=True)
    wb = Workbook()
    ws = wb.active
    ws.title = "项目清单"
    ws.append(["编号", "客户", "状态", "备注"])
    for i in range(1, 21):
        ws.append([f"A{i:03d}", f"客户{i%5+1}", "待交付", "旧项目名"])
    wb.save(path)

def pptx(path: Path, title: str):
    path.parent.mkdir(parents=True, exist_ok=True)
    prs = Presentation()
    slide = prs.slides.add_slide(prs.slide_layouts[5])
    slide.shapes.title.text = title
    slide.shapes.add_textbox(Inches(0.7), Inches(1.4), Inches(6), Inches(1.2)).text_frame.text = "旧项目名素材，待整理与交付。"
    slide2 = prs.slides.add_slide(prs.slide_layouts[5])
    slide2.shapes.title.text = "交付说明"
    slide2.shapes.add_textbox(Inches(0.7), Inches(1.4), Inches(7), Inches(1.2)).text_frame.text = "统一命名 · 压缩图片 · 生成清单 · 打包交付。"
    prs.save(path)

main = ROOT / "00-流水线-混乱素材"
# 120 normal photos
for i in range(1, 121):
    image(main / "图片" / f"IMG_2024_{i:04d}_旧项目名.jpg", label=f"IMG {i}")
# 24 large photos for compression
for i in range(1, 25):
    image(main / "高清图" / f"高清_{i:03d}_旧项目名.jpg", 3000, 2000, f"HD {i}", hue=i)
# 60 phone photos, 6 similar groups of 4 and 36 random
for g in range(1, 7):
    base = main / "手机照片" / f"相似组{g}"
    image(base / "原图.jpg", 1600, 1200, f"组{g}", hue=g)
    for v in range(1, 4):
        image(base / f"变体{v}.jpg", 1600, 1200, f"组{g}-{v}", hue=g + v)
for i in range(1, 37):
    image(main / "手机照片" / f"微信图片_{i:03d}.jpg", 1600, 1200, f"微信 {i}", hue=i)
# 40 screenshots
for i in range(1, 41):
    image(main / "截图" / f"截图_{i:03d}.png", 1000, 700, f"截图 {i}", hue=i)
# 140 text-ish files
for i in range(1, 81):
    write(main / "文档" / f"新建文本文档 ({i}).txt", f"旧项目名 文档 {i}\n待整理内容。\n")
for i in range(1, 41):
    write(main / "文档" / f"会议记录_{i:03d}.md", f"# 会议记录 {i}\n\n旧项目名\n\n- 待办 {i}\n")
for i in range(1, 21):
    write(main / "文档" / f"数据_{i:03d}.csv", "编号,内容\n1,旧项目名\n2,待整理\n")
# 70 office files
for i in range(1, 41):
    docx(main / "办公" / f"项目报告_{i:03d}.docx", f"项目报告 {i}")
for i in range(1, 21):
    xlsx(main / "办公" / f"项目清单_{i:03d}.xlsx")
for i in range(1, 11):
    pptx(main / "办公" / f"项目汇报_{i:03d}.pptx", f"项目汇报 {i}")
# exact duplicates
(main / "重复").mkdir(parents=True, exist_ok=True)
for i in range(1, 31):
    src = main / "文档" / f"新建文本文档 ({i}).txt"
    shutil.copy2(src, main / "重复" / f"副本_{i:03d}.txt")
# deep customer tree
for c in ["客户A", "客户B", "客户C"]:
    for y in ["2024", "2025", "2026"]:
        for m in ["01", "04", "07", "10"]:
            d = main / "客户资料" / c / y / m
            write(d / "合同.txt", f"{c} {y}-{m} 合同\n旧项目名\n")
            write(d / "交付说明.md", f"# {c} {y}-{m}\n\n旧项目名\n")
            image(d / "现场.jpg", 1200, 800, f"{c}-{y}")
# corrupt images
(main / "损坏").mkdir(parents=True, exist_ok=True)
for i in range(1, 9):
    (main / "损坏" / f"坏图_{i:02d}.jpg").write_bytes(b"\xff\xd8\xff\xe0" + b"x" * 200)
# output folder
(ROOT / "01-交付输出").mkdir(parents=True, exist_ok=True)
write(ROOT / "01-交付输出" / "说明.txt", "这是流水线输出目录，执行前可为空。\n")

# extra focused folders
extra = ROOT / "02-单功能演示"
for i in range(1, 21):
    image(extra / "图片压缩" / f"comp_{i:02d}.jpg", 2200, 1600, f"comp {i}", hue=i)
for i in range(1, 11):
    write(extra / "内容去重" / f"重复_{i:02d}.txt", f"完全重复的内容 {i}\n" * 4)
for i in range(1, 11):
    write(extra / "内容去重" / f"副本_{i:02d}.txt", f"完全重复的内容 {i}\n" * 4)
for i in range(1, 6):
    image(extra / "损坏图片" / f"坏图_{i}.jpg", 800, 600, "bad")
    (extra / "损坏图片" / f"坏图_{i}.jpg").write_bytes(b"\xff\xd8\xff\xe0" + b"x" * 100)

# templates
def step(kind, text1="", text2="", text3="", n1=0, n2=0, choice="", b1=False, b2=False, template=""):
    return {"kind": kind, "name": kind, "enabled": True, "text1": text1, "text2": text2,
            "text3": text3, "number1": n1, "number2": n2, "choice": choice,
            "bool1": b1, "bool2": b2, "templateName": template}

pipelines = [
    {"name": "交付整理-全流程", "sourceFolder": "", "outputFolder": "", "steps": [
        step("Classify", choice="ByType"),
        step("Rename", text1="交付_", text3="_", n1=1, n2=3, b1=True),
        step("ImageCheck", text1="损坏图片隔离"),
        step("ImageTarget", text1="压缩图片", n1=500),
        step("Report", text1="交付清单.csv", choice="Csv", b1=True),
        step("Cleanup", b1=True, b2=True),
        step("Archive", text1="交付包.zip", choice="Compress", b1=True),
    ]},
    {"name": "文档隐私交付", "sourceFolder": "", "outputFolder": "", "steps": [
        step("Metadata", text1="去元数据"),
        step("Convert", text1="PDF", choice="pdf"),
        step("Checksum", text1="Locifab-SHA256.txt", choice="SHA-256"),
        step("Report", text1="文档清单.csv", choice="Csv", b1=True),
    ]},
    {"name": "客户资料归档", "sourceFolder": "", "outputFolder": "", "steps": [
        step("Template", text1="客户/|合同/|交付物/|验收/|交付说明.md"),
        step("Classify", choice="ByYear"),
        step("Rename", text1="归档_", text3="_", n1=1, n2=3, b1=True),
        step("Report", text1="归档清单.csv", choice="Csv", b1=True),
    ]},
]
(ROOT / "05-模板与说明").mkdir(parents=True, exist_ok=True)
(ROOT / "05-模板与说明" / "流水线模板.json").write_text(json.dumps(pipelines, ensure_ascii=False, indent=2), encoding="utf-8")

# quick tool presets for import demo
tool_presets = [
    {"toolKey": "rename", "name": "交付前缀", "values": {"Prefix": "交付_", "Suffix": "", "StartNumberText": "1", "NumberPaddingText": "3", "NumberSeparator": "_", "UseNumbering": "True"},
     "step": step("Rename", text1="交付_", text3="_", n1=1, n2=3, b1=True)},
    {"toolKey": "imgcompress", "name": "图片500KB", "values": {"TargetKb": "500"},
     "step": step("ImageTarget", text1="压缩图片", n1=500)},
    {"toolKey": "report", "name": "清单CSV", "values": {"IncludeHash": "True"},
     "step": step("Report", text1="交付清单.csv", choice="Csv", b1=True)},
]
(ROOT / "05-模板与说明" / "功能配置模板.json").write_text(json.dumps(tool_presets, ensure_ascii=False, indent=2), encoding="utf-8")

readme = """# Locifab 视频演示数据

## 数据规模
- 主素材目录：约 560+ 个文件
- 包含：普通图片、高清图片、手机照片、截图、文档、表格、演示文稿、重复文件、相似照片、损坏图片、深层客户目录
- 体积：约 40–60 MB

## 视频建议（5 分钟）
1. 0:00–0:30 打开 Locifab，介绍完全离线。
2. 0:30–1:20 打开 `00-流水线-混乱素材`，展示文件数量和混乱目录。
3. 1:20–2:10 打开任务流水线，选择 `交付整理-全流程`，展示 6 个步骤和预览。
4. 2:10–3:50 执行流水线，展示进度和输出。
5. 3:50–4:30 打开输出目录，展示分类、重命名、压缩图、清单、ZIP；回到回退中心演示恢复。
6. 4:30–5:00 收尾，展示设置/作者/GitHub。

## 模板
- 交付整理-全流程：分类、重命名、图片检查、图片压缩、清单、ZIP
- 文档隐私交付：元数据、PDF、校验、清单
- 客户资料归档：目录模板、按年分类、重命名、清单

## 导入
- 流水线模板：复制到 Locifab 数据目录的 `pipeline-presets.json`，或在流水线页面导入。
- 功能配置：通过顶部“配置 → 导入”导入 `功能配置模板.json`。

## 录制建议
- 分辨率 1920×1080，100% 缩放。
- 先关闭无关窗口，避免通知弹窗。
- 流水线执行时间较长时可适当剪辑等待段，保留进度变化。
"""
(ROOT / "05-模板与说明" / "视频拍摄说明.md").write_text(readme, encoding="utf-8")

# zip
zip_path = ROOT.parent / "Locifab-视频演示数据-11.0.3.zip"
if zip_path.exists():
    zip_path.unlink()
with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as z:
    for f in ROOT.rglob("*"):
        if f.is_file():
            z.write(f, f.relative_to(ROOT.parent))

files = sum(1 for f in ROOT.rglob("*") if f.is_file())
size = sum(f.stat().st_size for f in ROOT.rglob("*") if f.is_file())
print("ROOT", ROOT)
print("FILES", files, "SIZE_MB", round(size / 1024 / 1024, 1))
print("ZIP", zip_path, round(zip_path.stat().st_size / 1024 / 1024, 1))
