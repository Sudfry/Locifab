# Locifab · 本地文件工坊

一款**完全离线**的 Windows 桌面文件处理工具。所有处理在本机完成，不联网、不上传、不调用外部 API。

- 作者：**Sudfry**
- 仓库：<https://github.com/Sudfry/Locifab>
- 版本：**11.0.1**
- 平台：Windows 10 / 11 x64
- 技术栈：C# / .NET 8 / WPF（手写 MVVM，无第三方 NuGet 依赖）

---

## 功能

### 文件整理

- 批量重命名：前缀、后缀、查找替换、序号、排序。
- 扫描与分类：按类型/年份/月份/名称前缀规划移动。
- 备份同步：只增不删、严格比对、SHA-256。
- 实时监控：只记录变化，不自动处理。
- 压缩与解压：ZIP 压缩、解压密码、路径穿越防护。
- 空目录清理。
- 时间与属性：修改时间、只读、隐藏、存档。
- 校验与清单：SHA-256 清单生成/校验。
- 文件夹清单：目录树 + CSV/HTML 导出。
- 拆分与合并。

### 文本与内容

- 文本替换：正则、扩展名范围、备份与回退。
- 元数据清理：DOCX/XLSX/PPTX 属性、图片 EXIF。
- 内容去重：中文二元组 + SimHash。
- 相似照片：感知哈希 + 阈值分组。

### 图片

- 图片批处理：缩放、格式、质量、水印。
- 图片压缩：目标 KB，自动质量与尺寸下降。
- 图片检查：损坏/截断检测与隔离。
- 内嵌图片：Office 媒体提取、PDF 内嵌 JPEG。

### 自动化

- 任务流水线：步骤编排、预览、执行、日志、模板。
- 功能配置：保存/应用/导出/导入当前工具设置。
- 流水线导入：按“功能 → 模板”导入为步骤。
- 回退中心：逐条记录、删除、逆向恢复。

---

## 安全设计

所有批量操作遵循：

```text
先预览 → 再执行 → 可回退
```

- 执行前生成操作日志。
- 移动/重命名记录旧路径与新路径。
- 转换/提取/压缩记录输出路径。
- 回退中心按批次恢复。
- 流水线在源目录工作副本上执行。

---

## 项目结构

```text
src/
  LocalCraft.App/         WPF 界面、导航、页面、ViewModel
  LocalCraft.Core/        文件操作、日志、回退、备份、校验
  LocalCraft.Conversion/  图片/文档转换引擎
tests/
  LocalCraft.SelfTest/    核心自测（182 项）
  Harness/                流水线端到端测试
  artifacts/              测试数据与模板
docs/
  答辩文档.md
  测试报告.md
  screenshots/
scripts/
  publish-win-x64.ps1
  run-full-test.ps1
```

---

## 构建

```powershell
dotnet build .\LocalCraft.sln -c Release
```

### 发布单文件

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish-win-x64.ps1
```

产物：`publish\win-x64\Locifab.exe`，自包含，无需安装 .NET。

---

## 测试

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run-full-test.ps1
```

包含：

- 核心自测 182 项。
- 14 套流水线模板真实执行。
- 19 个功能配置解析。

测试报告见 [docs/测试报告.md](docs/测试报告.md)。

---

## 测试数据

`tests/artifacts/测试数据与模板.zip` 包含：

- 434 个测试文件。
- 19 个功能配置。
- 14 套流水线模板。

生成脚本：

```powershell
python tests/gen_testdata_10_1.py
python tests/gen_testtemplates_10_1.py
```

---

## 已知限制

- 视频裁剪未实现：未内置视频编码器，避免生成损坏文件。
- PDF 内嵌图片只提取可识别 JPEG 图像流。
- HEIC/RAW 依赖 Windows 系统解码器。
- 内置 PDF 排版以视觉还原为主。

---

## 许可证

MIT License，见 [LICENSE](LICENSE)。
