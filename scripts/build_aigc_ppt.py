from pathlib import Path
from pptx import Presentation
from pptx.util import Inches, Pt
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN, MSO_ANCHOR
from pptx.enum.shapes import MSO_SHAPE
from pptx.oxml.ns import qn
from lxml import etree
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "Document" / "Locifab-项目方案-初版.pptx"

# ---------- palette ----------
INK = RGBColor(0x1F, 0x29, 0x37)
MUTED = RGBColor(0x6B, 0x72, 0x80)
LIGHT = RGBColor(0xF7, 0xF8, 0xFC)
WHITE = RGBColor(0xFF, 0xFF, 0xFF)
INDIGO = RGBColor(0x4F, 0x46, 0xE5)
VIOLET = RGBColor(0x7C, 0x3A, 0xED)
SOFT = RGBColor(0xEE, 0xF2, 0xFF)
BORDER = RGBColor(0xE5, 0xE7, 0xEB)
DARK = RGBColor(0x1E, 0x1B, 0x4B)
GREEN = RGBColor(0x10, 0xB9, 0x81)
AMBER = RGBColor(0xF5, 0x9E, 0x0B)

prs = Presentation()
prs.slide_width = Inches(13.333)
prs.slide_height = Inches(7.5)
BLANK = prs.slide_layouts[6]

FONT = "Microsoft YaHei UI"

def font(run, size=14, bold=False, color=INK, italic=False):
    run.font.name = FONT
    run.font.size = Pt(size)
    run.font.bold = bold
    run.font.italic = italic
    run.font.color.rgb = color
    rPr = run._r.get_or_add_rPr()
    ea = rPr.find(qn('a:ea'))
    if ea is None:
        ea = etree.SubElement(rPr, qn('a:ea'))
    ea.set('typeface', FONT)
    latin = rPr.find(qn('a:latin'))
    if latin is not None:
        latin.set('typeface', FONT)

def text(slide, x, y, w, h, value, size=14, bold=False, color=INK,
         align=PP_ALIGN.LEFT, anchor=MSO_ANCHOR.TOP, line=1.15, margins=(0, 0, 0, 0)):
    box = slide.shapes.add_textbox(Inches(x), Inches(y), Inches(w), Inches(h))
    tf = box.text_frame
    tf.word_wrap = True
    tf.margin_left, tf.margin_right, tf.margin_top, tf.margin_bottom = [Inches(v) for v in margins]
    tf.vertical_anchor = anchor
    p = tf.paragraphs[0]
    p.alignment = align
    p.line_spacing = line
    r = p.add_run()
    r.text = value
    font(r, size, bold, color)
    return box

def rich(slide, x, y, w, h, parts, size=14, color=INK, align=PP_ALIGN.LEFT, line=1.15):
    box = slide.shapes.add_textbox(Inches(x), Inches(y), Inches(w), Inches(h))
    tf = box.text_frame
    tf.word_wrap = True
    tf.margin_left = tf.margin_right = tf.margin_top = tf.margin_bottom = 0
    p = tf.paragraphs[0]
    p.alignment = align
    p.line_spacing = line
    for value, bold, col in parts:
        r = p.add_run()
        r.text = value
        font(r, size, bold, col or color)
    return box

def rect(slide, x, y, w, h, fill=WHITE, line=None, radius=True, transparency=None):
    shp = slide.shapes.add_shape(
        MSO_SHAPE.ROUNDED_RECTANGLE if radius else MSO_SHAPE.RECTANGLE,
        Inches(x), Inches(y), Inches(w), Inches(h))
    if radius:
        try:
            shp.adjustments[0] = 0.10
        except Exception:
            pass
    shp.fill.solid()
    shp.fill.fore_color.rgb = fill
    if transparency is not None:
        solid = shp.fill._xPr.find(qn('a:solidFill'))
        alpha = etree.SubElement(solid, qn('a:alpha'))
        alpha.set('val', str(int(transparency * 100000)))
    if line is None:
        shp.line.fill.background()
    else:
        shp.line.color.rgb = line
        shp.line.width = Pt(0.9)
    shp.shadow.inherit = False
    return shp

