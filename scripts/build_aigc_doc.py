from pathlib import Path
import shutil
from docx import Document
from docx.shared import Pt, Cm, Inches, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH, WD_LINE_SPACING
from docx.enum.table import WD_TABLE_ALIGNMENT
from docx.enum.section import WD_SECTION
from docx.oxml import OxmlElement
from docx.oxml.ns import qn

ROOT = Path(__file__).resolve().parents[1]
DOCX = ROOT / "Document" / "main.docx"
if DOCX.exists():
    shutil.copy2(DOCX, ROOT / "Document" / "main_详细版备份.docx")

doc = Document()
section = doc.sections[0]
section.page_width = Cm(21)
section.page_height = Cm(29.7)
section.top_margin = Cm(2.2)
section.bottom_margin = Cm(2.2)
section.left_margin = Cm(2.4)
section.right_margin = Cm(2.2)

# ---------- styles ----------
def set_east_asia(style_or_run, name="微软雅黑"):
    rpr = style_or_run._element.get_or_add_rPr()
    fonts = rpr.find(qn('w:rFonts'))
    if fonts is None:
        fonts = OxmlElement('w:rFonts')
        rpr.append(fonts)
    fonts.set(qn('w:eastAsia'), name)
    fonts.set(qn('w:ascii'), name)
    fonts.set(qn('w:hAnsi'), name)

normal = doc.styles["Normal"]
normal.font.name = "微软雅黑"
normal.font.size = Pt(10.5)
normal.font.color.rgb = RGBColor(0x22, 0x22, 0x22)
normal.paragraph_format.line_spacing = 1.45
normal.paragraph_format.space_after = Pt(4)
set_east_asia(normal)

for name, size, color, before, after in [
    ("Heading 1", 16, RGBColor(0x31, 0x2E, 0x81), 14, 8),
    ("Heading 2", 12.5, RGBColor(0x43, 0x3C, 0xA8), 10, 5),
]:
    st = doc.styles[name]
    st.font.name = "微软雅黑"
    st.font.size = Pt(size)
    st.font.bold = True
    st.font.color.rgb = color
    st.paragraph_format.space_before = Pt(before)
    st.paragraph_format.space_after = Pt(after)
    set_east_asia(st)

def p(text="", size=10.5, bold=False, color=None, align=None, after=4, before=0, line=1.45, indent=True):
    para = doc.add_paragraph()
    para.paragraph_format.space_after = Pt(after)
    para.paragraph_format.space_before = Pt(before)
    para.paragraph_format.line_spacing = line
    if indent:
        para.paragraph_format.first_line_indent = Pt(21)
    if align is not None:
        para.alignment = align
    r = para.add_run(text)
    r.font.name = "微软雅黑"
    r.font.size = Pt(size)
    r.bold = bold
    if color:
        r.font.color.rgb = color
    set_east_asia(r)
    return para

def bullet(text):
    para = doc.add_paragraph(style="List Bullet")
    para.paragraph_format.space_after = Pt(2)
    para.paragraph_format.line_spacing = 1.35
    r = para.add_run(text)
    r.font.name = "微软雅黑"
    r.font.size = Pt(10.5)
    set_east_asia(r)
    return para

def shade(cell, color):
    tcPr = cell._tc.get_or_add_tcPr()
    shd = OxmlElement('w:shd')
    shd.set(qn('w:fill'), color)
    tcPr.append(shd)

def table(headers, rows, widths=None):
    t = doc.add_table(rows=1, cols=len(headers))
    t.style = "Light Grid Accent 1"
    t.alignment = WD_TABLE_ALIGNMENT.CENTER
    hdr = t.rows[0].cells
    for i, h in enumerate(headers):
        hdr[i].text = ""
        para = hdr[i].paragraphs[0]
        r = para.add_run(h)
        r.font.name = "微软雅黑"
        r.font.size = Pt(9.5)
        r.bold = True
        r.font.color.rgb = RGBColor(0xFF, 0xFF, 0xFF)
        set_east_asia(r)
        shade(hdr[i], "433CA8")
    for row in rows:
        cells = t.add_row().cells
        for i, val in enumerate(row):
            cells[i].text = ""
            para = cells[i].paragraphs[0]
            r = para.add_run(str(val))
            r.font.name = "微软雅黑"
            r.font.size = Pt(9.2)
            set_east_asia(r)
    if widths:
        for row in t.rows:
            for i, width in enumerate(widths):
                row.cells[i].width = Cm(width)
    doc.add_paragraph().paragraph_format.space_after = Pt(2)
    return t

