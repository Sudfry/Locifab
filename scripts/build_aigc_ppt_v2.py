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
PALE = RGBColor(0xC7, 0xD2, 0xFE)

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
         align=PP_ALIGN.LEFT, anchor=MSO_ANCHOR.TOP, line=1.15):
    box = slide.shapes.add_textbox(Inches(x), Inches(y), Inches(w), Inches(h))
    tf = box.text_frame
    tf.word_wrap = True
    tf.margin_left = tf.margin_right = tf.margin_top = tf.margin_bottom = 0
    tf.vertical_anchor = anchor
    p = tf.paragraphs[0]
    p.alignment = align
    p.line_spacing = line
    r = p.add_run()
    r.text = value
    font(r, size, bold, color)
    return box

def clean(shp):
    style = shp._element.find(qn('p:style'))
    if style is not None:
        shp._element.remove(style)
    spPr = shp._element.spPr
    for e in spPr.findall(qn('a:effectLst')):
        spPr.remove(e)
    etree.SubElement(spPr, qn('a:effectLst'))
    return shp

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
        shp.line.width = Pt(0.8)
    clean(shp)
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
    clean(shp)
    return shp

def bg(slide, dark=False):
    fill = slide.background.fill
    fill.solid()
    fill.fore_color.rgb = DARK if dark else LIGHT

def label(slide, num, name, dark=False):
    text(slide, 0.72, 0.34, 0.55, 0.25, num, 9.5, True, PALE if dark else VIOLET)
    text(slide, 1.15, 0.34, 7.0, 0.25, name, 9.5, True, PALE if dark else MUTED)

def title(slide, value, sub=None, dark=False, y=0.76):
    text(slide, 0.72, y, 11.9, 0.72, value, 27 if len(value) <= 20 else 23, True,
         WHITE if dark else INK, line=1.06)
    if sub:
        text(slide, 0.74, y + 0.76, 11.4, 0.36, sub, 11.5, False,
             PALE if dark else MUTED, line=1.2)

def footer(slide, page):
    line = slide.shapes.add_connector(1, Inches(0.72), Inches(7.03), Inches(12.6), Inches(7.03))
    line.line.color.rgb = BORDER
    line.line.width = Pt(0.7)
    text(slide, 0.72, 7.10, 5.5, 0.22, "Locifab · 本地文件工坊", 8.3, False, RGBColor(0x9C, 0xA3, 0xAF))
    text(slide, 11.9, 7.10, 0.68, 0.22, f"{page:02d}", 8.3, False, RGBColor(0x9C, 0xA3, 0xAF), PP_ALIGN.RIGHT)

def chip(slide, x, y, w, value, size=9.5, fill=SOFT, color=INDIGO):
    rect(slide, x, y, w, 0.34, fill, None, True)
    text(slide, x + 0.04, y + 0.01, w - 0.08, 0.29, value, size, True, color, PP_ALIGN.CENTER, MSO_ANCHOR.MIDDLE)

def picture_fit(slide, path, x, y, w, h):
    im = Image.open(path)
    iw, ih = im.size
    scale = min(w / iw, h / ih)
    nw, nh = iw * scale, ih * scale
    slide.shapes.add_picture(str(path), Inches(x + (w - nw) / 2), Inches(y + (h - nh) / 2), Inches(nw), Inches(nh))

def note(slide, value):
    slide.notes_slide.notes_text_frame.text = value

def stat(slide, x, y, w, number, label, color=INDIGO):
    rect(slide, x, y, w, 1.14, WHITE, BORDER)
    text(slide, x + 0.2, y + 0.13, w - 0.4, 0.42, number, 22, True, color)
    text(slide, x + 0.2, y + 0.70, w - 0.4, 0.24, label, 8.8, False, MUTED)

# ---------------- 01 cover ----------------
s = prs.slides.add_slide(BLANK); bg(s, True)
circle(s, 9.1, -2.1, 5.5, VIOLET, 0.75)
circle(s, 11.1, 4.8, 3.5, INDIGO, 0.62)
circle(s, -1.7, 5.2, 3.2, RGBColor(0x31, 0x2E, 0x81), 0.55)
rect(s, 0.72, 0.62, 0.52, 0.52, WHITE, None, True, 0.90)
text(s, 0.72, 0.68, 0.52, 0.38, "L", 19, True, INDIGO, PP_ALIGN.CENTER, MSO_ANCHOR.MIDDLE)
text(s, 1.37, 0.67, 6.2, 0.32, "LOCIFAB  ·  AIGC 应用创新大赛", 9.5, True, PALE)
text(s, 0.72, 1.65, 11.4, 0.35, "赛题一 · AI 辅助开发 · 创意产品原型", 11, False, PALE)
text(s, 0.70, 2.08, 11.7, 0.88, "Locifab", 52, True, WHITE, line=0.95)
text(s, 0.72, 2.98, 11.6, 0.55, "本地文件工坊", 24, True, RGBColor(0xE0, 0xE7, 0xFF))
text(s, 0.74, 3.72, 11.0, 0.42, "完全离线的 Windows 文件处理工作台", 16, False, RGBColor(0xDD, 0xD6, 0xFE))
text(s, 0.74, 4.27, 8.0, 0.3, "先预览 · 再执行 · 可回退", 11.5, True, RGBColor(0xA5, 0xB4, 0xFC))
text(s, 0.72, 6.55, 5.5, 0.28, "Sudfry  ·  2026 年 10 月", 9.5, False, PALE)
text(s, 9.2, 6.55, 3.4, 0.28, "项目方案 · 初版", 9.5, False, PALE, PP_ALIGN.RIGHT)
note(s, "开场：这是完全离线的文件工作台，核心是让批量操作可回退。")