def circle(slide, x, y, d, fill, transparency=0):
    shp = slide.shapes.add_shape(MSO_SHAPE.OVAL, Inches(x), Inches(y), Inches(d), Inches(d))
    shp.fill.solid()
    shp.fill.fore_color.rgb = fill
    if transparency:
        solid = shp.fill._xPr.find(qn('a:solidFill'))
        alpha = etree.SubElement(solid, qn('a:alpha'))
        alpha.set('val', str(int(transparency * 100000)))
    shp.line.fill.background()
    shp.shadow.inherit = False
    return shp

def bg(slide, dark=False):
    fill = slide.background.fill
    fill.solid()
    fill.fore_color.rgb = DARK if dark else LIGHT

def section_label(slide, number, name, dark=False):
    text(slide, 0.7, 0.36, 0.55, 0.3, number, 10.5, True, VIOLET if not dark else RGBColor(0xA5, 0xB4, 0xFC))
    text(slide, 1.18, 0.36, 6.0, 0.3, name, 10.5, True, MUTED if not dark else RGBColor(0xC7, 0xD2, 0xFE))

def title(slide, value, sub=None, dark=False, y=0.72):
    text(slide, 0.7, y, 11.9, 0.78, value, 27 if len(value) < 18 else 24, True,
         WHITE if dark else INK, line=1.05)
    if sub:
        text(slide, 0.72, y + 0.82, 11.6, 0.45, sub, 12.5, False,
             RGBColor(0xC7, 0xD2, 0xFE) if dark else MUTED, line=1.2)

def footer(slide, dark=False, page=""):
    color = RGBColor(0x94, 0xA3, 0xB8) if dark else RGBColor(0x9C, 0xA3, 0xAF)
    line = slide.shapes.add_connector(1, Inches(0.7), Inches(7.05), Inches(12.63), Inches(7.05))
    line.line.color.rgb = RGBColor(0x31, 0x2E, 0x81) if dark else BORDER
    line.line.width = Pt(0.7)
    text(slide, 0.7, 7.12, 6.0, 0.22, "Locifab · 本地文件工坊", 8.5, False, color)
    text(slide, 11.85, 7.12, 0.78, 0.22, page, 8.5, False, color, PP_ALIGN.RIGHT)

def chip(slide, x, y, w, value, fill=SOFT, color=INDIGO, size=10.5):
    rect(slide, x, y, w, 0.36, fill, None, radius=True)
    text(slide, x, y + 0.015, w, 0.30, value, size, True, color, PP_ALIGN.CENTER, MSO_ANCHOR.MIDDLE)

def picture_fit(slide, path, x, y, w, h, border=True):
    im = Image.open(path)
    iw, ih = im.size
    scale = min(w / iw, h / ih)
    nw, nh = iw * scale, ih * scale
    px, py = x + (w - nw) / 2, y + (h - nh) / 2
    if border:
        rect(slide, x, y, w, h, WHITE, BORDER, radius=False)
    slide.shapes.add_picture(str(path), Inches(px), Inches(py), Inches(nw), Inches(nh))

def note(slide, value):
    slide.notes_slide.notes_text_frame.text = value

# ---------- 1 cover ----------
s = prs.slides.add_slide(BLANK)
bg(s, True)
circle(s, 9.2, -2.0, 5.4, VIOLET, 0.78)
circle(s, 11.0, 4.7, 3.4, INDIGO, 0.65)
circle(s, -1.6, 5.4, 3.0, RGBColor(0x31, 0x2E, 0x81), 0.55)
# top mark
rect(s, 0.72, 0.63, 0.52, 0.52, RGBColor(0xFF, 0xFF, 0xFF), None, True, 0.90)
text(s, 0.72, 0.68, 0.52, 0.40, "L", 20, True, INDIGO, PP_ALIGN.CENTER, MSO_ANCHOR.MIDDLE)
text(s, 1.38, 0.68, 6.0, 0.36, "LOCIFAB  ·  AIGC 应用创新大赛", 10, True, RGBColor(0xC7, 0xD2, 0xFE))
text(s, 0.72, 1.66, 11.5, 0.42, "赛题一 · AI 辅助开发 · 创意产品原型", 12, False, RGBColor(0xC7, 0xD2, 0xFE))
text(s, 0.70, 2.10, 11.8, 0.95, "Locifab", 54, True, WHITE, line=0.95)
text(s, 0.72, 3.02, 11.8, 0.58, "本地文件工坊", 25, True, RGBColor(0xE0, 0xE7, 0xFF))
text(s, 0.74, 3.78, 11.2, 0.5, "完全离线的 Windows 文件处理工作台", 17, False, RGBColor(0xDD, 0xD6, 0xFE))
text(s, 0.74, 4.44, 9.0, 0.4, "先预览 · 再执行 · 可回退", 12, True, RGBColor(0xA5, 0xB4, 0xFC))
# bottom
text(s, 0.72, 6.62, 5.2, 0.3, "Sudfry  ·  2026 年 10 月", 10, False, RGBColor(0xC7, 0xD2, 0xFE))
text(s, 9.1, 6.62, 3.5, 0.3, "项目方案 · 初版", 10, False, RGBColor(0xC7, 0xD2, 0xFE), PP_ALIGN.RIGHT)
note(s, "开场：这是完全离线的 Windows 文件工作台，核心是让批量操作可回退。")