def add_page_number(section):
    footer = section.footer
    para = footer.paragraphs[0]
    para.alignment = WD_ALIGN_PARAGRAPH.CENTER
    run = para.add_run()
    fldChar = OxmlElement('w:fldChar'); fldChar.set(qn('w:fldCharType'), 'begin')
    instrText = OxmlElement('w:instrText'); instrText.text = 'PAGE'
    fldEnd = OxmlElement('w:fldChar'); fldEnd.set(qn('w:fldCharType'), 'end')
    run._r.append(fldChar); run._r.append(instrText); run._r.append(fldEnd)
    run.font.size = Pt(9)
    run.font.color.rgb = RGBColor(0x88, 0x88, 0x88)

add_page_number(section)
header = section.header.paragraphs[0]
header.alignment = WD_ALIGN_PARAGRAPH.RIGHT
h = header.add_run("Locifab · 项目文档")
h.font.size = Pt(8.5)
h.font.color.rgb = RGBColor(0x99, 0x99, 0x99)
set_east_asia(h)

# ---------- cover ----------
p("杭州电子科技大学第二届 AIGC 应用创新大赛", 11.5, True, RGBColor(0x43, 0x3C, 0xA8), WD_ALIGN_PARAGRAPH.CENTER, 8, 8, 1.2, False)
p("赛题一 · AI 辅助开发 · 创意产品原型", 9.5, False, RGBColor(0x66, 0x66, 0x66), WD_ALIGN_PARAGRAPH.CENTER, 30, 0, 1.2, False)
p("Locifab", 30, True, RGBColor(0x31, 0x2E, 0x81), WD_ALIGN_PARAGRAPH.CENTER, 4, 30, 1.0, False)
p("本地文件工坊", 18, True, RGBColor(0x4C, 0x45, 0xB8), WD_ALIGN_PARAGRAPH.CENTER, 4, 0, 1.1, False)
p("完全离线的 Windows 文件处理工作台", 11.5, False, RGBColor(0x55, 0x55, 0x55), WD_ALIGN_PARAGRAPH.CENTER, 20, 4, 1.2, False)
p("先预览 · 再执行 · 可回退", 11, False, RGBColor(0x7C, 0x3A, 0xED), WD_ALIGN_PARAGRAPH.CENTER, 70, 0, 1.2, False)

cover = doc.add_table(rows=4, cols=2)
cover.style = "Table Grid"
cover.alignment = WD_TABLE_ALIGNMENT.CENTER
for i, (k, v) in enumerate([("参赛团队", "（请填写队名）"), ("团队成员", "（请填写姓名 / 学号）"), ("作品版本", "Locifab 11.0.3"), ("完成日期", "2026 年 10 月")]):
    cover.rows[i].cells[0].text = k
    cover.rows[i].cells[1].text = v
    for j in range(2):
        shade(cover.rows[i].cells[j], "F3F2FF" if j == 0 else "FFFFFF")
        for para in cover.rows[i].cells[j].paragraphs:
            for r in para.runs:
                r.font.name = "微软雅黑"; r.font.size = Pt(10); set_east_asia(r)
cover.columns[0].width = Cm(3.6)
cover.columns[1].width = Cm(8.5)

doc.add_page_break()