# ---------------- 02 agenda ----------------
s = prs.slides.add_slide(BLANK); bg(s)
label(s, "目录", "项目方案")
title(s, "从问题到成果，一条完整的落地路径", "20 页，覆盖创作、技术、设计、验证与团队协作。")
agenda = [
    ("01", "创作理念", "为什么做", "03"),
    ("02", "问题与需求", "真实场景与缺口", "04–05"),
    ("03", "解决方案", "统一流程与能力", "06–07"),
    ("04", "技术路径", "架构与工作流", "08–09"),
    ("05", "设计过程", "从原型到体验", "10–12"),
    ("06", "成果与验证", "产品、界面、测试", "13–16"),
    ("07", "团队与展望", "协作、边界、下一步", "17–20"),
]
for i, (num, head, desc, page) in enumerate(agenda):
    col = i % 3
    row = i // 3
    x = 0.74 + col * 4.02
    y = 1.98 + row * 1.52
    rect(s, x, y, 3.62, 1.18, WHITE, BORDER)
    text(s, x + 0.24, y + 0.16, 0.55, 0.24, num, 9, True, VIOLET)
    text(s, x + 0.24, y + 0.48, 2.35, 0.3, head, 12.5, True, INK)
    text(s, x + 0.24, y + 0.84, 2.9, 0.24, desc, 9, False, MUTED)
    text(s, x + 2.78, y + 0.18, 0.6, 0.24, page, 8.5, False, RGBColor(0xA0, 0xA7, 0xB3), PP_ALIGN.RIGHT)
footer(s, 2)
note(s, "目录：七个部分，20 页。")

# ---------------- 03 concept ----------------
s = prs.slides.add_slide(BLANK); bg(s)
label(s, "01", "创作理念")
title(s, "让文件整理变得可靠，\n而不是更快地冒险", "“不是做功能最多的工具，而是做用户敢用的工具。”")
cards = [
    ("01", "安全优先", "所有批量操作遵循\n“先预览、再执行、可回退”。", INDIGO),
    ("02", "离线可信", "不联网、不上传文件、\n不调用外部 API。", GREEN),
    ("03", "克制实用", "不堆功能，把高频操作\n做顺手、做可靠。", VIOLET),
]
for i, (num, head, body, color) in enumerate(cards):
    x = 0.74 + i * 4.02
    rect(s, x, 2.35, 3.62, 2.72, WHITE, BORDER)
    text(s, x + 0.26, 2.58, 0.55, 0.25, num, 9.5, True, color)
    text(s, x + 0.26, 2.92, 3.0, 0.36, head, 17, True, INK)
    text(s, x + 0.26, 3.46, 3.0, 0.95, body, 10.8, False, MUTED, line=1.45)
    rect(s, x + 0.26, 4.75, 0.52, 0.05, color, None, False)
rect(s, 0.74, 5.62, 11.88, 0.66, SOFT, None)
text(s, 0.96, 5.76, 11.4, 0.34, "“用户不是缺功能，而是缺一套敢用的流程。”", 11.5, True, INDIGO)
footer(s, 3)
note(s, "创作理念：安全、离线、克制。")

