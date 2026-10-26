from pathlib import Path
import json

ROOT = Path("tests/流水线测试模板-10.1.17")
ROOT.mkdir(parents=True, exist_ok=True)

def step(kind, name=None, text1="", text2="", text3="", n1=0, n2=0, choice="", b1=False, b2=False, template=""):
    return {
        "kind": kind,
        "name": name or kind,
        "enabled": True,
        "text1": text1, "text2": text2, "text3": text3,
        "number1": n1, "number2": n2,
        "choice": choice, "bool1": b1, "bool2": b2,
        "templateName": template,
    }

def preset(tool, name, values, st):
    return {"toolKey": tool, "name": name, "values": values, "step": st}

tool_presets = [
    preset("rename", "交付前缀+序号", {
        "Prefix": "交付_", "Suffix": "", "StartNumberText": "1",
        "NumberPaddingText": "3", "NumberSeparator": "_", "UseNumbering": "True"
    }, step("Rename", "批量重命名", text1="交付_", text2="", text3="_", n1=1, n2=3, b1=True)),
    preset("imgcompress", "图片500KB", {
        "TargetKb": "500", "ImageFolder": "", "ImageOutput": ""
    }, step("ImageTarget", "图片目标压缩", text1="图片压缩", n1=500)),
    preset("imgcheck", "损坏隔离", {
        "IntegrityFolder": ""
    }, step("ImageCheck", "图片完整性", text1="损坏图片隔离")),
    preset("embedimg", "Office素材", {
        "OfficeFolder": "", "OfficeOutput": ""
    }, step("EmbeddedImages", "内嵌图片提取", text1="内嵌图片")),
    preset("convert", "转PDF", {
        "OutputDirectory": "", "TargetFormat": "pdf"
    }, step("Convert", "格式转换", text1="转换结果", choice="pdf")),
    preset("findrep", "旧项目名替换", {
        "FindText": "旧项目名", "ReplaceText": "新项目名",
        "AnyExtension": "True", "IncludeExtensions": ""
    }, step("TextReplace", "文本替换", text1="旧项目名", text2="新项目名")),
    preset("metadata", "隐私副本", {
        "OutputFolder": ""
    }, step("Metadata", "元数据清理", text1="去元数据")),
    preset("checksum", "SHA-256清单", {
        "ManifestPath": ""
    }, step("Checksum", "SHA-256 校验", text1="Locifab-SHA256.txt", choice="SHA-256")),
    preset("report", "清单CSV哈希", {
        "OutputPath": "", "IncludeHash": "True"
    }, step("Report", "清单报告", text1="文件夹清单.csv", choice="Csv", b1=True)),
    preset("classify", "按类型分类", {
        "FolderPath": ""
    }, step("Classify", "分类移动", choice="ByType")),
    preset("backup", "只增备份", {
        "SourceFolder": "", "TargetFolder": ""
    }, step("Backup", "备份副本", text1="备份")),
    preset("dedup", "精确去重隔离", {
        "QuarantineDirectory": ""
    }, step("Dedup", "内容去重", text1="重复隔离")),
    preset("photos", "相似照片92%", {
        "QuarantineDirectory": "", "ThresholdPercent": "92"
    }, step("PhotoDedup", "相似照片", text1="相似照片隔离", n1=92)),
    preset("images", "图片批处理1920", {
        "OutputFolder": "", "SizeValue": "1920", "JpegQuality": "88"
    }, step("ImageBatch", "图片批处理", text1="图片输出", n1=1920, n2=88, choice="Keep")),
    preset("archive", "ZIP压缩", {
        "Password": "", "IsCompress": "True"
    }, step("Archive", "ZIP 处理", text1="Locifab-归档.zip", choice="Compress", b1=True)),
    preset("template", "客户交付目录", {
        "Template": "客户/\n  合同/\n  交付物/\n  验收/\n交付说明.md"
    }, step("Template", "目录模板", text1="客户/|合同/|交付物/|验收/|交付说明.md")),
    preset("filestate", "统一修改时间", {
        "FixedTime": "2026-01-01 09:00:00", "SetReadOnly": "False", "SetHidden": "False"
    }, step("FileState", "时间与属性", text1="2026-01-01 09:00:00")),
    preset("splitmerge", "按10MB拆分", {
        "OutputFolder": "", "SizeMb": "10"
    }, step("SplitMerge", "拆分与合并", text1="拆分合并结果", choice="Split", n1=10)),
    preset("cleanup", "空目录清理", {
        "Folder": ""
    }, step("Cleanup", "清理空目录", b1=True, b2=True)),
]