# ---------- abstract ----------
doc.add_heading("摘　要", level=1)
p("Locifab（本地文件工坊）是一款完全离线的 Windows 桌面文件处理工具，面向学生、年轻办公人群和小型团队。它把批量重命名、格式转换、图片处理、内容去重、备份校验、分类清理和任务流水线等高频操作集中到一个应用里，所有处理均在本机完成，不上传文件、不联网、不调用外部 API。")
p("作品把“安全”做成统一底座：所有批量操作遵循“先预览、再执行、可回退”，执行前生成计划与日志，完成后可在回退中心按批次恢复。技术上采用 .NET 8 + WPF、手写 MVVM，无第三方运行时依赖；通过自包含单文件发布，目标电脑双击即用。当前版本 11.0.3，主程序约 68.7 MB，核心自测 182 项通过，14 套流水线模板完成端到端验证。")
p("本项目的开发过程本身是 AIGC 应用实践：AI 承担需求拆解、界面与业务代码生成、文档整理和测试脚本编写；团队负责产品判断、技术决策、人工核查与结果验收。作品本体不包含生成式 AI 模型，也不把用户文件发送到任何在线服务。", after=8)
p("关键词：离线文件处理；可回退；任务流水线；WPF；AIGC 辅助开发", 10, True, None, None, 4, 6, 1.3, False)

# ---------- 1 ----------
doc.add_heading("一、项目背景与问题", level=1)
doc.add_heading("1.1 真实场景", level=2)
p("课程资料、社团照片、交付文档和项目素材通常分散在多个文件夹中。一次整理往往涉及几十到上百个文件：改名、转换格式、压缩图片、去重、备份、生成清单。单次操作看似简单，但重复次数多、出错代价高，用户常常宁可手动处理，也不敢使用批量工具。")
p("团队回收 27 份有效问卷，受访者以学生和刚入职的年轻用户为主。调研中，“省时、少步骤、结果准”是最核心的诉求，“隐私安全、离线可用”排在前列；用户最大的心理门槛不是功能多少，而是“装起来麻烦、跑起来卡、出错后无法恢复”。")
doc.add_heading("1.2 现有工具的不足", level=2)
for text in [
    "批量操作缺少可靠的回退机制，改错文件名后很难恢复。",
    "格式转换、图片压缩、去重、校验等能力分散在多个工具中，流程割裂。",
    "在线工具需要上传文件，隐私与网络条件不可控。",
    "专业工具安装复杂、体积大，不适合 U 盘携带和临时使用。",
]:
    bullet(text)

# ---------- 2 ----------
doc.add_heading("二、作品介绍", level=1)
doc.add_heading("2.1 产品定位", level=2)
p("Locifab 的定位是“完全离线、单文件运行、可回退的 Windows 文件工作台”。核心原则是：不联网、不装环境、不占大量内存、U 盘即插即用；所有文件处理在本机完成，数据不出本机。")
doc.add_heading("2.2 主要功能", level=2)
table(
    ["功能组", "能力"],
    [
        ["文件整理", "批量重命名、扫描分类、备份同步、压缩解压、空目录清理、时间与属性、拆分合并"],
        ["文本与内容", "文本替换、元数据清理、内容去重、相似照片"],
        ["图片与媒体", "图片压缩、图片检查、图片批处理、Office / PDF 内嵌图片提取"],
        ["校验与报告", "SHA-256 校验、文件夹清单、CSV / HTML 导出"],
        ["自动化", "任务流水线、功能配置模板、回退中心"],
    ],
    [3.0, 13.0]
)
p("当前版本提供 14 套内置流水线模板和 19 个功能配置模板，用户也可以把多个步骤组合成自己的流程，保存后重复使用。")

doc.add_heading("2.3 统一安全流程", level=2)
p("所有批量操作都遵循同一套流程：先在只读模式下生成预览，标明每个文件是“可执行、覆盖、冲突、无变化”还是“无效”；用户确认后才写入操作日志并执行；执行失败时尽量回滚，完成后可在回退中心恢复。日志保存在本机，不依赖系统撤销，也不受时间限制。")
p("这条流程覆盖重命名、移动、转换、压缩、提取、校验和流水线，用户可以放心尝试规则，而不必担心一次误操作破坏资料。")