# ---------- 2 concept ----------
s = prs.slides.add_slide(BLANK); bg(s)
section_label(s, "01", "创作理念")
title(s, "让文件整理变得可靠，\n而不是更快地冒险", "不是做功能最多的工具，而是做用户敢用的工具。")
# three cards
cards = [
    ("01", "安全优先", "所有批量操作遵循“先预览、再执行、可回退”，先写日志再动文件。", INDIGO),
    ("02", "离线可信", "不联网、不上传文件、不调用外部 API，数据始终留在本机。", GREEN),
    ("03", "克制实用", "不堆功能，把重命名、转换、压缩、去重和校验做顺手。", VIOLET),
]
for i, (num, head, body, color) in enumerate(cards):
    x = 0.7 + i * 3.92
    rect(s, x, 2.35, 3.58, 3.15, WHITE, BORDER)
    text(s, x + 0.28, 2.58, 0.6, 0.3, num, 11, True, color)
    text(s, x + 0.28, 2.98, 2.95, 0.4, head, 17, True, INK)
    text(s, x + 0.28, 3.52, 2.95, 1.2, body, 11.5, False, MUTED, line=1.45)
    rect(s, x + 0.28, 4.92, 0.55, 0.05, color, None, radius=False)
# quote strip
rect(s, 0.7, 5.95, 11.93, 0.72, SOFT, None)
text(s, 0.98, 6.10, 11.4, 0.38, "“用户不是缺功能，而是缺一套敢用的流程。”", 12.5, True, INDIGO)
footer(s, page="02")
note(s, "创作理念：安全优先、离线可信、克制实用。")

# ---------- 3 problem ----------
s = prs.slides.add_slide(BLANK); bg(s)
section_label(s, "02", "问题与需求分析")
title(s, "文件越来越多，操作却越来越碎", "27 份问卷、真实文件夹观察与竞品对照，收敛出四个共性问题。")
# left pain cards
pains = [
    ("怕出错", "批量改名、移动后难以恢复，只能手动硬扛。"),
    ("工具碎", "转换、压缩、去重、校验分散在不同软件里。"),
    ("隐私顾虑", "在线工具需要上传，网络和隐私不可控。"),
    ("部署重", "安装环境、依赖运行库，U 盘临时使用不方便。"),
]
for i, (head, body) in enumerate(pains):
    y = 2.22 + i * 1.02
    circle(s, 0.78, y + 0.12, 0.18, VIOLET)
    text(s, 1.08, y, 1.35, 0.3, head, 12.5, True, INK)
    text(s, 2.36, y - 0.01, 3.6, 0.7, body, 10.5, False, MUTED, line=1.3)