# ---------------- 04 problem ----------------
s = prs.slides.add_slide(BLANK); bg(s)
label(s, "02", "问题与需求")
title(s, "文件越来越多，操作却越来越碎", "从真实使用场景出发，先确认问题，再决定做什么。")
pains = [
    ("怕出错", "批量改名、移动后难以恢复，只能手动硬扛。", "回退缺失"),
    ("工具碎", "转换、压缩、去重、校验分散在不同软件。", "流程割裂"),
    ("隐私顾虑", "在线工具需要上传，网络与隐私不可控。", "数据外流"),
    ("部署重", "安装环境、依赖运行库，临时使用不方便。", "门槛偏高"),
]
for i, (head, body, tag) in enumerate(pains):
    x = 0.74 + (i % 2) * 6.02
    y = 2.10 + (i // 2) * 1.95
    rect(s, x, y, 5.65, 1.55, WHITE, BORDER)
    circle(s, x + 0.28, y + 0.30, 0.18, VIOLET if i % 2 == 0 else INDIGO)
    text(s, x + 0.58, y + 0.22, 1.4, 0.3, head, 13, True, INK)
    text(s, x + 0.58, y + 0.62, 4.6, 0.58, body, 9.8, False, MUTED, line=1.35)
    text(s, x + 4.55, y + 0.22, 0.82, 0.22, tag, 7.8, True, VIOLET, PP_ALIGN.RIGHT)
# bottom insight
rect(s, 0.74, 6.02, 11.88, 0.50, RGBColor(0x1E, 0x1B, 0x4B), None)
text(s, 0.96, 6.12, 11.4, 0.28, "结论：用户需要的不是更多工具，而是一套“低门槛 + 可回退”的统一流程。", 9.5, True, WHITE)
footer(s, 4)
note(s, "四个问题：怕出错、工具碎、隐私顾虑、部署重。")

# ---------------- 05 research ----------------
s = prs.slides.add_slide(BLANK); bg(s)
label(s, "02", "调研与洞察")
title(s, "27 份问卷，收敛出四个真实需求", "样本以学生与年轻办公人群为主，覆盖 11 个省市。")
stat(s, 0.74, 1.95, 2.35, "27", "有效问卷")
stat(s, 3.25, 1.95, 2.35, "11", "覆盖省市")
stat(s, 5.76, 1.95, 2.35, "4", "核心缺口")
# keyword panel
rect(s, 8.45, 1.95, 4.16, 1.14, RGBColor(0x1E, 0x1B, 0x4B), None)
text(s, 8.68, 2.08, 3.7, 0.25, "用户关键词", 9, True, PALE)
text(s, 8.68, 2.43, 3.7, 0.35, "省时 · 少步骤 · 结果准", 11.5, True, WHITE)
# insight rows
insights = [
    ("要的不是更多功能", "而是更少步骤、更准的结果。"),
    ("隐私与离线排在前列", "用户要的是数据控制权。"),
    ("安装与卡顿是心理门槛", "“双击即用、体积小”本身就是功能。"),
]
for i, (head, body) in enumerate(insights):
    y = 3.50 + i * 0.83
    text(s, 0.78, y, 0.28, 0.26, "0" + str(i + 1), 8.5, True, VIOLET)
    text(s, 1.18, y, 3.55, 0.28, head, 10.2, True, INK)
    text(s, 4.80, y, 7.5, 0.28, body, 10.2, False, MUTED)
# bottom quote
rect(s, 0.74, 6.30, 11.88, 0.42, SOFT, None)
text(s, 0.96, 6.38, 11.4, 0.28, "“省时、少步骤、结果准”构成产品价值主张的核心三角。", 9.5, True, INDIGO)
footer(s, 5)
note(s, "调研：27 份问卷、11 个省市、4 个缺口；关键词是省时、少步骤、结果准。")

# ---------------- 06 solution ----------------
s = prs.slides.add_slide(BLANK); bg(s)
label(s, "03", "解决方案")
title(s, "一个入口，完成整理、转换与交付", "把“安全”做成底座，再把高频能力组合起来。")
flow = [("01", "预览", "只读规划\n冲突先暴露"), ("02", "执行", "日志先行\n失败可补偿"), ("03", "回退", "逆向恢复\n不限时间")]
for i, (num, head, body) in enumerate(flow):
    x = 0.76 + i * 3.58
    rect(s, x, 2.08, 2.86, 1.84, WHITE, BORDER)
    text(s, x + 0.22, 2.25, 0.5, 0.24, num, 9, True, VIOLET)
    text(s, x + 0.22, 2.58, 2.2, 0.38, head, 18, True, INK)
    text(s, x + 0.22, 3.06, 2.3, 0.62, body, 10, False, MUTED, line=1.35)
    if i < 2:
        text(s, x + 2.92, 2.80, 0.5, 0.3, "→", 20, True, INDIGO)
text(s, 0.76, 4.35, 4.0, 0.3, "能力覆盖", 10.5, True, INK)
caps = ["文件整理", "格式转换", "图片处理", "内容去重", "备份校验", "任务流水线"]
for i, cap in enumerate(caps):
    chip(s, 0.76 + (i % 3) * 2.72, 4.72 + (i // 3) * 0.46, 2.48, cap, 9.3)
rect(s, 9.18, 4.35, 3.44, 1.55, RGBColor(0x1E, 0x1B, 0x4B), None)
text(s, 9.42, 4.52, 2.95, 0.6, "一次配置\n重复使用", 13.5, True, WHITE, line=1.18)
text(s, 9.42, 5.26, 2.9, 0.32, "模板 + 流水线 + 配置快照", 8.7, False, PALE)
footer(s, 6)
note(s, "解决方案：统一入口、统一安全流程、能力可组合。")

# ---------------- 07 safety ----------------
s = prs.slides.add_slide(BLANK); bg(s)
label(s, "03", "核心机制")
title(s, "先预览、再执行、可回退", "每一次批量操作都有计划、日志和退路。")
# mock table
rect(s, 0.74, 2.04, 7.45, 4.10, WHITE, BORDER)
text(s, 0.98, 2.22, 6.6, 0.28, "预览对照表（只读）", 10, True, INK)
headers = ["原名称", "新名称", "状态"]
for i, h in enumerate(headers):
    text(s, 0.98 + [0, 3.02, 5.82][i], 2.66, [2.95, 2.65, 1.3][i], 0.25, h, 8.3, True, MUTED)
rows = [("IMG_001.jpg", "交付_001.jpg", "可执行"), ("IMG_002.jpg", "交付_002.jpg", "冲突"), ("IMG_003.jpg", "交付_003.jpg", "可执行")]
for r, (a, b, c) in enumerate(rows):
    y = 3.08 + r * 0.72
    line = s.shapes.add_connector(1, Inches(0.98), Inches(y + 0.45), Inches(7.62), Inches(y + 0.45))
    line.line.color.rgb = BORDER; line.line.width = Pt(0.6)
    text(s, 0.98, y, 2.85, 0.26, a, 8.8, False, INK)
    text(s, 3.72, y, 2.55, 0.26, b, 8.8, False, INK)
    text(s, 6.54, y, 0.95, 0.26, c, 8.5, True, AMBER if c == "冲突" else GREEN, PP_ALIGN.CENTER)
text(s, 0.98, 5.54, 6.9, 0.28, "冲突项阻断提交；用户调整规则或取消勾选后才可执行。", 8.7, False, MUTED)
# right three points
points = [("日志先行", "执行前落盘，任何中断都留得下证据。"), ("失败补偿", "逐项更新状态，失败时逆序回滚。"), ("随时恢复", "回退中心按批次恢复，不依赖系统撤销。")]
for i, (head, body) in enumerate(points):
    y = 2.12 + i * 1.25
    rect(s, 8.55, y, 4.06, 1.02, WHITE, BORDER)
    text(s, 8.80, y + 0.16, 0.45, 0.22, "0" + str(i + 1), 8, True, VIOLET)
    text(s, 9.30, y + 0.13, 2.9, 0.26, head, 10.3, True, INK)
    text(s, 9.30, y + 0.48, 3.0, 0.38, body, 8.6, False, MUTED, line=1.25)
footer(s, 7)
note(s, "核心机制：预览对照表、日志先行、失败补偿、回退中心。")

# ---------------- 08 tech ----------------
s = prs.slides.add_slide(BLANK); bg(s)
label(s, "04", "技术路径 / 工作流")
title(s, "三层架构，四步工作流", "离线、可测试、可发布：技术选择围绕这三个目标。")
layers = [("App", "WPF 界面 · 导航 · ViewModel", INDIGO), ("Core", "文件 · 日志 · 回退 · 校验", VIOLET), ("Conversion", "图片 · 文档 · PDF · 元数据", RGBColor(0x0E, 0xA5, 0xE9))]
for i, (name, desc, color) in enumerate(layers):
    y = 2.12 + i * 0.92
    rect(s, 0.76, y, 5.10, 0.72, WHITE, BORDER)
    rect(s, 0.76, y, 0.10, 0.72, color, None, False)
    text(s, 1.02, y + 0.12, 1.1, 0.26, name, 11.3, True, INK)
    text(s, 2.00, y + 0.12, 3.55, 0.26, desc, 9.4, False, MUTED)
text(s, 0.76, 5.10, 4.0, 0.26, "技术栈", 9, True, MUTED)
for i, t in enumerate([".NET 8", "WPF", "手写 MVVM", "WIC / OOXML", "单文件发布"]):
    chip(s, 0.76 + (i % 3) * 1.66, 5.44 + (i // 3) * 0.42, 1.53, t, 8.2)
text(s, 6.56, 1.98, 5.6, 0.3, "执行工作流", 10.8, True, INK)
steps = [("选择目录", "源目录 / 输出目录"), ("只读规划", "预览新旧状态与冲突"), ("日志执行", "工作副本 + 逐项记录"), ("回退中心", "按批次逆向恢复")]
for i, (head, desc) in enumerate(steps):
    y = 2.42 + i * 0.86
    circle(s, 6.60, y + 0.08, 0.27, INDIGO)
    text(s, 6.60, y + 0.11, 0.27, 0.16, str(i + 1), 7, True, WHITE, PP_ALIGN.CENTER)
    text(s, 7.04, y, 1.5, 0.26, head, 10.5, True, INK)
    text(s, 8.48, y, 4.0, 0.26, desc, 9.2, False, MUTED)
rect(s, 6.56, 5.92, 5.98, 0.40, SOFT, None)
text(s, 6.74, 5.99, 5.6, 0.24, "源目录不直接改动；失败可补偿；输出与日志可追溯。", 8.8, True, INDIGO)
footer(s, 8)
note(s, "技术路径：三层架构；流水线在源目录工作副本上执行。")

# ---------------- 09 implementation ----------------
s = prs.slides.add_slide(BLANK); bg(s)
label(s, "04", "关键实现")
title(s, "把“安全”写进执行路径", "不是界面提示，而是规划、日志和执行器的共同约束。")
items = [
    ("01", "只读规划", "先计算每个文件的\n旧状态 → 新状态，\n预览阶段零写入。", INDIGO),
    ("02", "日志先行", "执行前生成并落盘\n操作日志，执行中\n逐项更新状态。", VIOLET),
    ("03", "本地转换", "WIC 处理图片，\nOOXML / 本地排版\n生成文档与 PDF。", GREEN),
]
for i, (num, head, body, color) in enumerate(items):
    x = 0.76 + i * 4.02
    rect(s, x, 2.10, 3.66, 2.70, WHITE, BORDER)
    text(s, x + 0.25, 2.30, 0.45, 0.22, num, 8.5, True, color)
    text(s, x + 0.25, 2.65, 3.0, 0.34, head, 15.5, True, INK)
    text(s, x + 0.25, 3.15, 3.0, 0.95, body, 9.8, False, MUTED, line=1.55)
    rect(s, x + 0.25, 4.45, 0.50, 0.05, color, None, False)
# bottom principle
rect(s, 0.76, 5.30, 11.82, 0.78, RGBColor(0x1E, 0x1B, 0x4B), None)
text(s, 0.98, 5.45, 11.2, 0.3, "离线约束：不带浏览器内核、不依赖外部模型、不把用户文件发到网络。", 9.8, True, WHITE)
text(s, 0.98, 5.78, 11.2, 0.22, "发布：自包含单文件 EXE + MSIX；目标设备为 Windows 10 1809 及以上 x64。", 8.5, False, PALE)
footer(s, 9)
note(s, "关键实现：只读规划、日志先行、本地转换；发布单文件与 MSIX。")

# ---------------- 10 design process ----------------
s = prs.slides.add_slide(BLANK); bg(s)
label(s, "05", "设计过程")
title(s, "从真实问题出发，用验收标准驱动迭代", "先定“怎么算完成”，再让 AI 生成实现，团队逐项验证。")
stages = [("01", "调研定位", "27 份问卷\n竞品对照"), ("02", "安全底座", "预览 / 日志\n回退"), ("03", "模块扩展", "转换 / 图片\n去重 / 校验"), ("04", "体验优化", "导航 / 动效\n配置模板"), ("05", "测试发布", "自测 / 流水线\n单文件 / MSIX")]
line = s.shapes.add_connector(1, Inches(1.15), Inches(3.12), Inches(11.95), Inches(3.12))
line.line.color.rgb = PALE; line.line.width = Pt(2.2)
for i, (num, head, desc) in enumerate(stages):
    x = 0.88 + i * 2.45
    circle(s, x, 2.91, 0.42, INDIGO)
    text(s, x, 2.97, 0.42, 0.2, num, 8, True, WHITE, PP_ALIGN.CENTER)
    text(s, x - 0.18, 3.60, 1.95, 0.28, head, 10.5, True, INK, PP_ALIGN.CENTER)
    text(s, x - 0.18, 3.96, 1.95, 0.64, desc, 8.8, False, MUTED, PP_ALIGN.CENTER, line=1.3)
rect(s, 0.76, 5.30, 11.80, 0.72, WHITE, BORDER)
text(s, 0.98, 5.44, 11.3, 0.32, "设计原则：先预览、再执行、可回退；动效可关闭；高 DPI 不裁切；设置只写本地。", 9.8, True, INDIGO)
text(s, 0.76, 6.22, 11.6, 0.28, "保留 Prompt、修改记录与测试输出，保证每一步可追溯。", 9, False, MUTED)
footer(s, 10)
note(s, "设计过程：调研、底座、模块、体验、测试发布。")

# ---------------- 11 design language ----------------
s = prs.slides.add_slide(BLANK); bg(s)
label(s, "05", "设计语言")
title(s, "让状态可见，让操作有退路", "界面不是装饰，而是安全流程的一部分。")
principles = [("预览先行", "所有批量动作先展示对照表，冲突项直接阻断。"), ("状态可见", "离线标识、进度、结果与日志入口常驻。"), ("动效克制", "页面切换与按钮反馈可关闭，不影响效率。"), ("高 DPI 适配", "100%–200% 缩放下不裁切、不重叠。")]
for i, (head, body) in enumerate(principles):
    x = 0.76 + (i % 2) * 5.95
    y = 2.06 + (i // 2) * 1.72
    rect(s, x, y, 5.55, 1.42, WHITE, BORDER)
    text(s, x + 0.24, y + 0.18, 0.45, 0.22, "0" + str(i + 1), 8, True, VIOLET)
    text(s, x + 0.74, y + 0.15, 2.0, 0.28, head, 10.8, True, INK)
    text(s, x + 0.74, y + 0.55, 4.55, 0.52, body, 9.3, False, MUTED, line=1.3)
# design checklist strip
for i, (tag, color) in enumerate([("预览对照", INDIGO), ("冲突阻断", AMBER), ("状态可见", GREEN), ("动效开关", VIOLET), ("高 DPI", RGBColor(0x0E, 0xA5, 0xE9))]):
    chip(s, 0.76 + i * 2.37, 5.60, 2.10, tag, 8.8, WHITE, color)
rect(s, 0.76, 6.20, 11.80, 0.48, SOFT, None)
text(s, 0.98, 6.30, 11.3, 0.26, "设计检查：可预览 · 可恢复 · 可关闭动效 · 高 DPI 不裁切。", 9.2, True, INDIGO)
footer(s, 11)
note(s, "设计语言：预览先行、状态可见、动效克制、高 DPI。")

# ---------------- 12 feature map ----------------
s = prs.slides.add_slide(BLANK); bg(s)
label(s, "05", "功能地图")
title(s, "四组能力，共用一个安全底座", "从整理到交付，常用操作不必再开多个软件。")
groups = [
    ("整理", ["批量重命名", "扫描分类", "备份同步", "压缩解压", "时间属性", "拆分合并"], INDIGO),
    ("内容", ["文本替换", "元数据清理", "内容去重", "相似照片"], VIOLET),
    ("图片", ["图片压缩", "图片检查", "图片批处理", "内嵌图片"], RGBColor(0x0E, 0xA5, 0xE9)),
    ("自动化", ["校验清单", "文件夹清单", "任务流水线", "配置模板"], GREEN),
]
for i, (head, feats, color) in enumerate(groups):
    x = 0.76 + i * 3.06
    rect(s, x, 2.04, 2.78, 4.18, WHITE, BORDER)
    rect(s, x, 2.04, 2.78, 0.08, color, None, False)
    text(s, x + 0.22, 2.30, 2.3, 0.34, head, 13, True, color)
    for j, feat in enumerate(feats):
        text(s, x + 0.22, 2.82 + j * 0.50, 2.3, 0.25, "·  " + feat, 9.3, False, INK)
footer(s, 12)
note(s, "功能地图：整理、内容、图片、自动化。")

# ---------------- 13 innovation ----------------
s = prs.slides.add_slide(BLANK); bg(s)
label(s, "06", "创新点")
title(s, "三个不常见、但真正解决问题的设计", "不是功能叠加，而是把关键环节做深。")
innovations = [
    ("01", "可回退底座", "日志记录操作前后的映射，\n回退不依赖系统撤销，\n覆盖重命名、移动、转换。", INDIGO),
    ("02", "内容相似度", "中文二元组 + SimHash，\n识别改名、换格式后的\n重复文档。", VIOLET),
    ("03", "流水线组合", "把检查、压缩、转换、\n校验、备份组合成模板，\n一键重复执行。", GREEN),
]
for i, (num, head, body, color) in enumerate(innovations):
    x = 0.76 + i * 4.02
    rect(s, x, 2.12, 3.66, 2.82, WHITE, BORDER)
    text(s, x + 0.26, 2.32, 0.5, 0.22, num, 8.5, True, color)
    text(s, x + 0.26, 2.68, 3.0, 0.34, head, 15, True, INK)
    text(s, x + 0.26, 3.20, 3.03, 1.0, body, 9.7, False, MUTED, line=1.5)
rect(s, 0.76, 5.35, 11.82, 0.72, SOFT, None)
text(s, 0.98, 5.48, 11.3, 0.32, "价值：把“批量操作”从有风险的高效工具，变成可以放心用的日常工具。", 9.8, True, INDIGO)
footer(s, 13)
note(s, "创新点：可回退、内容相似度、流水线。")

# ---------------- 14 results ----------------
s = prs.slides.add_slide(BLANK); bg(s)
label(s, "06", "最终成果")
title(s, "一个可运行、可回退、可发布的工具", "Locifab 11.0.3：自包含单文件，覆盖整理、处理、校验与流水线。")
for i, (num, lab) in enumerate([("68.7 MB", "单文件体积"), ("182", "核心自测通过"), ("14", "流水线模板"), ("19", "功能配置")]):
    stat(s, 0.76 + i * 2.96, 1.98, 2.66, num, lab)
text(s, 0.76, 3.52, 4.0, 0.28, "交付能力", 9.5, True, MUTED)
for i, feat in enumerate(["批量重命名与可回退移动", "格式转换、压缩与图片检查", "内容去重与相似照片", "备份、校验、清单与分类", "流水线 / 配置模板 / 回退中心"]):
    y = 3.90 + i * 0.42
    circle(s, 0.80, y + 0.08, 0.12, GREEN)
    text(s, 1.06, y, 5.3, 0.26, feat, 9.8, False, INK)
rect(s, 7.00, 3.50, 5.58, 2.95, RGBColor(0x1E, 0x1B, 0x4B), None)
text(s, 7.24, 3.70, 4.8, 0.3, "交付形态", 11, True, WHITE)
for i, (head, desc) in enumerate([("单文件 EXE", "双击即用，无需安装 .NET"), ("MSIX", "面向 Microsoft Store 发布"), ("源码 + 测试", "434 个文件，可复现、可回归")]):
    y = 4.22 + i * 0.62
    text(s, 7.26, y, 1.5, 0.25, head, 9.2, True, RGBColor(0xA5, 0xB4, 0xFC))
    text(s, 8.82, y, 3.4, 0.25, desc, 8.5, False, RGBColor(0xE0, 0xE7, 0xFF))
footer(s, 14)
note(s, "成果：68.7 MB、182 项自测、14 套模板、19 个配置。")

# ---------------- 15 UI ----------------
s = prs.slides.add_slide(BLANK); bg(s)
label(s, "06", "产品界面")
title(s, "预览、执行、回退，在同一个工作流里", "左侧分类导航，右侧集中操作；所有批量动作先展示预览。")
picture_fit(s, ROOT / "docs/screenshots/v11-home.png", 0.76, 1.98, 5.72, 3.72)
text(s, 0.76, 5.80, 5.72, 0.22, "首页 · 常用功能入口", 8.5, False, MUTED, PP_ALIGN.CENTER)
picture_fit(s, ROOT / "docs/screenshots/v11-pipeline.png", 6.80, 1.98, 5.72, 3.72)
text(s, 6.80, 5.80, 5.72, 0.22, "任务流水线 · 步骤与模板", 8.5, False, MUTED, PP_ALIGN.CENTER)
rect(s, 0.76, 6.20, 11.76, 0.44, SOFT, None)
text(s, 0.96, 6.27, 11.3, 0.26, "统一日志  ·  统一预览  ·  统一回退  ·  不直接修改源目录", 9.2, True, INDIGO)
footer(s, 15)
note(s, "界面展示：首页与流水线。")

# ---------------- 16 test ----------------
s = prs.slides.add_slide(BLANK); bg(s)
label(s, "06", "测试验证")
title(s, "把“看起来能用”变成“确实可用”", "434 个测试文件、19 个配置、14 套流水线，覆盖正常、冲突与失败场景。")
for i, (num, lab, color) in enumerate([("182 / 182", "核心自测", GREEN), ("14 / 14", "流水线模板", INDIGO), ("19 / 19", "配置解析", VIOLET), ("434", "测试文件", AMBER)]):
    stat(s, 0.76 + i * 2.96, 1.98, 2.66, num, lab, color)
text(s, 0.76, 3.50, 5.5, 0.28, "本轮修复", 9.5, True, INK)
for i, fix in enumerate(["坏图导致元数据 / 转换中断：逐文件处理并跳过", "分类与重命名同名冲突：自动追加序号", "流水线导入模板参数丢失：保存步骤快照", "模板首次选择后预览失效：修复命令状态"]):
    y = 3.86 + i * 0.46
    text(s, 0.80, y, 0.3, 0.24, "✓", 9.5, True, GREEN)
    text(s, 1.08, y, 5.4, 0.28, fix, 9.1, False, MUTED)
rect(s, 6.95, 3.50, 5.55, 2.65, WHITE, BORDER)
text(s, 7.18, 3.70, 4.8, 0.28, "质量底线", 10.5, True, INK)
for i, (k, v) in enumerate([("可回退", "有日志、可恢复"), ("可复现", "模板与配置可保存"), ("可审计", "本机操作留痕")]):
    y = 4.18 + i * 0.52
    text(s, 7.20, y, 1.1, 0.26, k, 9.2, True, INDIGO)
    text(s, 8.38, y, 3.7, 0.26, v, 9.2, False, MUTED)
footer(s, 16)
note(s, "测试：182 项自测、14 套流水线、19 个配置、434 个文件。")

# ---------------- 17 AIGC ----------------
s = prs.slides.add_slide(BLANK); bg(s)
label(s, "06", "AIGC 开发方式")
title(s, "AI 负责生成，人负责判断与验收", "协作不是“一键生成”，而是“生成 + 运行 + 核查”的闭环。")
columns = [
    ("AI 做什么", ["拆解需求", "生成界面与业务代码", "整理文档与测试脚本"], RGBColor(0xA5, 0xB4, 0xFC)),
    ("人做什么", ["产品定位与取舍", "技术方案与架构", "验收标准与最终文案"], RGBColor(0x6E, 0xE7, 0xB7)),
    ("如何核查", ["编译与自测", "真实文件运行", "逐项人工复核"], RGBColor(0xFD, 0xE6, 0x8A)),
]
for i, (head, items, color) in enumerate(columns):
    x = 0.76 + i * 4.02
    rect(s, x, 2.06, 3.66, 2.86, WHITE, BORDER)
    text(s, x + 0.26, 2.28, 3.0, 0.30, head, 12.5, True, INK)
    for j, item in enumerate(items):
        text(s, x + 0.26, 2.82 + j * 0.52, 2.95, 0.26, "·  " + item, 9.4, False, MUTED)
    rect(s, x + 0.26, 4.50, 0.48, 0.05, color, None, False)
rect(s, 0.76, 5.28, 11.82, 0.78, RGBColor(0x1E, 0x1B, 0x4B), None)
text(s, 0.98, 5.42, 11.2, 0.28, "作品本体不包含 AI 模型，不使用在线 API 处理用户文件。", 10, True, WHITE)
text(s, 0.98, 5.75, 11.2, 0.22, "关键不是生成速度，而是可验证性。", 8.5, False, PALE)
footer(s, 17)
note(s, "AIGC：AI 生成，人判断与验收；作品本体不含 AI 模型。")

# ---------------- 18 team ----------------
s = prs.slides.add_slide(BLANK); bg(s)
label(s, "07", "团队分工")
title(s, "四个角色，共同对结果负责", "请在各角色“负责人”空位填写姓名；队长负责最终验收。")
headers = ["角色", "负责人", "主要职责"]
rows = [
    ("队长 / 产品", "____________", "调研、定位、优先级、验收"),
    ("交互 / 视觉", "____________", "导航、动效、图标、演示"),
    ("算法 / 核心", "____________", "重命名、日志、回退、去重"),
    ("工程 / 测试", "____________", "转换、图片、流水线、发布与回归"),
]
t = s.shapes.add_table(len(rows) + 1, 3, Inches(0.76), Inches(2.02), Inches(11.8), Inches(3.6)).table
t.columns[0].width = Inches(2.3)
t.columns[1].width = Inches(2.5)
t.columns[2].width = Inches(7.0)
for i, h in enumerate(headers):
    cell = t.cell(0, i)
    cell.text = ""
    r = cell.text_frame.paragraphs[0].add_run(); r.text = h
    font(r, 12, True, WHITE)
    cell.fill.solid(); cell.fill.fore_color.rgb = INDIGO
for row_i, row in enumerate(rows, start=1):
    for col_i, value in enumerate(row):
        cell = t.cell(row_i, col_i)
        cell.text = ""
        r = cell.text_frame.paragraphs[0].add_run(); r.text = value
        font(r, 10.5, col_i == 0, INK if col_i != 1 else VIOLET)
        cell.fill.solid(); cell.fill.fore_color.rgb = WHITE
text(s, 0.78, 5.94, 11.6, 0.28, "协作机制：任务卡片 + Prompt 模板 + 验收清单；每个模块都能追溯到负责人。", 9.2, False, MUTED)
rect(s, 0.78, 6.34, 11.78, 0.36, SOFT, None)
text(s, 0.98, 6.40, 11.3, 0.24, "负责人填写后，建议在答辩时说明每人具体产出与交叉验收方式。", 8.6, True, INDIGO)
footer(s, 18)
note(s, "团队分工：五个角色，负责人空位待填写。")

# ---------------- 19 boundaries ----------------
s = prs.slides.add_slide(BLANK); bg(s)
label(s, "07", "边界与风险")
title(s, "如实说明边界，比掩盖问题更重要", "离线、体积与格式支持之间存在取舍。")
boundaries = [
    ("视频处理", "暂未实现", "不内置视频编码器，避免生成损坏文件。", AMBER),
    ("PDF 文本层", "以视觉还原为主", "内嵌图片仅支持可识别 JPEG 流。", AMBER),
    ("HEIC / RAW", "依赖系统解码器", "缺少解码器时明确失败并清理输出。", INDIGO),
]
for i, (head, status, body, color) in enumerate(boundaries):
    x = 0.76 + i * 4.02
    rect(s, x, 2.10, 3.66, 2.72, WHITE, BORDER)
    text(s, x + 0.24, 2.32, 3.0, 0.28, head, 12.5, True, INK)
    text(s, x + 0.24, 2.72, 3.0, 0.28, status, 9.5, True, color)
    text(s, x + 0.24, 3.18, 3.0, 0.95, body, 9.4, False, MUTED, line=1.45)
text(s, 0.76, 5.32, 4.0, 0.28, "应对原则", 9.5, True, MUTED)
for i, item in enumerate(["不假装支持：缺少能力时明确提示", "不静默失败：清理临时输出并留下原因", "不牺牲安全：可回退始终优先"]):
    text(s, 0.80, 5.72 + i * 0.38, 11.2, 0.26, "·  " + item, 9.4, False, INK)
footer(s, 19)
note(s, "边界：视频、PDF 文本层、HEIC/RAW；如实说明并提供失败提示。")

# ---------------- 20 closing ----------------
s = prs.slides.add_slide(BLANK); bg(s, True)
circle(s, 9.4, -2.1, 5.4, VIOLET, 0.75)
circle(s, -1.6, 5.1, 3.4, INDIGO, 0.60)
text(s, 0.82, 1.00, 8.8, 0.32, "LOCIFAB  /  PROJECT", 8.5, True, PALE)
text(s, 0.80, 1.55, 11.3, 1.45, "让每一次批量操作，\n都有退路。", 33, True, WHITE, line=1.12)
text(s, 0.84, 3.46, 9.8, 0.45, "完全离线 · 可回退 · 可组合 · 可审计", 13, False, PALE)
items = [("01", "视频处理", "离线补齐"), ("02", "PDF 文本层", "增强排版"), ("03", "流水线策略", "重试 / 跳过")]
for i, (num, head, desc) in enumerate(items):
    x = 0.84 + i * 3.55
    rect(s, x, 4.50, 3.05, 1.02, WHITE, None, True, 0.10)
    text(s, x + 0.20, 4.64, 0.5, 0.22, num, 7.5, True, INDIGO)
    text(s, x + 0.56, 4.62, 2.2, 0.26, head, 9.5, True, INK)
    text(s, x + 0.56, 4.98, 2.2, 0.22, desc, 8.0, False, MUTED)
text(s, 0.84, 6.38, 5.6, 0.28, "Sudfry  ·  github.com/Sudfry/Locifab", 9, False, PALE)
text(s, 10.6, 6.38, 1.9, 0.28, "谢谢", 9.5, True, RGBColor(0xA5, 0xB4, 0xFC), PP_ALIGN.RIGHT)
note(s, "收尾：一句话总结价值，带出后续三个方向。")

OUT.parent.mkdir(parents=True, exist_ok=True)
prs.save(OUT)
print("saved", OUT, "slides", len(prs.slides))