# ---------- 3 ----------
doc.add_heading("三、创新点", level=1)
doc.add_heading("3.1 把“可回退”做成底层能力", level=2)
p("多数批量工具把撤销当作附加功能，窗口关闭或系统重启后失效。Locifab 把操作前后的路径映射写入本机日志，回退时逆向执行，覆盖重命名、移动、转换、压缩和导出等动作，解决用户“敢不敢用”的问题。")
doc.add_heading("3.2 用内容相似度去重", level=2)
p("传统去重依赖文件名或字节哈希，文件改名、换格式后就识别不出来。Locifab 提取文档正文，使用中文二元组与 64 位 SimHash 计算相似度，按汉明距离分组，能识别“内容相同但文件名或格式不同”的资料。")
doc.add_heading("3.3 用流水线组合零散操作", level=2)
p("用户可以把重命名、检查、压缩、转换、校验、备份、清单等步骤保存为流水线，一键重复执行。导入的功能配置只显示“模板：名称”，参数在后台使用保存时的快照，既简洁又可复现。")

# ---------- 4 ----------
doc.add_heading("四、技术方案", level=1)
doc.add_heading("4.1 架构", level=2)
table(
    ["层次", "职责", "关键技术"],
    [
        ["LocalCraft.App", "WPF 界面、导航、页面、ViewModel", "手写 MVVM、ClearType、动效"],
        ["LocalCraft.Core", "文件扫描、重命名、日志、回退、备份、校验", "System.Text.Json、SHA-256、SimHash"],
        ["LocalCraft.Conversion", "图片 / 文档转换、PDF 输出、元数据清理", "WIC、OOXML、本地排版"],
    ],
    [4.0, 8.0, 4.0]
)
doc.add_heading("4.2 技术选型", level=2)
p("选择 .NET 8 + WPF 的原因是可自包含发布、无需目标机安装运行库，同时具备成熟的 Windows 文件与图像能力。项目不引入第三方 NuGet 依赖，降低供应链与体积风险；核心层不依赖 WPF，便于命令行自测。")
doc.add_heading("4.3 关键实现", level=2)
for text in [
    "只读规划：预览阶段只计算新旧状态，不创建目录、不写文件。",
    "日志先行：执行前落盘操作日志，执行中逐项更新状态，失败时补偿。",
    "本地转换：WIC 处理图片，纯代码读写 OOXML，PDF 由本地排版引擎生成。",
    "流水线：在源目录工作副本上执行，逐步记录日志，输出写入独立目录。",
]:
    bullet(text)
p("受限于离线与体积约束，视频裁剪暂未实现；PDF 内嵌图片只提取可识别的 JPEG 流；HEIC / RAW 依赖 Windows 系统解码器。这些边界在文档中如实说明。")

# ---------- 5 AIGC ----------
doc.add_heading("五、AIGC 使用说明", level=1)
p("按照大赛要求，本节说明项目中生成式 AI 的使用方式、参与环节和人工处理情况。")
doc.add_heading("5.1 使用的工具与环节", level=2)
table(
    ["工具", "使用环节", "用途"],
    [
        ["DeepSeek", "需求、设计、编码、排错", "拆解功能、生成界面与业务代码、解释报错、编写测试与脚本"],
        ["豆包", "文档与汇报", "梳理调研结论、润色项目文档、调整格式与措辞"],
        ["AI 编码助手", "开发与验证", "生成页面、样式、动效、测试用例和发布脚本"],
    ],
    [3.0, 4.0, 9.0]
)
doc.add_heading("5.2 人工负责的部分", level=2)
p("产品定位、市场缺口判断、技术选型、可回退流程、模块边界、验收标准和最终取舍由团队讨论确定。AI 负责把明确的需求转化为可运行代码和文档初稿，团队逐项运行、检查、修改，不把“生成完成”当作“交付完成”。")
doc.add_heading("5.3 核查与声明", level=2)
p("所有 AI 生成内容均经过人工核查：代码通过编译与自测，界面在 100%–200% 缩放下实际运行，文档中的数据能追溯到调研、测试或发布记录。项目未使用他人的代码、素材或数据；作品本体不包含 AI 模型，不使用在线 API 处理用户文件。")
p("AI 生成内容主要集中在界面布局、样式、业务代码、测试脚本和文档初稿；核心判断与最终文案由团队确认。", 10, False, RGBColor(0x55, 0x55, 0x55))