# right panel
rect(s, 6.32, 2.08, 6.3, 4.15, WHITE, BORDER)
text(s, 6.62, 2.32, 5.6, 0.35, "需求画像", 14, True, INK)
requirements = [("省时", "一次处理整批文件"), ("放心", "每一步都能恢复"), ("隐私", "完全离线运行"), ("轻量", "双击即用，不装环境")]
for i, (k, v) in enumerate(requirements):
    x = 6.62 + (i % 2) * 2.78
    y = 2.88 + (i // 2) * 1.22
    rect(s, x, y, 2.52, 0.95, SOFT, None, True)
    text(s, x + 0.18, y + 0.12, 0.9, 0.28, k, 11, True, INDIGO)
    text(s, x + 0.18, y + 0.45, 2.18, 0.32, v, 9.5, False, MUTED)
text(s, 6.62, 5.65, 5.55, 0.35, "结论：需要一套“低门槛 + 可回退”的统一流程。", 10.5, True, VIOLET)
footer(s, page="03")
note(s, "问题：怕出错、工具碎、隐私顾虑、部署重；需求是省时、放心、隐私、轻量。")

# ---------- 4 solution ----------
s = prs.slides.add_slide(BLANK); bg(s)
section_label(s, "03", "解决方案")
title(s, "一个入口，完成整理、转换与交付", "把“安全”做成底座，再把高频能力组合起来。")
# flow
flow = [("01", "预览", "只读规划\n冲突先暴露"), ("02", "执行", "日志先行\n失败可补偿"), ("03", "回退", "逆向恢复\n不限时间")]
for i, (num, head, body) in enumerate(flow):
    x = 0.78 + i * 3.55
    rect(s, x, 2.18, 2.85, 2.05, WHITE, BORDER)
    text(s, x + 0.22, 2.36, 0.5, 0.25, num, 10, True, VIOLET)
    text(s, x + 0.22, 2.70, 2.2, 0.42, head, 19, True, INK)
    text(s, x + 0.22, 3.20, 2.3, 0.7, body, 10.5, False, MUTED, line=1.35)
    if i < 2:
        text(s, x + 2.95, 2.92, 0.5, 0.35, "→", 22, True, INDIGO)
# capabilities
text(s, 0.78, 4.62, 4.0, 0.32, "能力覆盖", 12, True, INK)
caps = ["文件整理", "格式转换", "图片处理", "内容去重", "备份校验", "任务流水线"]
for i, cap in enumerate(caps):
    chip(s, 0.78 + (i % 3) * 2.72, 5.02 + (i // 3) * 0.50, 2.48, cap, SOFT, INDIGO, 10)
# right note
rect(s, 9.2, 4.62, 3.42, 1.55, RGBColor(0x1E, 0x1B, 0x4B), None)
text(s, 9.45, 4.82, 2.95, 0.62, "一次配置\n重复使用", 14, True, WHITE, line=1.15)
text(s, 9.45, 5.56, 2.9, 0.35, "模板 + 流水线 + 配置快照", 9.5, False, RGBColor(0xC7, 0xD2, 0xFE))
footer(s, page="04")
note(s, "解决方案：统一入口、统一安全流程、能力可组合。")

# ---------- 5 tech ----------
s = prs.slides.add_slide(BLANK); bg(s)
section_label(s, "04", "技术路径 / 工作流")
title(s, "三层架构，四步工作流", "离线、可测试、可发布：技术选择围绕这三个目标。")
# architecture stack
layers = [("App", "WPF 界面 · 导航 · ViewModel", INDIGO), ("Core", "文件 · 日志 · 回退 · 校验", VIOLET), ("Conversion", "图片 · 文档 · PDF · 元数据", RGBColor(0x0E, 0xA5, 0xE9))]
for i, (name, desc, color) in enumerate(layers):
    y = 2.18 + i * 0.98
    rect(s, 0.78, y, 5.15, 0.78, WHITE, BORDER)
    rect(s, 0.78, y, 0.10, 0.78, color, None, False)
    text(s, 1.04, y + 0.14, 1.1, 0.28, name, 12, True, INK)
    text(s, 2.06, y + 0.14, 3.6, 0.3, desc, 10, False, MUTED)
text(s, 0.78, 5.28, 5.2, 0.28, "技术栈", 9.5, True, MUTED)
for i, t in enumerate([".NET 8", "WPF", "手写 MVVM", "WIC / OOXML", "单文件发布"]):
    chip(s, 0.78 + (i % 3) * 1.68, 5.62 + (i // 3) * 0.46, 1.55, t, SOFT, INDIGO, 8.5)
# workflow right
text(s, 6.58, 2.05, 5.6, 0.32, "执行工作流", 12, True, INK)
steps = [("选择目录", "源目录 / 输出目录"), ("只读规划", "预览新旧状态与冲突"), ("日志执行", "工作副本 + 逐项记录"), ("回退中心", "按批次逆向恢复")]
for i, (head, desc) in enumerate(steps):
    y = 2.50 + i * 0.92
    circle(s, 6.62, y + 0.10, 0.28, INDIGO)
    text(s, 6.62, y + 0.13, 0.28, 0.18, str(i + 1), 7.5, True, WHITE, PP_ALIGN.CENTER)
    text(s, 7.08, y, 1.6, 0.28, head, 11, True, INK)
    text(s, 8.55, y, 4.0, 0.28, desc, 9.8, False, MUTED)
# bottom note
rect(s, 6.58, 6.20, 5.95, 0.42, SOFT, None)
text(s, 6.76, 6.27, 5.6, 0.26, "源目录不直接改动；失败可补偿；输出与日志可追溯。", 9.3, True, INDIGO)
footer(s, page="05")
note(s, "技术路径：三层架构；流水线在源目录工作副本上执行。")

# ---------- 6 design process ----------
s = prs.slides.add_slide(BLANK); bg(s)
section_label(s, "05", "设计过程")
title(s, "从真实问题出发，用验收标准驱动迭代", "先定“怎么算完成”，再让 AI 生成实现，团队逐项验证。")
stages = [
    ("01", "调研定位", "27 份问卷\n竞品对照"),
    ("02", "安全底座", "预览 / 日志\n回退"),
    ("03", "模块扩展", "转换 / 图片\n去重 / 校验"),
    ("04", "体验优化", "导航 / 动效\n配置模板"),
    ("05", "测试发布", "自测 / 流水线\n单文件 / MSIX"),
]
line = s.shapes.add_connector(1, Inches(1.15), Inches(3.18), Inches(11.95), Inches(3.18))
line.line.color.rgb = RGBColor(0xC7, 0xD2, 0xFE)
line.line.width = Pt(2.2)
for i, (num, head, desc) in enumerate(stages):
    x = 0.88 + i * 2.45
    circle(s, x, 2.97, 0.42, INDIGO)
    text(s, x, 3.03, 0.42, 0.2, num, 8, True, WHITE, PP_ALIGN.CENTER)
    text(s, x - 0.18, 3.66, 1.95, 0.3, head, 11, True, INK, PP_ALIGN.CENTER)
    text(s, x - 0.18, 4.04, 1.95, 0.68, desc, 9.2, False, MUTED, PP_ALIGN.CENTER, line=1.3)
# bottom principles
rect(s, 0.78, 5.52, 11.75, 0.72, WHITE, BORDER)
text(s, 1.02, 5.66, 11.3, 0.34, "设计原则：先预览、再执行、可回退；动效可关闭；高 DPI 不裁切；设置只写本地。", 10.3, True, INDIGO)
text(s, 0.78, 6.45, 11.8, 0.3, "过程中保留 Prompt、修改记录与测试输出，保证每一步可追溯。", 9.5, False, MUTED)
footer(s, page="06")
note(s, "设计过程：调研、底座、模块、体验、测试发布；验收标准驱动。")

# ---------- 7 results ----------
s = prs.slides.add_slide(BLANK); bg(s)
section_label(s, "06", "最终成果")
title(s, "一个可运行、可回退、可发布的离线工具", "Locifab 11.0.3：自包含单文件，覆盖整理、处理、校验与流水线。")
stats = [("68.7 MB", "单文件体积"), ("182", "核心自测通过"), ("14", "流水线模板"), ("19", "功能配置")]
for i, (num, label) in enumerate(stats):
    x = 0.78 + i * 2.95
    rect(s, x, 2.14, 2.65, 1.22, WHITE, BORDER)
    text(s, x + 0.22, 2.30, 2.2, 0.48, num, 22, True, INDIGO)
    text(s, x + 0.22, 2.88, 2.2, 0.28, label, 9.5, False, MUTED)
# feature list left
text(s, 0.78, 3.72, 4.0, 0.3, "交付能力", 11, True, MUTED)
features = ["批量重命名与可回退移动", "文档 / 图片格式转换与压缩", "内容去重与相似照片", "备份、校验、清单与分类", "流水线 / 配置模板 / 回退中心"]
for i, feat in enumerate(features):
    y = 4.10 + i * 0.42
    circle(s, 0.82, y + 0.09, 0.12, GREEN)
    text(s, 1.08, y, 5.4, 0.3, feat, 10.3, False, INK)
# delivery card right
rect(s, 6.95, 3.58, 5.55, 2.95, RGBColor(0x1E, 0x1B, 0x4B), None)
text(s, 7.20, 3.78, 4.8, 0.32, "交付形态", 12, True, WHITE)
for i, (head, desc) in enumerate([
    ("单文件 EXE", "双击即用，无需安装 .NET"),
    ("MSIX", "面向 Microsoft Store 发布"),
    ("源码 + 测试", "434 个文件，可复现、可回归"),
]):
    y = 4.30 + i * 0.65
    text(s, 7.22, y, 1.55, 0.27, head, 9.8, True, RGBColor(0xA5, 0xB4, 0xFC))
    text(s, 8.85, y, 3.4, 0.27, desc, 9.2, False, RGBColor(0xE0, 0xE7, 0xFF))
footer(s, page="07")
note(s, "成果：68.7 MB、182 项自测、14 套模板、19 个配置。")

# ---------- 8 UI ----------
s = prs.slides.add_slide(BLANK); bg(s)
section_label(s, "07", "产品界面")
title(s, "预览、执行、回退，在同一个工作流里", "左侧分类导航，右侧集中操作；所有批量动作先展示预览。")
picture_fit(s, ROOT / "docs/screenshots/v11-home.png", 0.78, 2.02, 5.72, 3.75)
text(s, 0.78, 5.86, 5.72, 0.25, "首页 · 常用功能入口", 9, False, MUTED, PP_ALIGN.CENTER)
picture_fit(s, ROOT / "docs/screenshots/v11-pipeline.png", 6.82, 2.02, 5.72, 3.75)
text(s, 6.82, 5.86, 5.72, 0.25, "任务流水线 · 步骤与模板", 9, False, MUTED, PP_ALIGN.CENTER)
rect(s, 0.78, 6.26, 11.76, 0.46, SOFT, None)
text(s, 0.98, 6.34, 11.35, 0.28, "统一日志  ·  统一预览  ·  统一回退  ·  不直接修改源目录", 9.6, True, INDIGO)
footer(s, page="08")
note(s, "界面展示：首页与流水线。强调统一预览、日志与回退。")

# ---------- 9 test ----------
s = prs.slides.add_slide(BLANK); bg(s)
section_label(s, "08", "测试验证")
title(s, "把“看起来能用”变成“确实可用”", "434 个测试文件、19 个配置、14 套流水线，覆盖正常、冲突与失败场景。")
# test stats
tests = [("182 / 182", "核心自测", GREEN), ("14 / 14", "流水线模板", INDIGO), ("19 / 19", "配置解析", VIOLET), ("434", "测试文件", AMBER)]
for i, (num, label, color) in enumerate(tests):
    x = 0.78 + i * 2.96
    rect(s, x, 2.12, 2.66, 1.15, WHITE, BORDER)
    text(s, x + 0.2, 2.27, 2.3, 0.4, num, 18, True, color)
    text(s, x + 0.2, 2.74, 2.2, 0.26, label, 9.2, False, MUTED)
# fixes
text(s, 0.78, 3.68, 5.5, 0.3, "本轮修复", 11, True, INK)
fixes = ["坏图导致元数据 / 转换中断：逐文件处理并跳过", "分类与重命名同名冲突：自动追加序号", "流水线导入模板参数丢失：保存步骤快照", "模板首次选择后预览失效：修复命令状态"]
for i, fix in enumerate(fixes):
    y = 4.04 + i * 0.46
    text(s, 0.82, y, 0.3, 0.25, "✓", 10, True, GREEN)
    text(s, 1.12, y, 5.55, 0.34, fix, 9.8, False, MUTED)
# quality panel
rect(s, 6.95, 3.60, 5.55, 2.5, WHITE, BORDER)
text(s, 7.2, 3.82, 4.9, 0.3, "质量底线", 12, True, INK)
for i, (k, v) in enumerate([("可回退", "有日志、可恢复"), ("可复现", "模板与配置可保存"), ("可审计", "本机操作留痕")]):
    y = 4.28 + i * 0.54
    text(s, 7.22, y, 1.2, 0.3, k, 10, True, INDIGO)
    text(s, 8.50, y, 3.7, 0.3, v, 9.8, False, MUTED)
footer(s, page="09")
note(s, "测试：182 项自测、14 套流水线、19 个配置、434 个文件；修复坏图与同名冲突。")

# ---------- 10 team ----------
s = prs.slides.add_slide(BLANK); bg(s)
section_label(s, "09", "团队协作")
title(s, "团队定义问题，AI 负责实现，人负责验收", "AIGC 协作不是“一键生成”，而是“生成 + 运行 + 核查”的闭环。")
# roles
text(s, 0.78, 2.05, 4.0, 0.3, "团队分工", 11, True, INK)
roles = [("产品 / 队长", "调研、定位、优先级、验收"), ("交互 / 视觉", "导航、动效、图标、演示"), ("算法 / 核心", "重命名、日志、回退、去重"), ("工程 / 测试", "转换、图片、流水线、发布")]
for i, (head, desc) in enumerate(roles):
    y = 2.44 + i * 0.92
    rect(s, 0.78, y, 5.55, 0.76, WHITE, BORDER)
    circle(s, 0.98, y + 0.22, 0.16, INDIGO if i % 2 == 0 else VIOLET)
    text(s, 1.26, y + 0.13, 1.55, 0.28, head, 9.5, True, INK)
    text(s, 2.80, y + 0.13, 3.3, 0.28, desc, 8.7, False, MUTED)
# AI panel
rect(s, 6.82, 2.02, 5.72, 3.85, RGBColor(0x1E, 0x1B, 0x4B), None)
text(s, 7.10, 2.24, 5.1, 0.3, "AIGC 协作方式", 12, True, WHITE)
text(s, 7.10, 2.65, 5.1, 0.58, "AI：拆解、生成、整理", 10.5, True, RGBColor(0xA5, 0xB4, 0xFC))
text(s, 7.10, 3.12, 5.1, 0.58, "人：判断、决策、验收", 10.5, True, RGBColor(0x6E, 0xE7, 0xB7))
text(s, 7.10, 3.70, 5.05, 1.55, "需求 → Prompt → 生成\n→ 运行测试 → 人工核查 → 合并\n\n作品本体不包含 AI 模型，\n不使用在线 API 处理用户文件。", 9.3, False, RGBColor(0xE0, 0xE7, 0xFF), line=1.45)
text(s, 6.82, 6.16, 5.7, 0.3, "关键不是生成速度，而是可验证性。", 10, True, INDIGO)
footer(s, page="10")
note(s, "团队分工与 AIGC 协作：AI 生成，人验收；作品本体不含 AI 模型。")

# ---------- 11 closing ----------
s = prs.slides.add_slide(BLANK); bg(s, True)
circle(s, 9.5, -2.2, 5.5, VIOLET, 0.75)
circle(s, -1.5, 5.2, 3.5, INDIGO, 0.60)
text(s, 0.82, 1.02, 8.8, 0.35, "LOCIFAB  /  PROJECT", 9, True, RGBColor(0xC7, 0xD2, 0xFE))
text(s, 0.80, 1.62, 11.4, 1.5, "让每一次批量操作，\n都有退路。", 34, True, WHITE, line=1.12)
text(s, 0.84, 3.55, 9.8, 0.5, "完全离线 · 可回退 · 可组合 · 可审计", 14, False, RGBColor(0xC7, 0xD2, 0xFE))
# three next items
items = [("01", "视频处理", "离线补齐"), ("02", "PDF 文本层", "增强排版"), ("03", "流水线策略", "重试 / 跳过")]
for i, (num, head, desc) in enumerate(items):
    x = 0.84 + i * 3.55
    rect(s, x, 4.55, 3.05, 1.05, RGBColor(0xFF, 0xFF, 0xFF), None, True, 0.10)
    text(s, x + 0.20, 4.68, 0.5, 0.22, num, 8, True, INDIGO)
    text(s, x + 0.58, 4.66, 2.2, 0.28, head, 10, True, INK)
    text(s, x + 0.58, 5.02, 2.2, 0.25, desc, 8.3, False, MUTED)
text(s, 0.84, 6.42, 5.6, 0.3, "Sudfry  ·  github.com/Sudfry/Locifab", 9.5, False, RGBColor(0xC7, 0xD2, 0xFE))
text(s, 10.6, 6.42, 1.9, 0.3, "谢谢", 10, True, RGBColor(0xA5, 0xB4, 0xFC), PP_ALIGN.RIGHT)
note(s, "收尾：一句话总结价值，带出后续三个方向。")

OUT.parent.mkdir(parents=True, exist_ok=True)
prs.save(OUT)
print("saved", OUT, "slides", len(prs.slides))
