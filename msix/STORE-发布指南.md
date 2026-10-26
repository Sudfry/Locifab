# Locifab MSIX · Microsoft Store 发布指南

- 包：`Locifab-11.0.3.msix`
- 版本：`11.0.3.0`
- 架构：x64
- 类型：完整信任 Win32 桌面应用（Full Trust）
- 能力：`runFullTrust`

---

## 1. 当前包的状态

| 检查项 | 结果 |
|---|---|
| MakeAppx 打包 | 通过 |
| 清单校验 | 通过 |
| 签名 | 通过 |
| 签名验证 | 通过 |
| 解包校验 | 通过 |
| 从包目录启动 EXE | 通过 |
| 当前机器安装 | 受旁加载/证书策略限制 |

说明：

- 当前包使用**测试自签名证书** `CN=Sudfry`。
- 该证书只适合本机测试，不适合直接作为正式 Store 发布证书。
- 当前机器未开启开发者模式/旁加载，因此无法完成 `Add-AppxPackage` 安装验证。
- 这不代表包结构有问题；Store 提交时会由 Partner Center 校验。

---

## 2. 提交前必须替换的身份

当前清单是占位身份：

```xml
<Identity
    Name="Sudfry.Locifab"
    Publisher="CN=Sudfry"
    Version="11.0.3.0"
    ProcessorArchitecture="x64" />
```

在 Partner Center 创建应用后，会得到类似：

```text
Package/Identity/Name:      12345Sudfry.Locifab
Package/Identity/Publisher: CN=ABCDEF12-3456-7890-ABCD-1234567890AB
```

把这两个值替换到 `msix/layout/AppxManifest.xml`，或使用重新打包脚本。

---

## 3. 重新打包

### 准备文件

```powershell
dotnet publish .\src\LocalCraft.App\LocalCraft.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=false -p:PublishTrimmed=false `
  -o .\publish\msix-x64
```

### 不带签名打包

```powershell
powershell -ExecutionPolicy Bypass -File .\msix\build-msix.ps1
```

### 带证书打包

```powershell
powershell -ExecutionPolicy Bypass -File .\msix\build-msix.ps1 `
  -PackageName "PartnerCenter.Identity.Name" `
  -Publisher "CN=PartnerCenter.Publisher" `
  -Version "11.0.3.0" `
  -CertPath "C:\path\store-signing.pfx" `
  -CertPassword "证书密码" `
  -Output "Locifab-Store-11.0.3.msix"
```

---

## 4. 签名证书

Store 包必须满足：

- 证书 Subject 的 `CN` 与清单 `Publisher` 一致。
- 使用代码签名证书（Code Signing）。
- 建议 SHA-256。

本机测试证书：

```text
msix/Locifab-Test.cer
```

仅包含公钥，可用于把测试证书加入信任。**私钥 PFX 不随交付包分发。**

本机信任测试证书（管理员 PowerShell）：

```powershell
certutil -addstore TrustedPeople .\msix\Locifab-Test.cer
certutil -addstore Root .\msix\Locifab-Test.cer
```

然后安装：

```powershell
Add-AppxPackage .\msix\Locifab-11.0.3.msix
```

---

## 5. Store 提交步骤

1. 登录 <https://partner.microsoft.com/dashboard>。
2. 创建新应用，保留应用名称 `Locifab`。
3. 进入 **Packages**，记录 Identity Name 与 Publisher。
4. 用这两个值重新打包并签名。
5. 上传 `Locifab-Store-11.0.3.msix`。
6. 填写：
   - 应用名称：Locifab 本地文件工坊
   - 分类：Utilities & tools
   - 年龄分级
   - 隐私政策
   - 支持链接：<https://github.com/Sudfry/Locifab>
7. 提交认证。

> 如果 Partner Center 页面要求 `.msixupload`，请使用 Visual Studio 的 **Create App Packages → Microsoft Store** 生成上传包；`.msix` 与 `.msixupload` 内容同源，只是封装格式不同。

---

## 6. Store 资料建议

### 简短描述

> 完全离线的 Windows 文件处理工作台：批量重命名、格式转换、图片压缩、去重、校验、备份与可回退流水线。

### 详细描述

> Locifab 将常用文件操作集中到一个本地应用：重命名、分类、压缩解压、备份、校验、清单、图片处理、内容去重、相似照片、拆分合并与任务流水线。所有操作先预览、再执行、可回退，文件不上传、不联网。
>
> 支持批量操作日志、回退中心、功能配置模板与流水线模板。单文件自包含，适合个人与小型团队整理资料。

### 隐私政策

Locifab 不收集、不上传任何文件或个人信息。所有处理在本机完成。

可放置在仓库中的 `PRIVACY.md`，并把链接填入 Partner Center。

---

## 7. 认证前检查

- [ ] 清单 Identity 与 Partner Center 一致
- [ ] 版本号高于上一次提交
- [ ] 证书 Subject 与 Publisher 一致
- [ ] 签名验证通过
- [ ] x64 包可启动
- [ ] 图标完整：50、44、150、310、Wide310x150、Splash
- [ ] 隐私政策链接可访问
- [ ] 支持链接可访问
- [ ] 没有测试证书、测试数据、私钥被打进包
- [ ] 包内没有 `Document/`、`tests/`、`*.pfx`

---

## 8. 常见失败

### 0x800B0109

根证书不受信任。管理员执行：

```powershell
certutil -addstore TrustedPeople .\msix\Locifab-Test.cer
certutil -addstore Root .\msix\Locifab-Test.cer
```

### 0x80073CFF

未开启开发者模式/旁加载。测试机需要：

- 设置 → 隐私和安全性 → 开发者选项 → 开发人员模式；或
- 使用已信任的正式签名证书。

### 0x80073CF0

包无法打开，通常是清单/签名/文件缺失。重新运行：

```powershell
makeappx unpack /p Locifab-11.0.3.msix /d unpacked
signtool verify /pa /v Locifab-11.0.3.msix
```