# ---------- 6 ----------
doc.add_heading("六、测试与效果", level=1)
p("项目使用独立的核心自测和流水线端到端测试，测试数据包含 434 个文件、19 个功能配置和 14 套流水线模板。")
table(
    ["测试项", "结果"],
    [
        ["核心自测", "182 / 182 通过"],
        ["流水线模板", "14 / 14 通过"],
        ["功能配置解析", "19 / 19 通过"],
        ["测试文件", "434 个，约 9.5 MB"],
        ["应用启动 / MSIX", "通过；MSIX 打包、签名、解包通过"],
    ],
    [6.0, 10.0]
)
p("测试中发现并修复了坏图导致元数据/转换中断、分类与重命名同名冲突、流水线导入参数丢失等问题。当前版本在真实文件夹上完成了重命名、转换、压缩、去重、备份、校验和流水线执行验证。")

doc.add_picture(str(ROOT / "docs/screenshots/v11-home.png"), width=Inches(6.2))
doc.paragraphs[-1].alignment = WD_ALIGN_PARAGRAPH.CENTER
p("图 1　Locifab 首页：常用功能与离线状态", 9, False, RGBColor(0x66, 0x66, 0x66), WD_ALIGN_PARAGRAPH.CENTER, 6, 2, 1.2, False)

doc.add_picture(str(ROOT / "docs/screenshots/v11-pipeline.png"), width=Inches(6.2))
doc.paragraphs[-1].alignment = WD_ALIGN_PARAGRAPH.CENTER
p("图 2　任务流水线：步骤、模板与导入配置", 9, False, RGBColor(0x66, 0x66, 0x66), WD_ALIGN_PARAGRAPH.CENTER, 6, 2, 1.2, False)

# ---------- 7 ----------
doc.add_heading("七、团队分工与开发过程", level=1)
table(
    ["角色", "主要工作"],
    [
        ["队长 / 产品", "需求调研、产品定位、功能优先级、验收标准、对外汇报"],
        ["交互与视觉", "页面结构、导航与动效、图标、高 DPI 适配、演示材料"],
        ["算法与核心", "重命名、日志、回退、去重、摘要、校验等核心逻辑"],
        ["工程与测试", "转换、图片处理、流水线、测试数据、发布与回归"],
    ],
    [4.0, 12.0]
)
p("开发按“调研 → 设计 → 安全底座 → 功能模块 → 体验优化 → 测试发布”推进。每个阶段都先写验收标准，再让 AI 生成实现，团队运行验证后合并；Prompt、修改记录和测试输出保留在仓库中，便于追溯。")

# ---------- 8 ----------
doc.add_heading("八、总结与展望", level=1)
p("Locifab 从真实的本地文件整理问题出发，把“可回退”做成所有批量操作的统一底座，再向上组合重命名、转换、图片、去重、备份、校验、分类和流水线。它不追求功能数量，而是让用户敢用、愿用、用得放心。")
p("后续计划包括：在离线条件下补充视频处理能力；增强 PDF 文本层和复杂排版；完善流水线的失败跳过与重试策略；继续优化大目录扫描性能。团队会坚持离线、可回退和可验证三条底线。")
p("作品本体不包含生成式 AI，AI 仅作为开发与文档协作工具；这一点也是本项目对 AIGC 应用边界的具体实践。")

# ---------- appendix ----------
doc.add_heading("附录　提交材料", level=1)
for text in [
    "可运行程序：Locifab-11.0.3/Locifab.exe（自包含单文件，约 68.7 MB）。",
    "安装包：Locifab-Store-11.0.3.msix（用于 Microsoft Store 提交）。",
    "源代码：src/、tests/、scripts/，附 README 与发布流程。",
    "测试材料：434 个测试文件、19 个功能配置、14 套流水线模板。",
    "文档与截图：答辩文档、测试报告、商店文案、界面截图。",
]:
    bullet(text)
p("Locifab 团队", 10.5, True, None, WD_ALIGN_PARAGRAPH.RIGHT, 2, 18, 1.2, False)
p("2026 年 10 月", 10.5, False, None, WD_ALIGN_PARAGRAPH.RIGHT, 2, 0, 1.2, False)

DOCX.parent.mkdir(parents=True, exist_ok=True)
doc.save(DOCX)
print("saved", DOCX, "paragraphs", len(doc.paragraphs), "tables", len(doc.tables))