pipeline_presets = [
    {
        "name": "全流程-输入整理交付",
        "sourceFolder": "", "outputFolder": "",
        "steps": [
            step("Rename", "批量重命名", text1="交付_", text3="_", n1=1, n2=3, b1=True),
            step("ImageCheck", "图片完整性", text1="损坏图片隔离"),
            step("ImageTarget", "图片目标压缩", text1="图片压缩", n1=800),
            step("Report", "清单报告", choice="Csv", b1=True),
            step("Archive", "ZIP 处理", text1="交付包.zip", choice="Compress", b1=True),
        ],
    },
    {
        "name": "素材库清洗",
        "sourceFolder": "", "outputFolder": "",
        "steps": [
            step("ImageCheck", "图片完整性", text1="损坏图片隔离"),
            step("ImageTarget", "图片目标压缩", text1="图片压缩", n1=500),
            step("PhotoDedup", "相似照片", text1="相似照片隔离", n1=92),
            step("Report", "清单报告", choice="Csv", b1=True),
        ],
    },
    {
        "name": "文档交付与隐私",
        "sourceFolder": "", "outputFolder": "",
        "steps": [
            step("Convert", "格式转换", text1="转换结果", choice="pdf"),
            step("Metadata", "元数据清理", text1="去元数据"),
            step("Checksum", "SHA-256 校验", text1="Locifab-SHA256.txt", choice="SHA-256"),
            step("Report", "清单报告", choice="Csv", b1=True),
        ],
    },
    {
        "name": "备份前审计",
        "sourceFolder": "", "outputFolder": "",
        "steps": [
            step("Checksum", "SHA-256 校验", text1="Locifab-SHA256.txt", choice="SHA-256"),
            step("Backup", "备份副本", text1="备份"),
            step("Report", "清单报告", choice="Csv", b1=True),
        ],
    },
    {
        "name": "导入配置模块组合",
        "sourceFolder": "", "outputFolder": "",
        "steps": [
            step("Rename", "批量重命名", text1="交付_", text3="_", n1=1, n2=3, b1=True, template="交付前缀+序号"),
            step("ImageTarget", "图片目标压缩", text1="图片压缩", n1=500, template="图片500KB"),
            step("Checksum", "SHA-256 校验", text1="Locifab-SHA256.txt", choice="SHA-256", template="SHA-256清单"),
        ],
    },
]

(ROOT / "功能配置模板.json").write_text(json.dumps(tool_presets, ensure_ascii=False, indent=2), encoding="utf-8")
(ROOT / "流水线模板.json").write_text(json.dumps(pipeline_presets, ensure_ascii=False, indent=2), encoding="utf-8")

readme = """# 流水线测试模板 10.1.17

## 文件
- 功能配置模板.json：19 个工具配置，可通过顶部“配置 → 导入”导入。
- 流水线模板.json：5 套流水线模板，适合测试步骤、参数、导入模块。

## 导入功能配置
1. 打开任意功能页。
2. 顶部“配置” → 导入。
3. 选择 功能配置模板.json。
4. 每个功能会在对应页面显示自己的配置。

## 导入流水线模板
1. 关闭 Locifab。
2. 备份 `%LOCALAPPDATA%\\Locifab\\pipeline-presets.json`。
3. 把 流水线模板.json 复制为 `%LOCALAPPDATA%\\Locifab\\pipeline-presets.json`。
4. 启动 Locifab → 任务流水线 → 模板/预设。

如果程序改用便携数据目录，则目标是 `LocifabData\\pipeline-presets.json`。

## 建议测试顺序
1. 用 `00-流水线输入` 测全流程。
2. 用 `02-图片压缩` 测图片目标 KB。
3. 用 `03-图片检查` 测损坏隔离。
4. 用 `04-内嵌图片` 测 Office 图片提取。
5. 用 `10-内容去重` 和 `11-相似照片` 测去重。
6. 用 `09-拆分合并` 测大文件拆分。
"""
(ROOT / "使用说明.md").write_text(readme, encoding="utf-8")
print(ROOT, len(tool_presets), len(pipeline_presets))
